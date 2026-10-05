using Android.App;
using Android.Content;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

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
    private readonly Func<RuntimeEntity, PmxModelComponent?> _resolvePmxModel;
    private readonly Func<string, float, bool> _setAudioVolume;
    private readonly Func<string, bool, bool> _setAudioLoop;
    private readonly Func<string, bool> _isAudioPlaying;
    private readonly GameProjectLlmSettings _llmSettings;
    private readonly MainThreadDispatcher _dispatcher = new();
    private readonly Dictionary<string, AndroidCompiledScript> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FailedScriptVersion> _failedScripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ScriptExecutionKey, AndroidScriptExecutionContext> _executionContexts = [];
    private readonly HashSet<string> _started = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeEntity? _loadingEntity;
    private IReadOnlyList<ScriptBinding> _loadingBindings = [];
    private double _fps;
    private bool _disposed;
    private RuntimeScene? _activeScene;
    private readonly GameProjectLipSyncSettings _lipSyncSettings;
    private SpeechDictionarySet? _speechDictionaries;
    private AndroidSpeechLipSync? _lipSync;
    private long _speechVersion;
    private readonly CancellationTokenSource _speechLifetime = new();

    public void PumpCallbacks()
    {
        if (!_disposed) _dispatcher.Pump();
    }

    public AndroidScriptGlobals CreateLoadingGlobals(RuntimeScene scene, RuntimeEntity entity, AndroidInputSnapshot input)
        => CreateExecutionContext(scene, entity, input).Globals;

    public void DispatchLoadingEvent(
        RuntimeScene scene,
        RuntimeEntity entity,
        IEnumerable<ScriptBinding> bindings,
        string eventName,
        float progress,
        string message)
    {
        if (_disposed) return;
        AndroidRuntimeEvent runtimeEvent = new(
            "loading", Guid.NewGuid().ToString("N"), eventName, Vector2.Zero, message, entity.Id, Progress: progress);
        foreach (ScriptBinding binding in bindings.Where(IsSupported))
        {
            Execute(binding, scene, entity, 0.0f, false, runtimeEvent, AndroidInputSnapshot.Empty);
        }
    }

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
        Action<RuntimeEntity, float?, bool?>? setMotionState = null,
        Func<RuntimeEntity, PmxModelComponent?>? resolvePmxModel = null,
        Func<string, float, bool>? setAudioVolume = null,
        Func<string, bool, bool>? setAudioLoop = null,
        Func<string, bool>? isAudioPlaying = null,
        GameProjectLlmSettings? llmSettings = null,
        GameProjectLipSyncSettings? lipSyncSettings = null)
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
        _resolvePmxModel = resolvePmxModel ?? (_ => null);
        _setAudioVolume = setAudioVolume ?? ((_, _) => false);
        _setAudioLoop = setAudioLoop ?? ((_, _) => false);
        _isAudioPlaying = isAudioPlaying ?? (_ => false);
        _llmSettings = llmSettings ?? new GameProjectLlmSettings();
        _lipSyncSettings = lipSyncSettings ?? new GameProjectLipSyncSettings();
    }

    public void Start(RuntimeScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, scene, entity, 0.0f, true, input: AndroidInputSnapshot.Empty);
            }
        }
    }

    public void StartLoading(RuntimeScene scene)
    {
        StopSpeech();
        AndroidScriptBubbleManager.Shared.Clear();
        if (_activeScene is not null)
        {
            AndroidScriptAsr.Shared.Cancel();
            AndroidScriptTts.Shared.Stop();
        }
        foreach (AndroidScriptExecutionContext context in _executionContexts.Values)
            context.Globals.Services.Dispose();
        _executionContexts.Clear();
        _activeScene = scene;
        _loadingEntity = new RuntimeEntity(new GameEntity
        {
            Id = "__scene_loading__",
            Name = scene.Name,
            Type = "scene"
        });
        _loadingBindings = scene.Definition.LoadingScripts.Where(IsSupported).ToArray();
        DispatchLoadingEvent(scene, _loadingEntity, _loadingBindings, "loading_started", 0.0f, $"Loading scene: {scene.Name}");
    }

    public void UpdateLoading(RuntimeScene scene, float progress, string message, bool completed)
    {
        if (_loadingEntity is null) return;
        DispatchLoadingEvent(scene, _loadingEntity, _loadingBindings,
            completed ? "loading_completed" : "loading_progress", progress, message);
        if (completed)
        {
            DisposeLoadingContexts();
            _loadingEntity = null;
            _loadingBindings = [];
        }
    }

    public void FailLoading(RuntimeScene scene, float progress, string message)
    {
        if (_loadingEntity is null) return;
        DispatchLoadingEvent(scene, _loadingEntity, _loadingBindings, "loading_failed", progress, message);
        DisposeLoadingContexts();
        _loadingEntity = null;
        _loadingBindings = [];
    }

    private void DisposeLoadingContexts()
    {
        foreach (ScriptExecutionKey key in _executionContexts.Keys.Where(key => ReferenceEquals(key.Entity, _loadingEntity)).ToArray())
        {
            _executionContexts[key].Globals.Services.Dispose();
            _executionContexts.Remove(key);
        }
    }

    public void Update(RuntimeScene scene, float deltaSeconds, AndroidInputSnapshot? input = null)
    {
        if (deltaSeconds > 0.0001f)
        {
            double instant = 1.0 / deltaSeconds;
            _fps = _fps <= 0.0 ? instant : _fps + (instant - _fps) * 0.1;
        }
        foreach (RuntimeEntity entity in scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, scene, entity, deltaSeconds, false, input: input ?? AndroidInputSnapshot.Empty);
            }
        }
    }

    public void DispatchEvent(RuntimeScene scene, AndroidRuntimeEvent runtimeEvent)
    {
        if (_disposed || !ReferenceEquals(scene, _activeScene))
        {
            return;
        }

        if (_loadingEntity is not null && runtimeEvent.TargetEntity == _loadingEntity.Id)
        {
            foreach (ScriptBinding binding in _loadingBindings)
                Execute(binding, scene, _loadingEntity, 0.0f, false, runtimeEvent);
            return;
        }
        RuntimeEntity? target = scene.GetEntity(runtimeEvent.TargetEntity);
        if (target is null && !string.IsNullOrEmpty(runtimeEvent.TargetEntity)) return;
        IEnumerable<RuntimeEntity> entities = target is null ? scene.Entities : [target];
        foreach (RuntimeEntity entity in entities)
        {
            foreach (ScriptBinding binding in entity.Definition.Scripts.Where(IsSupported))
            {
                Execute(binding, scene, entity, 0.0f, false, runtimeEvent);
            }
        }
    }

    public Task<string?> InvokeLlmToolAsync(
        RuntimeScene scene,
        RuntimeEntity target,
        string callbackName,
        RuntimeLlmToolCall toolCall)
        => _dispatcher.InvokeAsync(() => InvokeLlmTool(scene, target, callbackName, toolCall));

    private string? InvokeLlmTool(RuntimeScene scene, RuntimeEntity target, string callbackName, RuntimeLlmToolCall toolCall)
    {
        if (_disposed || !ReferenceEquals(scene, _activeScene)) return null;
        AndroidRuntimeEvent runtimeEvent = new(
            "llm",
            Guid.NewGuid().ToString("N"),
            "tool_execute",
            Vector2.Zero,
            string.Empty,
            target.Id,
            toolCall,
            Error: string.Empty,
            CallbackName: callbackName);
        IEnumerable<ScriptBinding> bindings = ReferenceEquals(target, _loadingEntity) ? _loadingBindings : target.Definition.Scripts;
        foreach (ScriptBinding binding in bindings.Where(IsSupported))
        {
            string path = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
            if (!_runners.TryGetValue(path, out AndroidCompiledScript? runner)) continue;
            ScriptExecutionKey key = new(scene, target, path);
            if (!_executionContexts.TryGetValue(key, out AndroidScriptExecutionContext? context))
            {
                context = CreateExecutionContext(scene, target, AndroidInputSnapshot.Empty);
                _executionContexts[key] = context;
            }
            context.Update(0.0f, false, runtimeEvent, AndroidInputSnapshot.Empty);
            try
            {
                object? result = runner.ExecuteResult(context.Globals, context.SubmissionArray);
                if (result is null) continue;
                return result switch
                {
                    string text => text,
                    _ => JsonSerializer.Serialize(result)
                };
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }
        return null;
    }

    private AndroidScriptExecutionContext CreateExecutionContext(
        RuntimeScene scene,
        RuntimeEntity entity,
        AndroidInputSnapshot input)
    {
        void Dispatch(AndroidRuntimeEvent value) => _dispatcher.Post(() => DispatchEvent(scene, value));
        AndroidScriptAsr.Shared.ConfigureCallbacks(Dispatch);
        AndroidScriptServices services = new(
            scene,
            _projectDirectory,
            _requestSceneChange,
            name => _playAudio(scene, name),
            _pauseAudio,
            _stopAudio,
            _refreshRenderTexture,
            _configureRenderTexture,
            _getRenderTexture,
            _listRenderTextures,
            runtimeEvent => DispatchEvent(scene, runtimeEvent),
            _llmSettings,
            Path.Combine(Application.Context.FilesDir!.AbsolutePath, "saves"),
            _dispatcher,
            AndroidScriptTts.Shared,
            AndroidScriptRealtime.Shared,
            AndroidScriptAsr.Shared,
            new AndroidScriptRealtimeVoice(Dispatch));
        AndroidScriptInput scriptInput = new(input);
        AndroidScriptGlobals globals = new(
            new AndroidScriptScene(scene, _projectDirectory, _requestSceneChange, () => _fps, services),
            new AndroidScriptEntity(
                entity,
                path => _applyMotion(scene, entity, path),
                (frame, playing) => _setMotionState(entity, frame, playing),
                () => _resolvePmxModel(entity),
                ResolveScriptAssetPath,
                (text, callback) => _ = SpeakAsync(scene, entity, text, 1.0f, 1.0f, null, callback),
                StopSpeech,
                (callbackName, toolCall) => InvokeLlmToolAsync(scene, entity, callbackName, toolCall),
                (text, speakerId, speed, volume, completed, callback) => _ = SpeakAsync(scene, entity, text, speed, volume, completed, callback)),
            scriptInput,
            new AndroidScriptAudio(name => _playAudio(scene, name), _pauseAudio, _stopAudio, _setAudioVolume, _setAudioLoop, _isAudioPlaying),
            0.0f,
            false,
            false,
            null,
            services);
        return new AndroidScriptExecutionContext(globals, scriptInput);
    }

    private async Task SpeakAsync(RuntimeScene scene, RuntimeEntity entity, string text, float speed, float volume, Action? completed, string callback)
    {
        long version = ++_speechVersion;
        _lipSync?.Dispose();
        _lipSync = null;
        try
        {
            await AndroidScriptTts.Shared.SpeakWithProgressAsync(text, speed, volume,
                () => _dispatcher.Post(() =>
                {
                    if (_disposed || version != _speechVersion || !ReferenceEquals(scene, _activeScene) || !_lipSyncSettings.Enabled) return;
                    try
                    {
                        PmxModelComponent? model = _resolvePmxModel(entity);
                        if (model is null) return;
                        _speechDictionaries ??= AndroidSpeechLipSync.LoadDictionaries(_lipSyncSettings,
                            _projectDirectory, AndroidBundledResourceStore.RootDirectory ?? AppContext.BaseDirectory);
                        _lipSync = new AndroidSpeechLipSync(model, _speechDictionaries, _lipSyncSettings, text, speed);
                    }
                    catch (Exception ex) { global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"TTS lip sync failed: {ex.Message}"); }
                }),
                (start, end) => _dispatcher.Post(() =>
                {
                    if (!_disposed && version == _speechVersion && start >= 0 && end > start && end <= text.Length)
                        _lipSync?.SetRange(text[start..end]);
                }), _speechLifetime.Token).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                if (_disposed || version != _speechVersion || !ReferenceEquals(scene, _activeScene)) return;
                _lipSync?.Dispose();
                _lipSync = null;
                completed?.Invoke();
                if (!string.IsNullOrWhiteSpace(callback))
                    DispatchEvent(scene, new AndroidRuntimeEvent("speech", Guid.NewGuid().ToString("N"), callback, Vector2.Zero, text, entity.Id));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Speech failed: {ex.Message}"); }
        finally
        {
            _dispatcher.Post(() =>
            {
                if (version != _speechVersion) return;
                _lipSync?.Dispose();
                _lipSync = null;
            });
        }
    }

    private void StopSpeech()
    {
        ++_speechVersion;
        _lipSync?.Dispose();
        _lipSync = null;
        AndroidScriptTts.Shared.Stop();
    }

    private void Execute(
        ScriptBinding binding,
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent = null,
        AndroidInputSnapshot? input = null)
    {
        string path = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
        if (_runners.TryGetValue(path, out AndroidCompiledScript? cachedRunner))
        {
            try { ExecuteCached(cachedRunner, path, scene, entity, deltaSeconds, isStart, runtimeEvent, input); }
            catch (Exception ex) { RecordScriptFailure(path, ex); }
            return;
        }

        if (!File.Exists(path)) return;
        FileInfo sourceFile = new(path);
        FailedScriptVersion version = new(sourceFile.LastWriteTimeUtc.Ticks, sourceFile.Length);
        if (_failedScripts.TryGetValue(path, out FailedScriptVersion failedVersion))
        {
            if (failedVersion == version)
            {
                return;
            }

            _failedScripts.Remove(path);
            _runners.Remove(path);
        }

        try
        {
            AndroidCompiledScript? runner = TryLoadPrecompiled(path);
            if (runner is null)
            {
                // Runtime Roslyn compilation is kept as a compatibility
                // fallback for older packages, but it must not be an
                // invisible per-frame cost when an assembly is missing.
                if (sourceFile.Length > 0)
                {
                    global::Android.Util.Log.Warn(
                        "ZhengyanGamePlayer",
                        $"Android C# script is not precompiled; compiling once at runtime: '{path}'. " +
                        "Re-export the .dwgame with Android C# precompilation enabled to avoid startup stalls.");
                }

                runner = Compile(path);
            }

            _runners[path] = runner;
            ExecuteCached(runner, path, scene, entity, deltaSeconds, isStart, runtimeEvent, input);
        }
        catch (Exception ex)
        {
            RecordScriptFailure(path, ex);
        }
    }

    private void RecordScriptFailure(string path, Exception error)
    {
        _runners.Remove(path);
        FileInfo source = new(path);
        _failedScripts[path] = new(source.LastWriteTimeUtc.Ticks, source.Exists ? source.Length : 0);
        global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Android C# script failed '{path}': {error}");
    }

    private void ExecuteCached(
        AndroidCompiledScript runner,
        string path,
        RuntimeScene scene,
        RuntimeEntity entity,
        float deltaSeconds,
        bool isStart,
        AndroidRuntimeEvent? runtimeEvent,
        AndroidInputSnapshot? input)
    {
        ScriptExecutionPhases phase = isStart
            ? ScriptExecutionPhases.Start
            : runtimeEvent is null ? ScriptExecutionPhases.Update : ScriptExecutionPhases.Event;
        if (runner.IsNoOp || (runner.Phases & phase) == 0)
        {
            return;
        }

        ScriptExecutionKey key = new(scene, entity, path);
        if (!_executionContexts.TryGetValue(key, out AndroidScriptExecutionContext? context))
        {
            context = CreateExecutionContext(scene, entity, input ?? AndroidInputSnapshot.Empty);
            _executionContexts.Add(key, context);
        }

        context.Update(deltaSeconds, isStart, runtimeEvent, input ?? AndroidInputSnapshot.Empty);
        runner.Execute(context.Globals, context.SubmissionArray);
    }

    private string ResolveScriptAssetPath(string value)
    {
        string normalized = GameProjectPath.NormalizePathText(value ?? string.Empty);
        if (normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(AndroidBundledResourceStore.RootDirectory))
        {
            string relative = normalized["app:".Length..].TrimStart('/', '\\');
            return Path.Combine(AndroidBundledResourceStore.RootDirectory, relative);
        }

        return GameProjectPath.ToAbsolute(_projectDirectory, normalized);
    }

    private static AndroidCompiledScript Compile(string path)
    {
        string source = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(source)) return AndroidCompiledScript.NoOp;
        byte[] image = AndroidScriptCompiler.Compile(path, AndroidScriptMetadata.GetReferences());
        return CreateCompiledScript(Assembly.Load(image), AnalyzeScriptPhases(source));
    }

    private AndroidCompiledScript? TryLoadPrecompiled(string sourcePath)
    {
        string relative = Path.GetRelativePath(_projectDirectory, sourcePath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        string assemblyPath = Path.Combine(_projectDirectory, "compiled", "android", Path.ChangeExtension(relative, ".dll"));
        if (!File.Exists(assemblyPath)) return null;
        try
        {
            return CreateCompiledScript(Assembly.Load(File.ReadAllBytes(assemblyPath)), AnalyzeScriptPhases(File.ReadAllText(sourcePath)));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ZhengyanGamePlayer", $"Precompiled Android C# script could not be loaded '{assemblyPath}': {ex.Message}");
            return null;
        }
    }
    private static AndroidCompiledScript CreateCompiledScript(Assembly assembly, ScriptExecutionPhases phases)
    {
        Type scriptType = assembly.GetType("Script")
            ?? throw new InvalidOperationException("Android C# script did not produce a Script type.");
        MethodInfo factory = scriptType.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Android C# script did not produce an execution factory.");
        return new AndroidCompiledScript(factory, phases);
    }

    private static ScriptExecutionPhases AnalyzeScriptPhases(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ScriptExecutionPhases.None;
        }

        SyntaxNode root = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script)).GetRoot();
        ScriptExecutionPhases phases = ScriptExecutionPhases.None;
        foreach (GlobalStatementSyntax global in root.DescendantNodes().OfType<GlobalStatementSyntax>())
        {
            if (global.Statement is EmptyStatementSyntax)
            {
                continue;
            }

            if (global.Statement is not IfStatementSyntax conditional
                || conditional.Else is not null)
            {
                return ScriptExecutionPhases.All;
            }

            ScriptExecutionPhases conditionalPhase = ClassifyCondition(conditional.Condition);
            if (conditionalPhase == ScriptExecutionPhases.All)
            {
                return ScriptExecutionPhases.All;
            }

            if (HasExecutableStatement(conditional.Statement))
            {
                phases |= conditionalPhase;
            }
        }

        return phases;
    }

    private static ScriptExecutionPhases ClassifyCondition(ExpressionSyntax condition)
    {
        IdentifierNameSyntax[] phaseIdentifiers = condition.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText is
                nameof(AndroidScriptGlobals.IsStart)
                or nameof(AndroidScriptGlobals.IsUpdate)
                or nameof(AndroidScriptGlobals.IsEvent)
                or nameof(AndroidScriptGlobals.IsGuiEvent)
                or nameof(AndroidScriptGlobals.IsSpriteEvent)
                or nameof(AndroidScriptGlobals.IsSpeechEvent)
                or nameof(AndroidScriptGlobals.IsLoadingEvent)
                or nameof(AndroidScriptGlobals.IsLlmEvent)
                or nameof(AndroidScriptGlobals.IsAsrEvent)
                or nameof(AndroidScriptGlobals.IsRealtimeVoiceEvent))
            .ToArray();
        if (phaseIdentifiers.Any(identifier => HasUnsafeConditionAncestor(identifier, condition)))
        {
            return ScriptExecutionPhases.All;
        }

        HashSet<string> identifiers = phaseIdentifiers
            .Select(identifier => identifier.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        bool start = identifiers.Contains(nameof(AndroidScriptGlobals.IsStart));
        bool update = identifiers.Contains(nameof(AndroidScriptGlobals.IsUpdate));
        bool runtimeEvent = identifiers.Overlaps(
        [
            nameof(AndroidScriptGlobals.IsEvent),
            nameof(AndroidScriptGlobals.IsGuiEvent),
            nameof(AndroidScriptGlobals.IsSpriteEvent),
            nameof(AndroidScriptGlobals.IsSpeechEvent),
            nameof(AndroidScriptGlobals.IsLoadingEvent),
            nameof(AndroidScriptGlobals.IsLlmEvent),
            nameof(AndroidScriptGlobals.IsAsrEvent),
            nameof(AndroidScriptGlobals.IsRealtimeVoiceEvent)
        ]);
        int phaseCount = (start ? 1 : 0) + (update ? 1 : 0) + (runtimeEvent ? 1 : 0);
        if (phaseCount != 1)
        {
            return ScriptExecutionPhases.All;
        }

        return start
            ? ScriptExecutionPhases.Start
            : update ? ScriptExecutionPhases.Update : ScriptExecutionPhases.Event;
    }

    private static bool HasExecutableStatement(StatementSyntax statement)
        => statement is not BlockSyntax block || block.Statements.Count != 0;

    private static bool HasUnsafeConditionAncestor(IdentifierNameSyntax identifier, ExpressionSyntax condition)
    {
        if (ReferenceEquals(identifier, condition))
        {
            return false;
        }

        for (SyntaxNode? current = identifier.Parent; current is not null; current = current.Parent)
        {
            if (current is ParenthesizedExpressionSyntax)
            {
                if (ReferenceEquals(current, condition)) return false;
                continue;
            }

            if (current is BinaryExpressionSyntax binary
                && (binary.IsKind(SyntaxKind.LogicalAndExpression)
                    || binary.IsKind(SyntaxKind.LogicalOrExpression)))
            {
                if (ReferenceEquals(current, condition)) return false;
                continue;
            }

            return true;
        }

        return false;
    }


    private sealed class AndroidCompiledScript
    {
        private readonly Func<object?[], Task<object?>>? _factory;

        public static AndroidCompiledScript NoOp { get; } = new(null, ScriptExecutionPhases.None);

        public AndroidCompiledScript(MethodInfo? factory, ScriptExecutionPhases phases)
        {
            _factory = factory is null
                ? null
                : factory.CreateDelegate<Func<object?[], Task<object?>>>();
            Phases = phases;
        }

        public bool IsNoOp => _factory is null;
        public ScriptExecutionPhases Phases { get; }

        public object? ExecuteResult(AndroidScriptGlobals globals, object?[] submissionArray)
        {
            if (_factory is null)
            {
                return null;
            }

            submissionArray[0] = globals;
            submissionArray[1] = null;
            Task<object?> task = _factory(submissionArray);
            return task.GetAwaiter().GetResult();
        }

        public void Execute(AndroidScriptGlobals globals, object?[] submissionArray)
        {
            _ = ExecuteResult(globals, submissionArray);
        }
    }

    private sealed class AndroidScriptExecutionContext(AndroidScriptGlobals globals, AndroidScriptInput input)
    {
        public AndroidScriptGlobals Globals { get; } = globals;
        public object?[] SubmissionArray { get; } = new object?[2];

        public void Update(float deltaSeconds, bool isStart, AndroidRuntimeEvent? runtimeEvent, AndroidInputSnapshot snapshot)
        {
            input.Update(snapshot);
            Globals.Update(deltaSeconds, isStart, runtimeEvent);
        }
    }

    private readonly record struct ScriptExecutionKey(RuntimeScene Scene, RuntimeEntity Entity, string Path);

    [Flags]
    private enum ScriptExecutionPhases
    {
        None = 0,
        Start = 1,
        Update = 2,
        Event = 4,
        All = Start | Update | Event
    }

    private readonly record struct FailedScriptVersion(long LastWriteTimeUtcTicks, long Length);

    private static bool IsSupported(ScriptBinding binding)
    {
        return binding.Enabled
            && (string.Equals(binding.Language, "csharp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(binding.Language, "cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(binding.Language, "csx", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(binding.Language)
                    && Path.GetExtension(binding.Path).ToLowerInvariant() is ".cs" or ".csx"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSpeech();
        _speechLifetime.Cancel();
        _speechLifetime.Dispose();
        _activeScene = null;
        _loadingEntity = null;
        _loadingBindings = [];
        _runners.Clear();
        _failedScripts.Clear();
        foreach (AndroidScriptExecutionContext context in _executionContexts.Values)
            context.Globals.Services.Dispose();
        _executionContexts.Clear();
        _started.Clear();
    }
}
