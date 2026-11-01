using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebLoadProgress(string Stage, float Value, string Path, string Message);

public sealed record WebLoadedProject(
    GameProject Project,
    GameProjectScene SceneDefinition,
    RuntimeScene RuntimeScene,
    WebAssetManifest Manifest,
    IReadOnlyDictionary<string, WebPmxAsset> PmxAssets,
    WebScriptRuntime Scripts);

public sealed class WebProjectLoader(IWebAssetStore assets)
{
    private readonly IWebAssetStore _assets = assets;

    public IWebAssetStore Assets => _assets;

    public async Task<WebLoadedProject> LoadAsync(
        IProgress<WebLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new("manifest", 0.02f, "web.manifest.json", "Reading Web manifest"));
        WebAssetManifest manifest = await _assets.LoadManifestAsync(cancellationToken);
        progress?.Report(new("project", 0.12f, "game.project.json", "Reading game project"));
        GameProject project = await _assets.ReadJsonAsync<GameProject>("game.project.json", cancellationToken);
        GameProjectStore.NormalizeScenes(project);
        string scenePath = string.IsNullOrWhiteSpace(project.DefaultScene)
            ? manifest.EntryScene
            : project.DefaultScene;
        scenePath = WebHttpAssetStore.NormalizeRelativePath(scenePath);
        progress?.Report(new("scene", 0.2f, scenePath, "Reading scene"));
        GameProjectScene sceneDefinition = await _assets.ReadJsonAsync<GameProjectScene>(scenePath, cancellationToken);
        project.Scene = sceneDefinition;

        Dictionary<string, VmdParsing> animations = [];
        IEnumerable<string> animationPaths = sceneDefinition.Cameras
            .Select(camera => camera.Camera.Vmd.Path)
            .Append(sceneDefinition.Lighting.Vmd.Path)
            .Concat(sceneDefinition.Entities.SelectMany(entity => entity.MotionLayers.Select(layer => layer.Path)))
            .Where(path => !string.IsNullOrWhiteSpace(path));
        string[] distinctAnimationPaths = animationPaths
            .Select(WebHttpAssetStore.NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (int i = 0; i < distinctAnimationPaths.Length; i++)
        {
            string animationPath = distinctAnimationPaths[i];
            progress?.Report(new(
                "animation",
                0.25f + 0.15f * (i + 1) / Math.Max(distinctAnimationPaths.Length, 1),
                animationPath,
                "Reading VMD animation"));
            animations[animationPath] = await LoadVmdAsync(animationPath, cancellationToken);
        }

        RuntimeScene runtimeScene = new(
            scenePath,
            sceneDefinition,
            static path => WebHttpAssetStore.NormalizeRelativePath(path),
            animationLoader: path => animations.TryGetValue(
                WebHttpAssetStore.NormalizeRelativePath(path), out VmdParsing? animation)
                ? animation
                : null);

        Dictionary<string, WebPmxAsset> pmxAssets = new(StringComparer.OrdinalIgnoreCase);
        RuntimeEntity[] modelEntities = runtimeScene.PmxModels.ToArray();
        for (int i = 0; i < modelEntities.Length; i++)
        {
            RuntimeEntity entity = modelEntities[i];
            if (string.IsNullOrWhiteSpace(entity.Definition.AssetPath)) continue;
            string assetPath = WebHttpAssetStore.NormalizeRelativePath(entity.Definition.AssetPath);
            progress?.Report(new(
                "model",
                0.42f + 0.28f * (i + 1) / Math.Max(modelEntities.Length, 1),
                assetPath,
                "Reading PMX model"));
            PmxParsing parsing = await LoadPmxParsingAsync(assetPath, cancellationToken);
            List<(VmdParsing Animation, float Weight)> layers = [];
            foreach (MotionLayerSettings layer in entity.Definition.MotionLayers)
            {
                if (string.IsNullOrWhiteSpace(layer.Path)) continue;
                string motionPath = WebHttpAssetStore.NormalizeRelativePath(layer.Path);
                if (!animations.TryGetValue(motionPath, out VmdParsing? animation))
                {
                    animation = await LoadVmdAsync(motionPath, cancellationToken);
                    animations[motionPath] = animation;
                }
                layers.Add((animation, layer.Weight));
            }

            WebPmxMesh mesh = WebPmxMesh.FromParsing(
                parsing,
                assetPath,
                texturePath => IsRegistered(texturePath)
                    ? _assets.GetUriAsync(texturePath, cancellationToken).AsTask().GetAwaiter().GetResult()
                    : null);
            WebPmxAsset asset = WebPmxAsset.Create(entity.Definition, parsing, layers, mesh);
            pmxAssets[entity.Id] = asset;
        }

        progress?.Report(new("scripts", 0.78f, "compiled/web", "Loading precompiled Web scripts"));
        WebScriptRuntime scripts = new();
        await scripts.LoadAsync(_assets, manifest, runtimeScene, cancellationToken);
        scripts.Start();
        progress?.Report(new("ready", 1.0f, scenePath, "Web scene ready"));
        return new WebLoadedProject(project, sceneDefinition, runtimeScene, manifest, pmxAssets, scripts);
    }

    public async Task<WebPmxMesh> LoadPmxAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        PmxParsing parsing = await LoadPmxParsingAsync(relativePath, cancellationToken);
        return WebPmxMesh.FromParsing(
            parsing,
            WebHttpAssetStore.NormalizeRelativePath(relativePath),
            texturePath => IsRegistered(texturePath)
                ? _assets.GetUriAsync(texturePath, cancellationToken).AsTask().GetAwaiter().GetResult()
                : null);
    }

    public async Task<PmxParsing> LoadPmxParsingAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await _assets.ReadAllBytesAsync(relativePath, cancellationToken);
        return PmxParsing.ParsingByBytes(bytes)
            ?? throw new InvalidDataException($"Invalid PMX asset: {relativePath}");
    }

    public async Task<VmdParsing> LoadVmdAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await _assets.ReadAllBytesAsync(relativePath, cancellationToken);
        return VmdParsing.ParsingByBytes(bytes)
            ?? throw new InvalidDataException($"Invalid VMD asset: {relativePath}");
    }

    private bool IsRegistered(string path)
    {
        string normalized = WebHttpAssetStore.NormalizeRelativePath(path);
        return _assets.Manifest.Assets.Any(asset =>
            string.Equals(WebHttpAssetStore.NormalizeRelativePath(asset.Path), normalized, StringComparison.OrdinalIgnoreCase));
    }
}



