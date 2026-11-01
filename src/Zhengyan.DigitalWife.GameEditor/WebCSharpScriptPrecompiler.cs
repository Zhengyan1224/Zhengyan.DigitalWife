extern alias AndroidRuntime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using RuntimeCore = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed record WebScriptPrecompileEntry(string Source, string Assembly, string Sha256, string ApiVersion);

internal sealed record WebScriptPrecompileResult(IReadOnlyList<WebScriptPrecompileEntry> Entries)
{
    public string ManifestPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Compiles the existing .csx source into the small Web script ABI.</summary>
internal static class WebCSharpScriptPrecompiler
{
    private const string OutputRoot = "compiled/web";
    private const string ApiVersion = "1.0";

    public static WebScriptPrecompileResult Precompile(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);
        string root = Path.GetFullPath(projectDirectory);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in project.Scenes.Append(project.EditorScene).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameProjectScene scene = GameProjectStore.LoadScene(root, scenePath);
            AddBindings(scene.LoadingScripts, root, paths);
            foreach (GameEntity entity in scene.Entities) AddBindings(entity.Scripts, root, paths);
        }

        string outputRoot = Path.Combine(root, OutputRoot.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(outputRoot);
        List<WebScriptPrecompileEntry> entries = [];
        List<string> errors = [];
        foreach (string path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string source = Path.GetRelativePath(root, path).Replace('\\', '/');
                string relativeAssembly = Path.ChangeExtension(source, ".dll").Replace('\\', '/');
                string assembly = $"{OutputRoot}/{relativeAssembly}";
                string assemblyPath = Path.Combine(root, assembly.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
                byte[] image = Compile(path);
                File.WriteAllBytes(assemblyPath, image);
                entries.Add(new(source, assembly, Convert.ToHexString(SHA256.HashData(image)), ApiVersion));
            }
            catch (Exception exception) { errors.Add($"{path}: {exception.Message}"); }
        }

        string manifestPath = Path.Combine(outputRoot, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            apiVersion = ApiVersion,
            targetFramework = "net10.0-browser",
            scripts = entries,
            errors
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new WebScriptPrecompileResult(entries) { ManifestPath = manifestPath, Errors = errors };
    }

    private static void AddBindings(IEnumerable<ScriptBinding> bindings, string root, ISet<string> paths)
    {
        foreach (ScriptBinding binding in bindings.Where(binding => binding.Enabled && IsCSharp(binding.Language, binding.Path)))
        {
            string path = GameProjectPath.ToAbsolute(root, binding.Path);
            if (File.Exists(path)) paths.Add(Path.GetFullPath(path));
        }
    }

    private static byte[] Compile(string path)
    {
        string body = File.ReadAllText(path);
        if (string.Equals(body.Trim(), "return null;", StringComparison.Ordinal)) body = string.Empty;
        string source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Numerics;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using Zhengyan.DigitalWife.GameProjects;
            using Zhengyan.DigitalWife.GamePlayer.Runtime;
            using Zhengyan.DigitalWife.Mmd;

            namespace Zhengyan.GeneratedWebScripts;

            public sealed class Script : IWebGameScript
            {
                public void Start(WebScriptContext context) => Execute(context, 0.0f, true, false);
                public void Update(WebScriptContext context, float deltaSeconds) => Execute(context, deltaSeconds, false, true);
                private void Execute(WebScriptContext context, float DeltaSeconds, bool IsStart, bool IsUpdate)
                {
                    RuntimeScene Scene = context.Scene;
                    RuntimeEntity? Entity = context.Entity;
                    RuntimeCameraInput Input = context.Input;
                    {{body}}
                }
            }
            """;
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "WebScript_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).Substring(0, 16),
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            string diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(diagnostics);
        }
        return image.ToArray();
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        Assembly[] assemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(Vector3).Assembly,
            typeof(RuntimeCore.RuntimeScene).Assembly,
            typeof(GameProject).Assembly
        ];
        foreach (Assembly assembly in assemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location)) continue;
            if (paths.Add(assembly.Location)) yield return MetadataReference.CreateFromFile(assembly.Location);
        }
    }

    private static bool IsCSharp(string language, string path)
        => language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
            || language.Equals("cs", StringComparison.OrdinalIgnoreCase)
            || language.Equals("csx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);
}
