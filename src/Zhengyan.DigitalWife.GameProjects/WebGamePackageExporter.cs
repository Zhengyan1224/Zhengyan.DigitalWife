using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed record WebGamePackageResult(
    string OutputDirectory,
    string ManifestPath,
    int AssetCount,
    int ScriptCount,
    long TotalBytes);

public sealed record WebManifestSigningOptions(
    string PrivateKeyPem,
    string PublicKeyPem,
    string KeyId = "");

/// <summary>
/// Publishes a development project into the static directory consumed by the
/// Web player. The output contains only project-relative paths and a hashed
/// manifest; it never asks the browser to extract a Windows directory.
/// </summary>
public static class WebGamePackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static WebGamePackageResult Publish(
        string projectDirectory,
        string outputDirectory,
        bool overwrite = true,
        WebManifestSigningOptions? signing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string sourceRoot = Path.GetFullPath(projectDirectory);
        string targetRoot = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Web output must be different from the source project directory.");
        Directory.CreateDirectory(targetRoot);

        GameProject project = GameProjectStore.Load(sourceRoot);
        GameProjectStore.NormalizeScenes(project);
        GameProject webProject = Clone(project);
        NormalizeProjectPaths(webProject, sourceRoot);
        File.WriteAllBytes(Path.Combine(targetRoot, "game.project.json"), JsonSerializer.SerializeToUtf8Bytes(webProject, JsonOptions));

        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase) { "game.project.json" };
        foreach (string scenePath in webProject.Scenes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameProjectScene scene = GameProjectStore.LoadScene(sourceRoot, scenePath);
            NormalizeScenePaths(scene, sourceRoot);
            WriteJson(targetRoot, scenePath, scene);
            written.Add(NormalizeRelative(scenePath));
        }

        CopyProjectFiles(sourceRoot, targetRoot, written, overwrite);
        List<WebPackageAsset> assets = [];
        foreach (string file in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(targetRoot, file).Replace('\\', '/');
            if (string.Equals(relative, "web.manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] bytes = File.ReadAllBytes(file);
            assets.Add(new WebPackageAsset(relative, GetKind(relative), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }

        List<object> scripts = BuildScriptManifest(webProject, sourceRoot, assets);
        var manifestCore = new
        {
            formatVersion = 1,
            runtime = "web",
            renderer = "webgl2",
            apiVersion = "1.0",
            entryScene = NormalizeRelative(webProject.DefaultScene),
            assets,
            scripts,
            capabilities = new[] { "scene", "pmx", "vmd", "pmx-skinning", "pmx-morph", "pmx-ik", "webgl2", "pointer", "touch", "gui-events", "web-audio", "motion-control", "speech-callbacks", "precompiled-scripts" },
            keyId = signing?.KeyId ?? string.Empty
        };
        object manifest = manifestCore;
        if (signing is not null)
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(signing.PrivateKeyPem);
            byte[] signature = rsa.SignData(
                JsonSerializer.SerializeToUtf8Bytes(manifestCore, JsonOptions),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            manifest = new
            {
                manifestCore.formatVersion,
                manifestCore.runtime,
                manifestCore.renderer,
                manifestCore.apiVersion,
                manifestCore.entryScene,
                manifestCore.assets,
                manifestCore.scripts,
                manifestCore.capabilities,
                manifestCore.keyId,
                signatureAlgorithm = "RSA-SHA256",
                signature = Convert.ToBase64String(signature),
                publicKey = signing.PublicKeyPem
            };
        }
        string manifestPath = Path.Combine(targetRoot, "web.manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
        return new WebGamePackageResult(targetRoot, manifestPath, assets.Count, scripts.Count, assets.Sum(asset => asset.Size));
    }

    private static List<object> BuildScriptManifest(GameProject project, string sourceRoot, List<WebPackageAsset> assets)
    {
        List<object> scripts = [];
        HashSet<string> scriptIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string scenePath in project.Scenes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameProjectScene scene = GameProjectStore.LoadScene(sourceRoot, scenePath);
            IEnumerable<(ScriptBinding Binding, string Entity)> bindings =
                scene.LoadingScripts.Select(binding => (binding, string.Empty))
                    .Concat(scene.Entities.SelectMany(entity => entity.Scripts.Select(binding => (binding, entity.Id))));
            foreach ((ScriptBinding binding, string entityId) in bindings
                .Where(item => item.Binding.Enabled && (IsCSharp(item.Binding.Path, item.Binding.Language) || item.Binding.Language.Equals("python",StringComparison.OrdinalIgnoreCase))))
            {
                string source = NormalizeOptional(binding.Path, sourceRoot);
                if(binding.Language.Equals("python",StringComparison.OrdinalIgnoreCase))
                {
                    string id=$"{scenePath}:{entityId}:{source}";
                    if(scriptIds.Add(id) && assets.FirstOrDefault(a=>a.Path.Equals(source,StringComparison.OrdinalIgnoreCase)) is { } asset)
                        scripts.Add(new{id,scene=scenePath,assembly=source,language="python",sha256=asset.Sha256,entity=entityId,apiVersion="1.0",targetFramework="wasm-python",dependencies=Array.Empty<string>()});
                    continue;
                }
                string assembly = $"compiled/web/{Path.ChangeExtension(source, ".dll")}";
                string fullAssembly = Path.Combine(sourceRoot, assembly.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullAssembly)) continue;
                string scriptId = $"{scenePath}:{entityId}:{source}";
                if (!scriptIds.Add(scriptId)) continue;
                byte[] bytes = File.ReadAllBytes(fullAssembly);
                if (!assets.Any(asset => string.Equals(asset.Path, assembly, StringComparison.OrdinalIgnoreCase)))
                {
                    assets.Add(new WebPackageAsset(assembly, "assembly", bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
                }
                scripts.Add(new
                {
                    id = scriptId,
                    scene = scenePath,
                    assembly,
                    type = string.Empty,
                    factory = string.Empty,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    entity = entityId,
                    apiVersion = "1.0",
                    targetFramework = "net10.0-browser",
                    dependencies = Array.Empty<string>()
                });
            }
        }
        return scripts;
    }

    /// <summary>Refreshes a development Web export after recompiling scripts, without copying or replacing game data.</summary>
    public static WebGamePackageResult RefreshManifest(string outputDirectory)
    {
        string root = Path.GetFullPath(outputDirectory), manifestPath = Path.Combine(root, "web.manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        if (!string.IsNullOrEmpty(manifest["signature"]?.GetValue<string>()))
            throw new InvalidOperationException("A signed Web export must be re-exported with its signing key.");
        GameProject project = GameProjectStore.Load(root);
        List<WebPackageAsset> assets = [];
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (path == "web.manifest.json" || IsIgnored(path)) continue;
            using var stream = File.OpenRead(file);
            assets.Add(new(path, GetKind(path), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
        }
        var scripts = BuildScriptManifest(project, root, assets);
        manifest["assets"] = JsonSerializer.SerializeToNode(assets, JsonOptions);
        manifest["scripts"] = JsonSerializer.SerializeToNode(scripts, JsonOptions);
        File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions));
        return new(root, manifestPath, assets.Count, scripts.Count, assets.Sum(a => a.Size));
    }

    private static void CopyProjectFiles(string sourceRoot, string targetRoot, ISet<string> written, bool overwrite)
    {
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (IsIgnored(relative) || written.Contains(NormalizeRelative(relative))) continue;
            string destination = Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite);
        }
    }

    private static void NormalizeProjectPaths(GameProject project, string root)
    {
        project.DefaultScene = NormalizeRelative(project.DefaultScene);
        project.EditorScene = NormalizeRelative(project.EditorScene);
        project.Scenes = project.Scenes.Select(NormalizeRelative).ToList();
    }

    private static void NormalizeScenePaths(GameProjectScene scene, string root)
    {
        scene.LoadingScripts = scene.LoadingScripts.Select(binding => NormalizeBinding(binding, root)).ToList();
        scene.Cameras.ForEach(camera => camera.Camera.Vmd.Path = NormalizeOptional(camera.Camera.Vmd.Path, root));
        scene.Lighting.Vmd.Path = NormalizeOptional(scene.Lighting.Vmd.Path, root);
        foreach (GameEntity entity in scene.Entities)
        {
            entity.AssetPath = NormalizeOptional(entity.AssetPath, root);
            entity.Plane.TexturePath = NormalizeOptional(entity.Plane.TexturePath, root);
            entity.Particle.TexturePath = NormalizeOptional(entity.Particle.TexturePath, root);
            foreach (MotionLayerSettings layer in entity.MotionLayers) layer.Path = NormalizeOptional(layer.Path, root);
            entity.Scripts = entity.Scripts.Select(binding => NormalizeBinding(binding, root)).ToList();
        }
        foreach (AudioAsset audio in scene.Audio) audio.Path = NormalizeOptional(audio.Path, root);
        foreach (MotionAsset motion in scene.Motions) motion.Path = NormalizeOptional(motion.Path, root);
        foreach (SpriteSettings sprite in scene.Sprites) sprite.Path = NormalizeOptional(sprite.Path, root);
        scene.Skybox.TexturePath = NormalizeOptional(scene.Skybox.TexturePath, root);
        scene.LoadingScreen.BackgroundImagePath = NormalizeOptional(scene.LoadingScreen.BackgroundImagePath, root);
    }

    private static ScriptBinding NormalizeBinding(ScriptBinding binding, string root)
    {
        binding.Path = NormalizeOptional(binding.Path, root);
        return binding;
    }

    private static string NormalizeOptional(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.StartsWith("app:", StringComparison.OrdinalIgnoreCase)) return path.Replace('\\', '/');
        return NormalizeRelative(path.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            ? path["project:".Length..]
            : Path.IsPathRooted(path) ? GameProjectPath.ToProjectRelative(root, path) : path);
    }

    private static string NormalizeRelative(string path)
    {
        string normalized = (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> result = [];
        foreach (string part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (result.Count == 0) throw new InvalidDataException($"Path escapes Web package root: {path}");
                result.RemoveAt(result.Count - 1);
                continue;
            }
            result.Add(part);
        }
        return string.Join('/', result);
    }

    private static void WriteJson<T>(string root, string relative, T value)
    {
        string path = Path.Combine(root, NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    }

    private static GameProject Clone(GameProject project) => JsonSerializer.Deserialize<GameProject>(JsonSerializer.Serialize(project, JsonOptions), JsonOptions)
        ?? throw new InvalidDataException("Could not clone game project for Web publishing.");

    private static string GetKind(string relative) => Path.GetExtension(relative).ToLowerInvariant() switch
    {
        ".json" => "json", ".pmx" => "pmx", ".vmd" => "vmd", ".dll" => "assembly",
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".dds" => "texture", _ => "binary"
    };

    private static bool IsIgnored(string relative) => relative.Split('/').Any(part =>
        part.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || part.Equals(".git", StringComparison.OrdinalIgnoreCase));

    private static bool IsCSharp(string path, string language) =>
        language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
        || language.Equals("cs", StringComparison.OrdinalIgnoreCase)
        || language.Equals("csx", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);

    private sealed record WebPackageAsset(string Path, string Kind, long Size, string Sha256);
}
