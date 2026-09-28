extern alias AndroidScripting;

using System.Security.Cryptography;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using AndroidScriptCompiler = AndroidScripting::Zhengyan.DigitalWife.GamePlayer.Android.AndroidScriptCompiler;
using AndroidScriptGlobals = AndroidScripting::Zhengyan.DigitalWife.GamePlayer.Android.AndroidScriptGlobals;

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
                byte[] image = AndroidScriptCompiler.Compile(sourcePath);
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
            version = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            globalsContract = typeof(AndroidScriptGlobals).Assembly.GetName().Name,
            scripts = entries,
            errors
        }, new JsonSerializerOptions { WriteIndented = true }));

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Android C# precompile failed:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors)
                + Environment.NewLine
                + $"Diagnostics were written to '{manifestPath}'.");
        }

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

}
