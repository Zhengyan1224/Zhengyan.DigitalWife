using Zhengyan.DigitalWife.GamePlayer.Runtime;
using System.Numerics;
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
    IReadOnlyDictionary<string, WebPmxMesh> SceneMeshes,
    IReadOnlyDictionary<string, WebAudioAsset> AudioAssets,
    IReadOnlyDictionary<string, string> SpriteUris,
    WebScriptRuntime Scripts);

public sealed record WebAudioAsset(
    string Alias,
    string Path,
    string Uri,
    bool Loop,
    float Volume);

public sealed class WebProjectLoader(IWebAssetStore assets)
{
    private readonly IWebAssetStore _assets = assets;

    public Action<string, RuntimeSpeechRequest>? SpeechRequestHandler { get; set; }

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
            .Concat(sceneDefinition.Motions.Select(motion => motion.Path))
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

        Dictionary<string, WebPmxAsset> pmxAssets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WebPmxMesh> sceneMeshes = new(StringComparer.OrdinalIgnoreCase);
        RuntimeScene runtimeScene = new(
            scenePath,
            sceneDefinition,
            static path => WebHttpAssetStore.NormalizeRelativePath(path),
            animationLoader: path => animations.TryGetValue(
                WebHttpAssetStore.NormalizeRelativePath(path), out VmdParsing? animation)
                ? animation
                : null,
            motionCommand: (entityId, command) =>
            {
                if (!pmxAssets.TryGetValue(entityId, out WebPmxAsset? asset)) return;
                switch (command.Operation)
                {
                    case "apply":
                        asset.ApplyMotion(command.Path);
                        break;
                    case "play":
                        asset.PlayMotion(command.Restart);
                        break;
                    case "pause":
                        asset.PauseMotion();
                        break;
                    case "stop":
                        asset.StopMotion();
                        break;
                }
            },
            speechRequest: (entityId, request) => SpeechRequestHandler?.Invoke(entityId, request));

        Dictionary<string, WebAudioAsset> audioAssets = new(StringComparer.OrdinalIgnoreCase);
        foreach (AudioAsset audio in sceneDefinition.Audio.Where(item => !string.IsNullOrWhiteSpace(item.Path)))
        {
            string path = WebHttpAssetStore.NormalizeRelativePath(audio.Path);
            if (!IsRegistered(path)) continue;
            string uri = await _assets.GetUriAsync(path, cancellationToken);
            WebAudioAsset resolved = new(
                string.IsNullOrWhiteSpace(audio.Name) ? path : audio.Name,
                path,
                uri,
                audio.Loop,
                Math.Clamp(audio.Volume, 0.0f, 1.0f));
            audioAssets[resolved.Alias] = resolved;
            audioAssets[path] = resolved;
        }

        Dictionary<string, string> spriteUris = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteSettings sprite in sceneDefinition.Sprites.Where(item => !string.IsNullOrWhiteSpace(item.Path)))
        {
            string path = WebHttpAssetStore.NormalizeRelativePath(sprite.Path);
            if (IsRegistered(path))
            {
                spriteUris[sprite.Id] = await _assets.GetUriAsync(path, cancellationToken);
            }
        }

        foreach (GameEntity entity in sceneDefinition.Entities.Where(item => IsEntityType(item.Type, "textured_plane", "plane", "water_surface", "water")))
        {
            if (IsEntityType(entity.Type, "textured_plane", "plane"))
            {
                string texturePath = WebHttpAssetStore.NormalizeRelativePath(entity.Plane.TexturePath);
                string? textureUri = IsRegistered(texturePath)
                    ? await _assets.GetUriAsync(texturePath, cancellationToken)
                    : null;
                sceneMeshes[entity.Id] = CreateQuadMesh(
                    Math.Max(entity.Plane.Width, 0.01f),
                    Math.Max(entity.Plane.Height, 0.01f),
                    entity.Plane.Tint,
                    Math.Clamp(entity.Plane.Opacity, 0.0f, 1.0f),
                    textureUri,
                    entity.Plane.ReceiveShadow);
            }
            else if (IsEntityType(entity.Type, "water_surface", "water"))
            {
                sceneMeshes[entity.Id] = CreateQuadMesh(
                    Math.Max(entity.Water.Size, 0.1f),
                    Math.Max(entity.Water.Size, 0.1f),
                    new Vector4Dto(entity.Water.DeepColor.X, entity.Water.DeepColor.Y, entity.Water.DeepColor.Z, entity.Water.Alpha),
                    Math.Clamp(entity.Water.Alpha, 0.0f, 1.0f),
                    null,
                    receiveShadow: false,
                    doubleSided: true);
            }
        }

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
            WebPmxAsset asset = WebPmxAsset.Create(
                entity.Definition,
                parsing,
                layers,
                mesh,
                path => animations.TryGetValue(
                    WebHttpAssetStore.NormalizeRelativePath(path),
                    out VmdParsing? animation) ? animation : null);
            pmxAssets[entity.Id] = asset;
        }

        progress?.Report(new("scripts", 0.78f, "compiled/web", "Loading precompiled Web scripts"));
        WebScriptRuntime scripts = new();
        await scripts.LoadAsync(_assets, manifest, runtimeScene, cancellationToken);
        scripts.Start();
        progress?.Report(new("ready", 1.0f, scenePath, "Web scene ready"));
        return new WebLoadedProject(project, sceneDefinition, runtimeScene, manifest, pmxAssets, sceneMeshes, audioAssets, spriteUris, scripts);
    }

    private static WebPmxMesh CreateQuadMesh(
        float width,
        float height,
        Vector4Dto tint,
        float opacity,
        string? textureUri,
        bool receiveShadow,
        bool doubleSided = false)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        return new WebPmxMesh(
            [
                -halfWidth, 0.0f, -halfHeight,
                 halfWidth, 0.0f, -halfHeight,
                 halfWidth, 0.0f,  halfHeight,
                -halfWidth, 0.0f,  halfHeight
            ],
            [
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f
            ],
            [0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f],
            [0u, 1u, 2u, 2u, 3u, 0u],
            new Vector3(-halfWidth, 0.0f, -halfHeight),
            new Vector3(halfWidth, 0.0f, halfHeight),
            "Web scene quad",
            [new WebPmxMaterial(
                0,
                6,
                new Vector4(tint.X, tint.Y, tint.Z, tint.W * opacity),
                Vector3.One,
                Vector4.Zero,
                0.0f,
                doubleSided,
                false,
                false,
                receiveShadow,
                textureUri,
                "Scene quad")]);
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

    private static bool IsEntityType(string? value, params string[] accepted)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return accepted.Any(item => string.Equals(normalized, item, StringComparison.OrdinalIgnoreCase));
    }
}



