using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Android.Util;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidCSharpScriptHost : IDisposable
{
    private readonly string _projectDirectory;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<RuntimeScene, string, bool> _playAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Func<string, bool> _refreshRenderTexture;
    private readonly Func<string, string, float, bool> _configureRenderTexture;
    private readonly Func<string, AndroidRenderTextureInfo?> _getRenderTexture;
    private readonly Func<IReadOnlyList<AndroidRenderTextureInfo>> _listRenderTextures;
    private readonly Dictionary<string, ScriptRunner<object?>> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _started = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AndroidCSharpScriptHost(
        string projectDirectory,
        Action<string> requestSceneChange,
        Func<RuntimeScene, string, bool> playAudio,
        Func<string, bool> stopAudio,
        Func<string, bool>? refreshRenderTexture = null,
        Func<string, string, float, bool>? configureRenderTexture = null,
        Func<string, AndroidRenderTextureInfo?>? getRenderTexture = null,
        Func<IReadOnlyList<AndroidRenderTextureInfo>>? listRenderTextures = null)
    {
        _projectDirectory = projectDirectory;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _stopAudio = stopAudio;
        _refreshRenderTexture = refreshRenderTexture ?? (_ => false);
        _configureRenderTexture = configureRenderTexture ?? ((_, _, _) => false);
        _getRenderTexture = getRenderTexture ?? (_ => null);
        _listRenderTextures = listRenderTextures ?? (() => []);
    }

    public void Start(RuntimeScene scene)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, 0.0f, true));
            }
        }
    }

    public void Update(RuntimeScene scene, float deltaSeconds)
    {
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, deltaSeconds, false));
            }
        }
    }

    public void DispatchEvent(RuntimeScene scene, AndroidRuntimeEvent runtimeEvent)
    {
        if (_disposed)
        {
            return;
        }

        RuntimeEntity? target = scene.GetEntity(runtimeEvent.TargetEntity);
        IEnumerable<RuntimeEntity> entities = target is null ? scene.Entities : [target];
        foreach (RuntimeEntity entity in entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, CreateGlobals(scene, entity, 0.0f, false, runtimeEvent));
            }
        }
    }

    private AndroidScriptGlobals CreateGlobals(
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent = null)
    {
        AndroidScriptServices services = new(
            scene,
            _requestSceneChange,
            name => _playAudio(scene, name),
            _stopAudio,
            _refreshRenderTexture,
            _configureRenderTexture,
            _getRenderTexture,
            _listRenderTextures);
        return new AndroidScriptGlobals(scene, entity, deltaSeconds, isStart, runtimeEvent, services);
    }

    private void Execute(ScriptBinding binding, AndroidScriptGlobals globals)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
        if (!File.Exists(path)) return;
        try
        {
            if (!_runners.TryGetValue(path, out ScriptRunner<object?>? runner))
            {
                ScriptOptions options = ScriptOptions.Default
                    .WithReferences(
                    [
                        CreateMetadataReference(typeof(object).Assembly),
                        CreateMetadataReference(typeof(Console).Assembly),
                        CreateMetadataReference(typeof(Vector2).Assembly),
                        CreateMetadataReference(typeof(Enumerable).Assembly),
                        CreateMetadataReference(typeof(AndroidScriptGlobals).Assembly),
                        CreateMetadataReference(typeof(RuntimeScene).Assembly),
                        CreateMetadataReference(typeof(GameProject).Assembly)
                    ])
                    .WithMetadataResolver(AndroidMetadataReferenceResolver.Instance)
                    .WithImports("System", "System.Numerics", "Zhengyan.DigitalWife.GameProjects", "Zhengyan.DigitalWife.GamePlayer.Runtime");
                runner = CSharpScript.Create<object?>(File.ReadAllText(path), options, typeof(AndroidScriptGlobals)).CreateDelegate();
                _runners[path] = runner;
            }

            runner(globals).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android C# script failed '{path}': {ex.GetBaseException().Message}");
        }
    }

    private static MetadataReference CreateMetadataReference(Assembly assembly)
    {
        // Android assemblies are loaded from the APK and Assembly.Location is
        // commonly a synthetic path such as '/Zhengyan...'. Roslyn's Assembly
        // overload then tries to open that path. Use the in-memory metadata
        // image instead.
        unsafe
        {
            if (assembly.TryGetRawMetadata(out byte* blob, out int length)
                && blob is not null && length > 0)
            {
                // TryGetRawMetadata returns the metadata directory inside the
                // loaded PE image, not a complete PE image. CreateFromImage
                // therefore makes Roslyn reject the reference and fall back
                // to Assembly.Location (which is a synthetic Android path).
                // Build a reference directly from the in-memory metadata.
                ModuleMetadata module = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                AssemblyMetadata assemblyMetadata = AssemblyMetadata.Create(module);
                // Do not assign a file path. Android assemblies commonly have
                // synthetic locations, and Roslyn otherwise attempts to probe
                // that path while binding System.Private.CoreLib.
                return assemblyMetadata.GetReference();
            }
        }

        throw new InvalidOperationException($"Assembly metadata is unavailable: {assembly.FullName}");
    }

    private sealed class AndroidMetadataReferenceResolver : MetadataReferenceResolver
    {
        public static AndroidMetadataReferenceResolver Instance { get; } = new();

        // All references are supplied from loaded assemblies below. Do not let
        // Roslyn fall back to probing Android's synthetic assembly paths.
        public override bool ResolveMissingAssemblies => false;

        public override System.Collections.Immutable.ImmutableArray<PortableExecutableReference> ResolveReference(
            string reference,
            string? baseFilePath,
            MetadataReferenceProperties properties)
        {
            string name = Path.GetFileNameWithoutExtension(reference);
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            return assembly is null
                ? System.Collections.Immutable.ImmutableArray<PortableExecutableReference>.Empty
                : System.Collections.Immutable.ImmutableArray.Create(
                    (PortableExecutableReference)CreateMetadataReference(assembly));
        }

        public override PortableExecutableReference? ResolveMissingAssembly(
            MetadataReference definition,
            AssemblyIdentity referenceIdentity)
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    referenceIdentity.Name,
                    StringComparison.OrdinalIgnoreCase));
            return assembly is null ? null : (PortableExecutableReference)CreateMetadataReference(assembly);
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => typeof(AndroidMetadataReferenceResolver).GetHashCode();
    }

    private static bool IsSupported(ScriptBinding binding)
    {
        return binding.Enabled
            && (string.Equals(binding.Language, "csharp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(binding.Language, "csx", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runners.Clear();
        _started.Clear();
    }
}

public sealed record AndroidRuntimeEvent(
    string Type,
    string Id,
    string EventName,
    Vector2 Position,
    string Text = "",
    string TargetEntity = "");

public sealed record AndroidRenderTextureInfo(
    string Id,
    string Name,
    int Width,
    int Height,
    string RefreshMode,
    float RefreshIntervalSeconds,
    bool HasRendered,
    double LastRenderedSeconds);

public sealed record AndroidQualityBudgetInfo(
    string Profile,
    int TargetFrameRate,
    int TextureMemoryBudgetMb,
    int RenderTargetMemoryBudgetMb,
    int DrawCallBudget,
    long EstimatedGpuBytes,
    double LastFrameGpuEstimateMs,
    int AdaptiveParticleLimit,
    bool ReflectionsEnabled);

public sealed class AndroidScriptGlobals
{
    public AndroidScriptGlobals(
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent = null,
        AndroidScriptServices? services = null)
    {
        Scene = scene;
        Entity = entity;
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
        Event = runtimeEvent;
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public RuntimeScene Scene { get; }
    public RuntimeEntity Entity { get; }
    public float DeltaSeconds { get; }
    public bool IsStart { get; }

    /// <summary>非 null 时表示一次 GUI/Sprite/触摸事件；Start/Update 时为 null。</summary>
    public AndroidRuntimeEvent? Event { get; }

    public bool IsEvent => Event is not null;

    public AndroidScriptServices Services { get; }
}

public sealed class AndroidScriptServices
{
    private readonly RuntimeScene _scene;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<string, bool> _playAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Func<string, bool> _refreshRenderTexture;
    private readonly Func<string, string, float, bool> _configureRenderTexture;
    private readonly Func<string, AndroidRenderTextureInfo?> _getRenderTexture;
    private readonly Func<IReadOnlyList<AndroidRenderTextureInfo>> _listRenderTextures;

    internal AndroidScriptServices(
        RuntimeScene scene,
        Action<string> requestSceneChange,
        Func<string, bool> playAudio,
        Func<string, bool> stopAudio,
        Func<string, bool> refreshRenderTexture,
        Func<string, string, float, bool> configureRenderTexture,
        Func<string, AndroidRenderTextureInfo?> getRenderTexture,
        Func<IReadOnlyList<AndroidRenderTextureInfo>> listRenderTextures)
    {
        _scene = scene;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _stopAudio = stopAudio;
        _refreshRenderTexture = refreshRenderTexture;
        _configureRenderTexture = configureRenderTexture;
        _getRenderTexture = getRenderTexture;
        _listRenderTextures = listRenderTextures;
    }

    public RuntimeEntity? FindEntity(string idOrName) => _scene.GetEntity(idOrName);
    public RuntimeEntity AddPointLight(string name, Vector3 position, Vector3 color, float intensity = 1, float range = 8)
        => _scene.AddPointLight(name, position, color, intensity, range);
    public RuntimeEntity AddSpotLight(string name, Vector3 position, Vector3 rotation, Vector3 color, float intensity = 1, float range = 12)
        => _scene.AddSpotLight(name, position, rotation, color, intensity, range);
    public bool RemoveEntity(string idOrName) => _scene.RemoveEntity(idOrName);
    public void ChangeScene(string path) => _requestSceneChange(path);
    public bool PlayAudio(string idOrName) => _playAudio(idOrName);
    public bool StopAudio(string idOrName) => _stopAudio(idOrName);

    /// <summary>使指定 RenderTexture 在下一帧强制刷新。</summary>
    public bool RefreshRenderTexture(string idOrName) => _refreshRenderTexture(idOrName);

    /// <summary>修改 RenderTexture 的刷新模式：every_frame、interval 或 manual。</summary>
    public bool ConfigureRenderTexture(string idOrName, string refreshMode, float intervalSeconds = 0.1f)
        => _configureRenderTexture(idOrName, refreshMode, intervalSeconds);

    /// <summary>查询一个 RenderTexture 的尺寸、刷新模式和最近绘制时间。</summary>
    public AndroidRenderTextureInfo? GetRenderTexture(string idOrName) => _getRenderTexture(idOrName);

    /// <summary>列出当前场景全部可用 RenderTexture。</summary>
    public IReadOnlyList<AndroidRenderTextureInfo> GetRenderTextures() => _listRenderTextures();
}
