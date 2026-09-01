using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed record DesktopScriptPrecompileEntry(string Source, string Assembly, string Sha256);

internal sealed record DesktopScriptPrecompileResult(IReadOnlyList<DesktopScriptPrecompileEntry> Entries)
{
    public string ManifestPath { get; init; } = string.Empty;
}

internal static class DesktopCSharpScriptPrecompiler
{
    private const string OutputRoot = "compiled/desktop";

    public static DesktopScriptPrecompileResult Precompile(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);

        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        Type globalsType = LoadDesktopGlobalsType();
        HashSet<string> scriptPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in project.Scenes.Append(project.EditorScene)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
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
        List<DesktopScriptPrecompileEntry> entries = [];
        List<string> errors = [];
        foreach (string sourcePath in scriptPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string relativeSource = Path.GetRelativePath(fullProjectDirectory, sourcePath).Replace('\\', '/');
                string relativeAssembly = Path.ChangeExtension(relativeSource, ".dll");
                string assemblyPath = Path.Combine(outputDirectory, relativeAssembly.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                byte[] image = Compile(sourcePath, globalsType);
                File.WriteAllBytes(assemblyPath, image);
                entries.Add(new DesktopScriptPrecompileEntry(
                    relativeSource,
                    $"{OutputRoot}/{relativeAssembly}",
                    Convert.ToHexString(SHA256.HashData(image))));
            }
            catch (Exception ex)
            {
                errors.Add($"{sourcePath}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Desktop C# precompile failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        string manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            globalsContract = globalsType.Assembly.GetName().Name,
            scripts = entries
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new DesktopScriptPrecompileResult(entries) { ManifestPath = manifestPath };
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

    private static byte[] Compile(string path, Type globalsType)
    {
        string scriptSource = File.ReadAllText(path);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptSource)));
        string compilationBody = string.IsNullOrWhiteSpace(scriptSource) ? "return null;" : scriptSource;
        string source = "using System;\n"
            + "using System.Collections.Generic;\n"
            + "using System.Globalization;\n"
            + "using System.IO;\n"
            + "using System.Linq;\n"
            + "using System.Net;\n"
            + "using System.Net.Http;\n"
            + "using System.Net.Sockets;\n"
            + "using System.Numerics;\n"
            + "using System.Text;\n"
            + "using System.Text.Json;\n"
            + "using System.Text.RegularExpressions;\n"
            + "using System.Threading;\n"
            + "using System.Threading.Tasks;\n"
            + "using Zhengyan.DigitalWife.Mmd.Game.Pmx;\n"
            + "using Zhengyan.DigitalWife.GamePlayer;\n"
            + "\n"
            + compilationBody;
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script),
            path);
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation(
            "DesktopScript_" + sourceSha256,
            syntaxTree,
            GetMetadataReferences(globalsType),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release),
            returnType: typeof(object),
            globalsType: globalsType);

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

    private static Type LoadDesktopGlobalsType()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.GamePlayer.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                "The desktop GamePlayer script contract is unavailable. Rebuild GameEditor before exporting the package.",
                assemblyPath);
        }

        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        return assembly.GetType("Zhengyan.DigitalWife.GamePlayer.CSharpScriptGlobals", throwOnError: true)!;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences(Type globalsType)
    {
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        Assembly[] requiredAssemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Task).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(StringBuilder).Assembly,
            typeof(JsonSerializer).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,
            typeof(System.Net.IPAddress).Assembly,
            typeof(System.Net.Http.HttpClient).Assembly,
            typeof(System.Net.Sockets.TcpClient).Assembly,
            typeof(Vector3).Assembly,
            globalsType.Assembly,
            typeof(GameProject).Assembly
        ];
        foreach (Assembly assembly in requiredAssemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            string name = assembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("Zhengyan.DigitalWife.", StringComparison.Ordinal)
                && name.EndsWith(".Core", StringComparison.Ordinal))
            {
                continue;
            }

            if (assembly.IsDynamic || !identities.Add(assembly.FullName ?? assembly.GetName().Name ?? string.Empty)) continue;
            if (string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)) continue;
            yield return MetadataReference.CreateFromFile(assembly.Location);
        }

        string llmAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.Llm.OpenAI.dll");
        foreach (string additionalAssemblyPath in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.Mmd.Game.dll"),
            llmAssemblyPath
        })
        {
            if (!File.Exists(additionalAssemblyPath)) continue;
            AssemblyName name = AssemblyName.GetAssemblyName(additionalAssemblyPath);
            if (identities.Add(name.FullName ?? name.Name ?? additionalAssemblyPath))
            {
                yield return MetadataReference.CreateFromFile(additionalAssemblyPath);
            }
        }
    }
}
