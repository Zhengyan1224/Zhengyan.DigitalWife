using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed record AndroidScriptPrecompileEntry(string Source, string Assembly, string Sha256);

internal sealed record AndroidScriptPrecompileResult(IReadOnlyList<AndroidScriptPrecompileEntry> Entries)
{
    public string ManifestPath { get; init; } = string.Empty;
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

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Android C# precompile failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        string manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            globalsContract = typeof(AndroidScriptGlobalsContract).Assembly.GetName().Name,
            scripts = entries
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new AndroidScriptPrecompileResult(entries) { ManifestPath = manifestPath };
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
            + "using Zhengyan.DigitalWife.GamePlayer.Runtime;\n"
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release),
            returnType: typeof(object),
            globalsType: typeof(AndroidScriptGlobalsContract));

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            string diagnostics = string.Join(Environment.NewLine, result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
            throw new InvalidOperationException(diagnostics);
        }

        return image.ToArray();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        Assembly[] requiredAssemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Task).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(Vector3).Assembly,
            typeof(AndroidScriptGlobalsContract).Assembly,
            typeof(GameProject).Assembly,
            typeof(RuntimeScene).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly
        ];
        foreach (Assembly assembly in requiredAssemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            if (assembly.IsDynamic || !identities.Add(assembly.FullName ?? assembly.GetName().Name ?? string.Empty)) continue;
            if (string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)) continue;
            yield return MetadataReference.CreateFromFile(assembly.Location);
        }
    }
}
