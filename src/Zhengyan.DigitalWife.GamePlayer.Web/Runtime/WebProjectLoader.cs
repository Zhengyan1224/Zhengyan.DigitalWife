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
    WebScriptRuntime Scripts)
{
    public IReadOnlyDictionary<string, string> TextureUris { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string,WebSurfaceScriptApi> SurfaceScriptApis {get;init;}=new Dictionary<string,WebSurfaceScriptApi>();
    public WebSceneSimulation Simulation { get; } = new();
    public Dictionary<string, string> SurfaceSignatures { get; } = new();
    public IReadOnlyDictionary<string, VmdParsing> Animations { get; init; } = new Dictionary<string, VmdParsing>();
    public Dictionary<string, DateTimeOffset> ModelRetryAfter { get; } = new();
}

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
    public Action<string>? ScriptWarningHandler { get; set; }
    public Func<PmxParsing, Vector3, Task<IPmxPhysicsBridge?>>? PhysicsFactory { get; set; }
    public Func<RuntimeScene, GameProject, Task>? InitializeServices { get; set; }
    public Func<RuntimeScene,GameProject,Task>? InitializeModels {get;set;}
    public Action<WebScriptRuntime>? ScriptRuntimeCreated {get;set;}
    public Func<GameProject, GameProjectScene, Task>? ConfigureLoadingScreen { get; set; }
    public Func<WebScriptEntry,string,IWebGameScript>? PythonFactory { get; set; }
    private Zhengyan.DigitalWife.Scripting.RuntimeGraphicsQuality? _graphics;

    public IWebAssetStore Assets => _assets;

    public async Task<WebLoadedProject> LoadAsync(
        IProgress<WebLoadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? scenePathOverride = null)
    {
        WebScriptRuntime? loadingRuntime=null;
        var externalProgress=progress;
        progress=new ScriptLoadProgress(value=>{externalProgress?.Report(value);loadingRuntime?.PublishLoading("progress",value.Value,value.Message);});
        progress?.Report(new("manifest", 0.02f, "web.manifest.json", "Reading Web manifest"));
        WebAssetManifest manifest = await _assets.LoadManifestAsync(cancellationToken);
        progress?.Report(new("project", 0.12f, "game.project.json", "Reading game project"));
        GameProject project = await _assets.ReadJsonAsync<GameProject>("game.project.json", cancellationToken);
        GameProjectStore.NormalizeScenes(project);
        string scenePath = scenePathOverride ?? (string.IsNullOrWhiteSpace(project.DefaultScene)
            ? manifest.EntryScene
            : project.DefaultScene);
        scenePath = WebHttpAssetStore.NormalizeRelativePath(scenePath);
        progress?.Report(new("scene", 0.2f, scenePath, "Reading scene"));
        GameProjectScene sceneDefinition = await _assets.ReadJsonAsync<GameProjectScene>(scenePath, cancellationToken);
        if (ConfigureLoadingScreen is not null) await ConfigureLoadingScreen(project, sceneDefinition);
        project.Scene = sceneDefinition;

        Dictionary<string, VmdParsing> animations = [];
        IEnumerable<string> animationPaths = sceneDefinition.Cameras
            .Select(camera => camera.Camera.Vmd.Path)
            .Append(sceneDefinition.Lighting.Vmd.Path)
            .Concat(sceneDefinition.Entities.SelectMany(entity => entity.MotionLayers.Select(layer => layer.Path)))
            .Concat(sceneDefinition.Motions.Select(motion => motion.Path))
            .Concat(manifest.Assets.Where(a => a.Path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase)).Select(a => a.Path))
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
        WebMeshColliders meshColliders = new();
        RuntimeScene? runtimeScenePlaceholder = null;
        RuntimeScene runtimeScene = new(
            scenePath,
            sceneDefinition,
            static path => WebHttpAssetStore.NormalizeRelativePath(path),
            resetPmxPhysics: id => { if (!pmxAssets.TryGetValue(id, out var asset)) return false; asset.ResetPhysics(); return true; },
            meshColliderResolver: (id, settings) => runtimeScenePlaceholder?.GetEntity(id) is RuntimeEntity entity
                && (pmxAssets.GetValueOrDefault(id)?.Mesh ?? sceneMeshes.GetValueOrDefault(id)) is WebPmxMesh mesh
                ? meshColliders.Resolve(entity, settings, mesh) : null,
            nodeWorldResolver: (id, bone) => pmxAssets.TryGetValue(id, out var asset) && runtimeScenePlaceholder is not null
                ? asset.GetBoneWorld(bone, runtimeScenePlaceholder.GetEntity(id)!.TransformMatrix) : null,
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
                    case "layers":
                        asset.SetMotionLayers();
                        break;
                }
            },
            speechRequest: (entityId, request) => SpeechRequestHandler?.Invoke(entityId, request),
            graphics: _graphics ??= project.CreateRuntimeGraphicsQuality());
        runtimeScenePlaceholder = runtimeScene;
        if(InitializeServices is not null)await InitializeServices(runtimeScene,project);
        WebScriptRuntime scripts=new(){PythonFactory=PythonFactory};
        await scripts.LoadAsync(_assets,manifest,runtimeScene,cancellationToken);
        loadingRuntime=scripts;ScriptRuntimeCreated?.Invoke(scripts);
        await scripts.StartLoadingAsync();
        scripts.PublishLoading("started",.4f,"Loading scene assets");

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
            if (sprite.Path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase)) { spriteUris[sprite.Id] = sprite.Path; continue; }
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
                string texturePath = entity.Plane.TexturePath;
                string? textureUri = texturePath.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? texturePath : !string.IsNullOrWhiteSpace(texturePath) && IsRegistered(texturePath)
                    ? await _assets.GetUriAsync(texturePath, cancellationToken)
                    : null;
                sceneMeshes[entity.Id] = CreateQuadMesh(
                    Math.Max(entity.Plane.Width, 0.01f),
                    Math.Max(entity.Plane.Height, 0.01f),
                    entity.Plane.Tint,
                    Math.Clamp(entity.Plane.Opacity, 0.0f, 1.0f),
                    textureUri,
                    entity.Plane.ReceiveShadow,
                    doubleSided: true);
            }
            else if (IsEntityType(entity.Type, "water_surface", "water"))
            {
                sceneMeshes[entity.Id] = WebSceneSimulation.CreateWaterMesh(entity.Water);
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
            IPmxPhysicsBridge? physics = PhysicsFactory is null ? null : await PhysicsFactory(parsing, entity.Definition.PhysicsGravity);
            WebPmxAsset asset = WebPmxAsset.Create(
                entity.Definition,
                parsing,
                layers,
                mesh,
                path => animations.TryGetValue(
                    WebHttpAssetStore.NormalizeRelativePath(path),
                    out VmdParsing? animation) ? animation : null,
                preferGpuSkinning: true,
                physicsBridge: physics);
            if (parsing.Bones.Length > 0)
                foreach (MotionAsset motion in sceneDefinition.Motions) asset.PrepareMotion(motion.Path);
            pmxAssets[entity.Id] = asset;
            entity.PmxScriptApi = asset;
            entity.ScriptAssetPathResolver = WebHttpAssetStore.NormalizeRelativePath;
            asset.ScriptWarning = ScriptWarningHandler;
            entity.MorphWeightSetter = asset.SetMorphWeight;
            entity.MorphWeightGetter = name => asset.Pose.GetMorphWeight(name);
        }

        progress?.Report(new("scripts", 0.78f, "compiled/web", "Loading precompiled Web scripts"));
        WebPmxRelations.Apply(runtimeScene, pmxAssets);
        Dictionary<string, string> textures = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in manifest.Assets.Where(a => a.Kind == "texture" || new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".dds", ".sph", ".spa" }.Contains(Path.GetExtension(a.Path).ToLowerInvariant())).Select(a => a.Path)
            .Concat(sceneDefinition.Entities.Select(e => e.Particle.TexturePath))
            .Append(sceneDefinition.Skybox.TexturePath).Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>())
        {
            string normalized = path.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ? path[8..] : path;
            if (!normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase) && IsRegistered(normalized))
                textures[path] = await _assets.GetUriAsync(normalized, cancellationToken);
        }
        foreach(var asset in pmxAssets.Values)asset.ScriptTextureResolver=path=>ResolveScriptTexture(path,textures);
        var shaderSources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in manifest.Assets.Where(a=>a.Size<=1024*1024&&new[]{".glsl",".vert",".frag",".vsh",".fsh"}.Contains(Path.GetExtension(a.Path).ToLowerInvariant())))
            shaderSources[entry.Path]=System.Text.Encoding.UTF8.GetString(await _assets.ReadAllBytesAsync(entry.Path,cancellationToken));
        foreach(var asset in pmxAssets.Values)asset.ScriptShaderResolver=path=>shaderSources.GetValueOrDefault(WebHttpAssetStore.NormalizeRelativePath(path));
        var surfaceApis=new Dictionary<string,WebSurfaceScriptApi>();
        foreach(var entity in runtimeScene.TexturedPlanes)
        {
            var api=new WebSurfaceScriptApi(sceneMeshes[entity.Id],path=>shaderSources.GetValueOrDefault(WebHttpAssetStore.NormalizeRelativePath(path)));
            entity.ScriptAssetPathResolver=WebHttpAssetStore.NormalizeRelativePath;
            entity.PmxScriptApi=api;surfaceApis[entity.Id]=api;
        }
        if(InitializeModels is not null)await InitializeModels(runtimeScene,project);
        await scripts.FlushLoadingEventsAsync();
        await scripts.StartAsync();
        progress?.Report(new("renderer", 0.90f, scenePath, "正在准备场景画面"));
        return new WebLoadedProject(project, sceneDefinition, runtimeScene, manifest, pmxAssets, sceneMeshes, audioAssets, spriteUris, scripts) { TextureUris = textures, Animations = animations,SurfaceScriptApis=surfaceApis };
    }

    public async Task<WebPmxAsset> LoadRuntimeModelAsync(RuntimeEntity entity, WebLoadedProject project)
    {
        string path = WebHttpAssetStore.NormalizeRelativePath(entity.Definition.AssetPath);
        var parsing = await LoadPmxParsingAsync(path);
        var mesh = await LoadPmxAsync(path);
        var layers = entity.Definition.MotionLayers.Where(l => project.Animations.ContainsKey(l.Path))
            .Select(l => (project.Animations[l.Path], l.Weight)).ToArray();
        var physics = PhysicsFactory is null ? null : await PhysicsFactory(parsing, entity.Definition.PhysicsGravity);
        var asset = WebPmxAsset.Create(entity.Definition, parsing, layers, mesh, p => project.Animations.GetValueOrDefault(p), true, physics);
        entity.PmxScriptApi = asset; entity.ScriptAssetPathResolver = WebHttpAssetStore.NormalizeRelativePath;
        asset.ScriptTextureResolver = path=>ResolveScriptTexture(path,project.TextureUris); asset.ScriptWarning = ScriptWarningHandler;
        entity.MorphWeightSetter = asset.SetMorphWeight; entity.MorphWeightGetter = asset.Pose.GetMorphWeight;
        return asset;
    }

    private sealed class ScriptLoadProgress(Action<WebLoadProgress> report):IProgress<WebLoadProgress>
    {public void Report(WebLoadProgress value)=>report(value);}

    private static string? ResolveScriptTexture(string path,IReadOnlyDictionary<string,string> textures)
    {
        if(path.StartsWith("rt:",StringComparison.OrdinalIgnoreCase))return path;
        if(path.StartsWith("app:",StringComparison.OrdinalIgnoreCase))return "./engine/"+path[4..].Replace("Resources/","").Replace('\\','/');
        string normalized=WebHttpAssetStore.NormalizeRelativePath(path);
        return textures.GetValueOrDefault(path)??textures.GetValueOrDefault(normalized);
    }

    internal static WebPmxMesh CreateQuadMesh(
        float width,
        float height,
        Vector4Dto tint,
        float opacity,
        string? textureUri,
        bool receiveShadow,
        bool doubleSided = false,
        bool horizontal = false)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        return new WebPmxMesh(
            horizontal ? [
                -halfWidth, 0.0f, -halfHeight,
                 halfWidth, 0.0f, -halfHeight,
                 halfWidth, 0.0f,  halfHeight,
                -halfWidth, 0.0f,  halfHeight
            ] : [
                -halfWidth, -halfHeight, 0.0f,
                 halfWidth, -halfHeight, 0.0f,
                 halfWidth,  halfHeight, 0.0f,
                -halfWidth,  halfHeight, 0.0f
            ],
            horizontal ? [
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f,
                0.0f, 1.0f, 0.0f
            ] : [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f],
            [0u, 1u, 2u, 2u, 3u, 0u],
            horizontal ? new Vector3(-halfWidth, 0.0f, -halfHeight) : new Vector3(-halfWidth, -halfHeight, 0),
            horizontal ? new Vector3(halfWidth, 0.0f, halfHeight) : new Vector3(halfWidth, halfHeight, 0),
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



