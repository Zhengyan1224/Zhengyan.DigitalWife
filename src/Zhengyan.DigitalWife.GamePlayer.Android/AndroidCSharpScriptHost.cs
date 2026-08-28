using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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
    private readonly Func<string, bool> _pauseAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Func<string, bool> _refreshRenderTexture;
    private readonly Func<string, string, float, bool> _configureRenderTexture;
    private readonly Func<string, AndroidRenderTextureInfo?> _getRenderTexture;
    private readonly Func<IReadOnlyList<AndroidRenderTextureInfo>> _listRenderTextures;
    private readonly Action<RuntimeScene, RuntimeEntity, string> _applyMotion;
    private readonly Action<RuntimeEntity, float?, bool?> _setMotionState;
    private readonly Dictionary<string, AndroidCompiledScript> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _started = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AndroidCSharpScriptHost(
        string projectDirectory,
        Action<string> requestSceneChange,
        Func<RuntimeScene, string, bool> playAudio,
        Func<string, bool> pauseAudio,
        Func<string, bool> stopAudio,
        Func<string, bool>? refreshRenderTexture = null,
        Func<string, string, float, bool>? configureRenderTexture = null,
        Func<string, AndroidRenderTextureInfo?>? getRenderTexture = null,
        Func<IReadOnlyList<AndroidRenderTextureInfo>>? listRenderTextures = null,
        Action<RuntimeScene, RuntimeEntity, string>? applyMotion = null,
        Action<RuntimeEntity, float?, bool?>? setMotionState = null)
    {
        _projectDirectory = projectDirectory;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _pauseAudio = pauseAudio;
        _stopAudio = stopAudio;
        _refreshRenderTexture = refreshRenderTexture ?? (_ => false);
        _configureRenderTexture = configureRenderTexture ?? ((_, _, _) => false);
        _getRenderTexture = getRenderTexture ?? (_ => null);
        _listRenderTextures = listRenderTextures ?? (() => []);
        _applyMotion = applyMotion ?? ((_, _, _) => { });
        _setMotionState = setMotionState ?? ((_, _, _) => { });
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
            _pauseAudio,
            _stopAudio,
            _refreshRenderTexture,
            _configureRenderTexture,
            _getRenderTexture,
            _listRenderTextures);
        return new AndroidScriptGlobals(
            new AndroidScriptScene(scene, _projectDirectory),
            new AndroidScriptEntity(
                entity,
                path => _applyMotion(scene, entity, path),
                (frame, playing) => _setMotionState(entity, frame, playing)),
            new AndroidScriptInput(),
            new AndroidScriptAudio(name => _playAudio(scene, name), _pauseAudio, _stopAudio),
            deltaSeconds,
            isStart,
            !isStart && runtimeEvent is null,
            runtimeEvent,
            services);
    }

    private void Execute(ScriptBinding binding, AndroidScriptGlobals globals)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
        if (!File.Exists(path)) return;
        try
        {
            if (!_runners.TryGetValue(path, out AndroidCompiledScript? runner))
            {
                runner = Compile(path);
                _runners[path] = runner;
            }

            runner.Execute(globals);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android C# script failed '{path}': {ex}");
        }
    }

    private static AndroidCompiledScript Compile(string path)
    {
        string source = """
            using System;
            using System.Numerics;
            using Zhengyan.DigitalWife.GameProjects;
            using Zhengyan.DigitalWife.GamePlayer.Runtime;
            using Zhengyan.DigitalWife.GamePlayer.Android;

            """ + File.ReadAllText(path);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script),
            path);
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation(
            "AndroidScript_" + Guid.NewGuid().ToString("N"),
            syntaxTree,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release),
            returnType: typeof(object),
            globalsType: typeof(AndroidScriptGlobals));

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        if (!result.Success)
        {
            string diagnostics = string.Join(Environment.NewLine, result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
            throw new InvalidOperationException($"Android C# script compilation failed:{Environment.NewLine}{diagnostics}");
        }

        Assembly assembly = Assembly.Load(image.ToArray());
        Type scriptType = assembly.GetType("Script")
            ?? throw new InvalidOperationException("Android C# script did not produce a Script type.");
        MethodInfo factory = scriptType.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Android C# script did not produce an execution factory.");
        return new AndroidCompiledScript(factory);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        // The Android host may not have loaded framework assemblies that are
        // only used by a script (Console is a common example). Touch the
        // representative types first so their in-memory metadata is available.
        List<Assembly> requiredAssemblies =
        [
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Task).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(Vector3).Assembly,
            typeof(AndroidScriptGlobals).Assembly,
            typeof(RuntimeScene).Assembly,
            typeof(GameProject).Assembly
        ];

        // System.Runtime is a facade on some .NET runtimes and is not
        // necessarily loaded by the game host. Roslyn needs its async-builder
        // contract when emitting the script submission factory.
        try
        {
            requiredAssemblies.Add(Assembly.Load(new AssemblyName("System.Runtime")));
        }
        catch (Exception)
        {
            // The concrete core library reference above is still usable on
            // runtimes that do not expose a separate System.Runtime facade.
        }

        foreach (Assembly assembly in requiredAssemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            if (assembly.IsDynamic || !identities.Add(assembly.FullName ?? assembly.GetName().Name ?? string.Empty))
            {
                continue;
            }

            yield return CreateMetadataReference(assembly);
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

    private sealed class AndroidCompiledScript
    {
        private readonly MethodInfo _factory;

        public AndroidCompiledScript(MethodInfo factory)
        {
            _factory = factory;
        }

        public void Execute(AndroidScriptGlobals globals)
        {
            object?[] submissionArray = [globals, null];
            Task<object?> task = (Task<object?>)_factory.Invoke(null, [submissionArray])!;
            task.GetAwaiter().GetResult();
        }
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

public sealed class AndroidScriptScene
{
    private readonly RuntimeScene _scene;

    internal AndroidScriptScene(RuntimeScene scene, string projectDirectory)
    {
        _scene = scene;
        Camera = new AndroidScriptCamera(scene, projectDirectory);
    }

    public string Name => _scene.Name;
    public AndroidScriptCamera Camera { get; }
    public RuntimeEntity? GetEntity(string idOrName) => _scene.GetEntity(idOrName);
}

public sealed class AndroidScriptEntity
{
    private readonly RuntimeEntity _entity;
    private readonly Action<string> _applyMotion;
    private readonly Action<float?, bool?> _setMotionState;

    internal AndroidScriptEntity(RuntimeEntity entity, Action<string> applyMotion, Action<float?, bool?> setMotionState)
    {
        _entity = entity;
        _applyMotion = applyMotion;
        _setMotionState = setMotionState;
    }

    public string Id => _entity.Id;
    public string Name { get => _entity.Name; set => _entity.Name = value; }
    public Vector3 Position { get => _entity.Position; set => _entity.Position = value; }
    public Vector3 RotationDegrees { get => _entity.RotationDegrees; set => _entity.RotationDegrees = value; }
    public Vector3 Scale { get => _entity.Scale; set => _entity.Scale = value; }
    public string ReceiveShadowMode { get => _entity.ReceiveShadowMode; set => _entity.ReceiveShadowMode = value; }

    public void SetPosition(float x, float y, float z) => Position = new Vector3(x, y, z);
    public void SetPosition(Vector3 position) => Position = position;
    public void ApplyMotion(string motionPath) => _applyMotion(motionPath);
    public void PlayMotion(bool restart = false) => _setMotionState(restart ? 0.0f : null, true);
    public void PauseMotion() => _setMotionState(null, false);
    public void StopMotion() => _setMotionState(0.0f, false);
    public void SeekMotionFrame(float frame) => _setMotionState(Math.Max(frame, 0.0f), null);
    public bool TryResetPhysics() => _entity.TryResetPhysics();
    public void ResetPhysics() => _entity.ResetPhysics();
    public void ResetMotionPhysics() => ResetPhysics();
    public void Speak(string text, Action? onCompleted = null) => onCompleted?.Invoke();
    public void Speak(string text, int speakerId = 0, float speed = 1.0f, float volume = 1.0f, Action? onCompleted = null)
        => onCompleted?.Invoke();
}

public sealed class AndroidScriptInput
{
    // Android gameplay input is touch-driven. Keyboard queries are retained so
    // desktop-authored scripts compile and simply remain inactive on touch-only devices.
    public bool IsKeyDown(string key) => false;
}

public sealed class AndroidScriptAudio
{
    private readonly Func<string, bool> _play;
    private readonly Func<string, bool> _pause;
    private readonly Func<string, bool> _stop;

    internal AndroidScriptAudio(Func<string, bool> play, Func<string, bool> pause, Func<string, bool> stop)
    {
        _play = play;
        _pause = pause;
        _stop = stop;
    }

    public bool Play(string idOrName) => _play(idOrName);
    public bool Pause(string idOrName) => _pause(idOrName);
    public bool Stop(string idOrName) => _stop(idOrName);
}

public sealed class AndroidScriptCamera
{
    private readonly RuntimeScene _scene;
    private readonly string _projectDirectory;

    internal AndroidScriptCamera(RuntimeScene scene, string projectDirectory)
    {
        _scene = scene;
        _projectDirectory = projectDirectory;
    }

    public void SetCameraVmd(string cameraName, string path, bool loop = true, float playbackSpeed = 1.0f, bool play = true)
    {
        RuntimeCamera? camera = Find(cameraName);
        if (camera is null)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android script camera was not found: {cameraName}");
            return;
        }
        string absolutePath = GameProjectPath.ToAbsolute(_projectDirectory, path);
        if (!File.Exists(absolutePath))
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android script camera VMD asset was not found: {absolutePath}");
            return;
        }
        camera.SetVmd(path, loop, playbackSpeed, play);
        camera.Settings.ControlMode = "vmd";
        global::Android.Util.Log.Info("ZhengyanGamePlayer", $"Android script applied camera VMD '{path}' to '{camera.Name}'.");
    }
    public void PlayCameraVmd(string cameraName, bool restart = false) => Find(cameraName)?.PlayVmd(restart);
    public void PauseCameraVmd(string cameraName) => Find(cameraName)?.PauseVmd();
    public void SeekCameraVmd(string cameraName, float frame) => Find(cameraName)?.SeekVmd(frame);
    public void ClearCameraVmd(string cameraName)
    {
        RuntimeCamera? camera = Find(cameraName);
        if (camera is null) return;
        camera.SetVmd(string.Empty, play: false);
        camera.Settings.ControlMode = "custom";
    }
    public void UseEditorOrbitMode()
    {
        _scene.MainCamera.Settings.ControlMode = "editor";
    }

    private RuntimeCamera? Find(string idOrName) => _scene.Cameras.FirstOrDefault(camera =>
        string.Equals(camera.Id, idOrName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(camera.Name, idOrName, StringComparison.OrdinalIgnoreCase));
}

public sealed class AndroidScriptGlobals
{
    public AndroidScriptGlobals(
        AndroidScriptScene scene,
        AndroidScriptEntity entity,
        AndroidScriptInput input,
        AndroidScriptAudio audio,
        float deltaSeconds,
        bool isStart,
        bool isUpdate,
        AndroidRuntimeEvent? runtimeEvent = null,
        AndroidScriptServices? services = null)
    {
        Scene = scene;
        Entity = entity;
        Input = input;
        Audio = audio;
        DeltaSeconds = deltaSeconds;
        IsStart = isStart;
        IsUpdate = isUpdate;
        Event = runtimeEvent;
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public AndroidScriptScene Scene { get; }
    public AndroidScriptEntity Entity { get; }
    public AndroidScriptInput Input { get; }
    public AndroidScriptAudio Audio { get; }
    public float DeltaSeconds { get; }
    public bool IsStart { get; }
    public bool IsUpdate { get; }

    /// <summary>非 null 时表示一次 GUI/Sprite/触摸事件；Start/Update 时为 null。</summary>
    public AndroidRuntimeEvent? Event { get; }

    public bool IsEvent => Event is not null;
    public bool IsGuiEvent => string.Equals(Event?.Type, "gui", StringComparison.OrdinalIgnoreCase);
    public bool IsSpriteEvent => string.Equals(Event?.Type, "sprite", StringComparison.OrdinalIgnoreCase);
    public bool IsSpeechEvent => string.Equals(Event?.Type, "speech", StringComparison.OrdinalIgnoreCase);
    public string GuiControlId => IsGuiEvent ? Event!.Id : string.Empty;
    public string GuiControlName => IsGuiEvent ? Event!.Text : string.Empty;
    public string GuiEventName => IsGuiEvent ? Event!.EventName : string.Empty;
    public string SpeechCallbackName => IsSpeechEvent ? Event!.EventName : string.Empty;

    public AndroidScriptServices Services { get; }
}

public sealed class AndroidScriptServices
{
    private readonly RuntimeScene _scene;
    private readonly Action<string> _requestSceneChange;
    private readonly Func<string, bool> _playAudio;
    private readonly Func<string, bool> _pauseAudio;
    private readonly Func<string, bool> _stopAudio;
    private readonly Func<string, bool> _refreshRenderTexture;
    private readonly Func<string, string, float, bool> _configureRenderTexture;
    private readonly Func<string, AndroidRenderTextureInfo?> _getRenderTexture;
    private readonly Func<IReadOnlyList<AndroidRenderTextureInfo>> _listRenderTextures;

    internal AndroidScriptServices(
        RuntimeScene scene,
        Action<string> requestSceneChange,
        Func<string, bool> playAudio,
        Func<string, bool> pauseAudio,
        Func<string, bool> stopAudio,
        Func<string, bool> refreshRenderTexture,
        Func<string, string, float, bool> configureRenderTexture,
        Func<string, AndroidRenderTextureInfo?> getRenderTexture,
        Func<IReadOnlyList<AndroidRenderTextureInfo>> listRenderTextures)
    {
        _scene = scene;
        _requestSceneChange = requestSceneChange;
        _playAudio = playAudio;
        _pauseAudio = pauseAudio;
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
    public bool PauseAudio(string idOrName) => _pauseAudio(idOrName);
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
