using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed record AndroidScriptPrecompileEntry(string Source, string Assembly, string Sha256);

internal sealed record AndroidScriptPrecompileResult(IReadOnlyList<AndroidScriptPrecompileEntry> Entries)
{
    public string ManifestPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
}

internal static class AndroidCSharpScriptPrecompiler
{
    private const string OutputRoot = "compiled/android";

    public static AndroidScriptPrecompileResult Precompile(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);

        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        HashSet<string> scriptPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in project.Scenes.Append(project.EditorScene).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameProjectScene scene = GameProjectStore.LoadScene(fullProjectDirectory, scenePath);
            AddBindings(scene.LoadingScripts, fullProjectDirectory, scriptPaths);
            foreach (GameEntity entity in scene.Entities)
            {
                AddBindings(entity.Scripts, fullProjectDirectory, scriptPaths);
            }
        }

        string outputDirectory = Path.Combine(fullProjectDirectory, OutputRoot.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outputDirectory);
        List<AndroidScriptPrecompileEntry> entries = [];
        List<string> errors = [];
        foreach (string sourcePath in scriptPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string relativeSource = Path.GetRelativePath(fullProjectDirectory, sourcePath).Replace('\\', '/');
                string relativeAssembly = Path.ChangeExtension(relativeSource, ".dll");
                string assemblyPath = Path.Combine(outputDirectory, relativeAssembly.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                byte[] image = Compile(sourcePath);
                File.WriteAllBytes(assemblyPath, image);
                entries.Add(new AndroidScriptPrecompileEntry(relativeSource, $"{OutputRoot}/{relativeAssembly}", Convert.ToHexString(SHA256.HashData(image))));
            }
            catch (Exception ex)
            {
                errors.Add($"{sourcePath}: {ex.Message}");
            }
        }

        string manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            globalsContract = typeof(AndroidScriptGlobalsContract).Assembly.GetName().Name,
            scripts = entries,
            errors
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new AndroidScriptPrecompileResult(entries)
        {
            ManifestPath = manifestPath,
            Errors = errors
        };
    }

    private static void AddBindings(IEnumerable<ScriptBinding> bindings, string projectDirectory, ISet<string> paths)
    {
        foreach (ScriptBinding binding in bindings.Where(binding => binding.Enabled && IsCSharp(binding.Language, binding.Path)))
        {
            string path = GameProjectPath.ToAbsolute(projectDirectory, binding.Path);
            if (File.Exists(path)) paths.Add(Path.GetFullPath(path));
        }
    }

    private static bool IsCSharp(string language, string path)
    {
        return string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "csx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".csx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Compile(string path)
    {
        string source = "using System;\n"
            + "using System.Numerics;\n"
            + "using System.Threading;\n"
            + "using System.Threading.Tasks;\n"
            + "using Zhengyan.DigitalWife.GameProjects;\n"
            + "using Zhengyan.DigitalWife.Mmd.Game.Pmx;\n\n"
            + File.ReadAllText(path);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script),
            path);
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation(
            "AndroidScript_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).Substring(0, 16),
            syntaxTree,
            GetMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                concurrentBuild: false),
            returnType: typeof(object),
            globalsType: typeof(AndroidScriptGlobalsContract));

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            // Emit diagnostics can be empty for script compilations when the
            // failure is produced by the compilation pipeline itself. Include
            // the complete compilation diagnostic set as a fallback so export
            // never fails with an opaque "no diagnostics" message.
            IEnumerable<Diagnostic> allDiagnostics = result.Diagnostics
                .Concat(compilation.GetDiagnostics())
                .GroupBy(diagnostic => diagnostic.ToString(), StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
            string diagnostics = string.Join(Environment.NewLine, allDiagnostics.Select(diagnostic => diagnostic.ToString()));
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                diagnostics = $"Roslyn did not emit an assembly (result.Success={result.Success}, " +
                    $"diagnosticCount={result.Diagnostics.Length}, compilationDiagnosticCount={compilation.GetDiagnostics().Length}).";
            }
            throw new InvalidOperationException(diagnostics);
        }

        return image.ToArray();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        Assembly[] requiredAssemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Task).Assembly,
            typeof(System.Runtime.CompilerServices.CallSite).Assembly,
            typeof(System.Linq.Expressions.Expression).Assembly,
            typeof(System.Dynamic.DynamicObject).Assembly,
            typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(Vector3).Assembly,
            typeof(AndroidScriptGlobalsContract).Assembly,
            typeof(GameProject).Assembly,
            typeof(PmxModelComponent).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly
        ];
        foreach (Assembly assembly in requiredAssemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)
                || !paths.Add(Path.GetFullPath(assembly.Location))) continue;
            yield return MetadataReference.CreateFromFile(assembly.Location);
        }

        // Keep the runtime binder reference explicit. On some .NET SDK layouts an
        // AppDomain-loaded facade with the same identity can otherwise hide the
        // implementation metadata required for dynamic globals in script submissions.
        string binderPath = typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location;
        if (File.Exists(binderPath) && paths.Add(Path.GetFullPath(binderPath)))
        {
            yield return MetadataReference.CreateFromFile(binderPath);
        }

        // Include the complete .NET shared-framework reference set. This is
        // required by dynamic script submissions on machines where the editor's
        // plugin load context does not have every runtime facade loaded yet.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(path) && paths.Add(Path.GetFullPath(path)))
                {
                    yield return MetadataReference.CreateFromFile(path);
                }
            }
        }

    }
}
