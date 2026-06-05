using System.Reflection;
using System.Numerics;
using ImGuiNET;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class GamePlayerGame : Zhengyan.DigitalWife.Mmd.Game.Game
{
    private readonly record struct LoadingStep(string Message, Action Action);

    private readonly OrbitCamera _camera = new();
    private readonly string _projectDirectory;
    private readonly List<PlayerPmxObject> _pmxObjects = [];
    private readonly List<RuntimeParticleObject> _particleObjects = [];
    private readonly List<RuntimeWaterObject> _waterObjects = [];
    private readonly List<RuntimePlaneObject> _planeObjects = [];
    private readonly List<(RuntimeEntity Entity, List<IScriptInstance> Scripts, string Name)> _scriptTargets = [];
    private readonly List<IScriptInstance> _loadingScripts = [];
    private readonly Queue<LoadingStep> _loadingSteps = [];
    private readonly MainThreadDispatcher _dispatcher = new();
    private readonly Dictionary<string, RuntimeEntity> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEntity> _entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _audioClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioSource> _audioSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _waterRippleTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptHost _scriptHost;
    private LoadingScreenComponent? _loadingScreen;
    private RuntimeGuiOverlayComponent? _guiOverlay;
    private RuntimeCameraControllerComponent? _cameraController;
    private RuntimeDebugDrawComponent? _debugDraw;
    private SceneRenderTextureManager? _renderTextureManager;
    private SkyboxComponent? _skybox;
    private RuntimeScene? _runtimeScene;
    private RuntimeInput? _runtimeInput;
    private RuntimeAudio? _runtimeAudio;
    private RuntimeVoice? _runtimeVoice;
    private RuntimeLlm? _runtimeLlm;
    private RuntimeAsr? _runtimeAsr;
    private RuntimeRealtimeVoice? _runtimeRealtimeVoice;
    private RuntimePerformance _runtimePerformance = new();
    private RuntimeEntity? _loadingEntity;
    private string? _pendingScenePath;
    private string _loadingMessage = "Loading...";
    private int _loadingTotalSteps;
    private int _loadingCompletedSteps;
    private int _loadingDelayFrames;
    private bool _loadingReadyToFinish;
    private bool _isLoading;
    private bool _renderedSceneThisFrame;
    private string _hoveredSpriteId = string.Empty;
    private string _pressedSpriteId = string.Empty;
    private bool _wasLeftMouseDown;

    public GamePlayerGame(string projectDirectory)
        : base(CreateInitialOptions(projectDirectory))
    {
        _projectDirectory = projectDirectory;
        _scriptHost = new ScriptHost(projectDirectory);
    }

    public GameProject Project { get; private set; } = new();

    public OrbitCamera Camera => _camera;

    public IReadOnlyList<PlayerPmxObject> PmxObjects => _pmxObjects;

    public IReadOnlyList<RuntimeParticleObject> ParticleObjects => _particleObjects;

    public IReadOnlyList<RuntimeWaterObject> WaterObjects => _waterObjects;

    public IReadOnlyList<RuntimePlaneObject> PlaneObjects => _planeObjects;

    private float LoadingProgress => _loadingTotalSteps <= 0
        ? 0.0f
        : Math.Clamp(_loadingCompletedSteps / (float)_loadingTotalSteps, 0.0f, 1.0f);

    protected override void Initialize()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png");
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
    }

    private static GameOptions CreateInitialOptions(string projectDirectory)
    {
        GameOptions options = new()
        {
            Title = "Zhengyan.DigitalWife Game Player",
            WindowSize = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            VSync = true,
            Samples = 4,
            UseOpenCL = false,
            EnableAudio = true,
            ClearColor = new Vector4(0.08f, 0.09f, 0.12f, 1.0f),
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        };

        try
        {
            string projectPath = Path.Combine(projectDirectory, GameProjectStore.ProjectFileName);
            if (!File.Exists(projectPath))
            {
                return options;
            }

            GameProject project = GameProjectStore.Load(projectDirectory);
            options.Title = string.IsNullOrWhiteSpace(project.Window.Title) ? options.Title : project.Window.Title;
            options.WindowSize = new Silk.NET.Maths.Vector2D<int>(
                Math.Max(320, project.Window.Width),
                Math.Max(240, project.Window.Height));
            options.IsFullscreen = project.Window.DesktopSpriteMode ? false : project.Window.Fullscreen;
            options.IsResizable = project.Window.Resizable;
            options.IsTopMost = project.Window.DesktopSpriteMode;
            options.HideWindowBorder = project.Window.DesktopSpriteMode;
            options.TransparentFramebuffer = project.Window.DesktopSpriteMode;
            if (project.Window.DesktopSpriteMode)
            {
                options.ClearColor = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            }
        }
        catch
        {
        }

        return options;
    }

    protected override void LoadContent()
    {
        _cameraController = AddComponent(new RuntimeCameraControllerComponent(_camera, ResolveRuntimeEntity)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f,
            UpdateOrder = int.MaxValue - 100
        });

        _runtimeInput = new RuntimeInput(this);
        _renderTextureManager = new SceneRenderTextureManager(this, () => Project.Scene, GetRenderTextureExcludedComponents);

        _ = AddComponent(new GroundShadowPassComponent(this)
        {
            DrawOrder = 110
        });

        _debugDraw = AddComponent(new RuntimeDebugDrawComponent(_camera)
        {
            DrawOrder = int.MaxValue - 100
        });

        _guiOverlay = AddComponent(new RuntimeGuiOverlayComponent(
            () => Project.Scene.GuiControls,
            () => Project.Scene.Sprites,
            () => _runtimeScene,
            _camera,
            () => Project.Window,
            ResolveProjectPath,
            DispatchGuiEvent)
        {
            RuntimeTextureProvider = _renderTextureManager,
            DrawOrder = int.MaxValue - 10,
            UpdateOrder = int.MaxValue
        });

        BeginProjectLoad();
    }

    protected override void Update(GameTime gameTime)
    {
        _runtimePerformance.Update(gameTime);
        _dispatcher.Pump();

        if (_isLoading)
        {
            ProcessLoadingStep();
            return;
        }

        if (_pendingScenePath is not null)
        {
            string nextScenePath = _pendingScenePath;
            _pendingScenePath = null;
            BeginSceneLoad(nextScenePath);
            return;
        }

        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        UpdateSpritePointerEvents();

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, string name) in _scriptTargets.ToArray())
        {
            entity.SyncFromModel();
            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.Update(entity, _runtimeScene, _runtimeInput, _runtimeAudio, gameTime.ElapsedSeconds);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script update failed for entity '{name}': {ex.Message}");
                }
            }
        }
    }

    protected override void LateUpdate(GameTime gameTime)
    {
        if (_isLoading || _runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        UpdateWaterInteractions(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _renderTextureManager?.RenderAll(gameTime, _camera, ApplyRuntimeCamera, ApplyRuntimeCamera);
        _renderedSceneThisFrame = false;

        if (TryDrawCameraViewports(gameTime))
        {
            _renderedSceneThisFrame = true;
            return;
        }

        ApplyRuntimeCamera(_camera);
    }

    private bool TryDrawCameraViewports(GameTime gameTime)
    {
        if (_renderTextureManager is null || GraphicsDevice is null)
        {
            return false;
        }

        List<SceneCameraSettings> viewportCameras = Project.Scene.Cameras
            .Where(camera => camera.Enabled && camera.Viewport.Enabled)
            .ToList();
        if (viewportCameras.Count == 0)
        {
            return false;
        }

        Silk.NET.OpenGLES.GL gl = GraphicsDevice.Gl;
        int screenWidth = Math.Max(GraphicsDevice.BackBufferSize.X, 1);
        int screenHeight = Math.Max(GraphicsDevice.BackBufferSize.Y, 1);
        gl.BindFramebuffer(Silk.NET.OpenGLES.GLEnum.Framebuffer, 0);
        gl.Disable(Silk.NET.OpenGLES.GLEnum.StencilTest);
        gl.Enable(Silk.NET.OpenGLES.GLEnum.ScissorTest);

        foreach (SceneCameraSettings settings in viewportCameras)
        {
            LayoutRect rect = LayoutResolver.Resolve(
                settings.Viewport.LayoutMode,
                settings.Viewport.X,
                settings.Viewport.Y,
                settings.Viewport.Width,
                settings.Viewport.Height,
                screenWidth,
                screenHeight,
                Project.Window.Width,
                Project.Window.Height);
            int x = Math.Clamp((int)MathF.Round(rect.X), 0, screenWidth - 1);
            int yTop = Math.Clamp((int)MathF.Round(rect.Y), 0, screenHeight - 1);
            int width = Math.Clamp((int)MathF.Round(rect.Width), 1, screenWidth - x);
            int height = Math.Clamp((int)MathF.Round(rect.Height), 1, screenHeight - yTop);
            int y = Math.Max(screenHeight - yTop - height, 0);

            OrbitCamera camera = _renderTextureManager.ResolveCamera(settings.Name, _camera);
            camera.Width = width;
            camera.Height = height;

            gl.Viewport(x, y, (uint)width, (uint)height);
            gl.Scissor(x, y, (uint)width, (uint)height);
            gl.ColorMask(true, true, true, true);
            gl.DepthMask(true);
            Vector4 clearColor = Project.Scene.Lighting.ClearColor.ToVector4();
            gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
            gl.Clear(Silk.NET.OpenGLES.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGLES.ClearBufferMask.DepthBufferBit | Silk.NET.OpenGLES.ClearBufferMask.StencilBufferBit);

            ApplyRuntimeCamera(camera);
            DrawSceneComponentsOnce(gameTime);
        }

        gl.Disable(Silk.NET.OpenGLES.GLEnum.ScissorTest);
        gl.Viewport(0, 0, (uint)screenWidth, (uint)screenHeight);
        ApplyRuntimeCamera(_camera);
        return true;
    }

    private void DrawSceneComponentsOnce(GameTime gameTime)
    {
        IReadOnlyList<DrawableGameComponent> overlays = GetOverlayComponents();
        foreach (DrawableGameComponent component in Components
            .OfType<DrawableGameComponent>()
            .Where(component => component.Visible && !overlays.Contains(component))
            .OrderBy(component => component.DrawOrder))
        {
            component.Draw(gameTime);
        }
    }

    protected override void UnloadContent()
    {
        ClearRuntimeScene();
        _renderTextureManager?.Dispose();
        _renderTextureManager = null;
        _runtimeVoice?.Dispose();
        _runtimeVoice = null;
        _runtimeAsr?.Dispose();
        _runtimeAsr = null;
        _runtimeRealtimeVoice?.Dispose();
        _runtimeRealtimeVoice = null;
        _runtimeLlm?.Dispose();
        _runtimeLlm = null;
    }

    private void LoadScene(string scenePath)
    {
        BeginSceneLoad(scenePath);
    }

    private void BeginProjectLoad()
    {
        BeginLoading("Loading project...");
        EnqueueLoadingStep("Loading project...", () =>
        {
            Project = GameProjectStore.Load(_projectDirectory);
            ApplyRuntimeSettings();
            ApplyWindowSettings();
            _runtimeVoice?.Dispose();
            _runtimeVoice = new RuntimeVoice(this, _dispatcher, _projectDirectory, Project.Voice);
            _runtimeLlm?.Dispose();
            _runtimeLlm = new RuntimeLlm(Project.Llm, _dispatcher, DispatchLlmEvent);
            _runtimeAsr?.Dispose();
            _runtimeAsr = new RuntimeAsr(_projectDirectory, Project.Asr, _dispatcher, DispatchAsrEvent);
            _runtimeRealtimeVoice?.Dispose();
            _runtimeRealtimeVoice = new RuntimeRealtimeVoice(this, _projectDirectory, Project.RealtimeVoice, Project.Voice, _dispatcher, DispatchRealtimeVoiceEvent);
            _runtimePerformance = new RuntimePerformance();
            EnqueueSceneLoadSteps(Project.DefaultScene);
        });
    }

    private void BeginSceneLoad(string scenePath)
    {
        BeginLoading($"Loading scene: {scenePath}");
        EnqueueSceneLoadSteps(scenePath);
    }

    private void BeginLoading(string message)
    {
        EnsureLoadingScreen();
        _loadingSteps.Clear();
        _loadingTotalSteps = 0;
        _loadingCompletedSteps = 0;
        _loadingDelayFrames = 2;
        _loadingReadyToFinish = false;
        _loadingMessage = message;
        _isLoading = true;
    }

    private void EnsureLoadingScreen()
    {
        if (_loadingScreen is not null)
        {
            return;
        }

        _loadingScreen = AddComponent(new LoadingScreenComponent(
            () => LoadingProgress,
            () => _loadingMessage,
            () => Project.Scene.LoadingScreen,
            ResolveProjectPath)
        {
            DrawOrder = int.MaxValue
        });
    }

    private void EnqueueLoadingStep(string message, Action action)
    {
        _loadingSteps.Enqueue(new LoadingStep(message, action));
        _loadingTotalSteps++;
    }

    private void ProcessLoadingStep()
    {
        if (_loadingReadyToFinish)
        {
            FinishLoading();
            return;
        }

        if (_loadingDelayFrames > 0)
        {
            _loadingDelayFrames--;
            return;
        }

        if (!_loadingSteps.TryDequeue(out LoadingStep step))
        {
            CompleteLoadingOnNextFrame();
            return;
        }

        _loadingMessage = step.Message;
        try
        {
            step.Action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{step.Message} failed: {ex}");
            _loadingMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            _loadingCompletedSteps++;
        }

        DispatchLoadingEvent("loading_progress", LoadingProgress, _loadingMessage);

        if (_loadingSteps.Count == 0)
        {
            CompleteLoadingOnNextFrame();
        }
    }

    private void CompleteLoadingOnNextFrame()
    {
        _loadingCompletedSteps = _loadingTotalSteps;
        _loadingReadyToFinish = true;
        if (!_loadingMessage.StartsWith("Load failed:", StringComparison.Ordinal))
        {
            _loadingMessage = "Loading complete";
        }

        DispatchLoadingEvent("loading_completed", 1.0f, _loadingMessage);
    }

    private void FinishLoading()
    {
        _isLoading = false;
        _loadingReadyToFinish = false;
        DisposeLoadingScripts();
        _loadingMessage = ResolveWindowTitle();

        if (_loadingScreen is not null)
        {
            _ = RemoveComponent(_loadingScreen);
            _loadingScreen = null;
        }

        Title = _loadingMessage;
    }

    private void EnqueueSceneLoadSteps(string scenePath)
    {
        EnqueueLoadingStep($"Reading scene: {scenePath}", () =>
        {
            ClearRuntimeScene();

            Project.Scene = GameProjectStore.LoadScene(_projectDirectory, scenePath);
            Project.DefaultScene = scenePath;
            ApplyCameraSettings();
            ApplySceneSettings();

            if (_runtimeVoice?.IsEnabled == true && Project.Voice.PreloadOnSceneLoad)
            {
                EnqueueLoadingStep("Preloading TTS...", () => _runtimeVoice.Preload());
            }

            if (_runtimeAsr?.Enabled == true && Project.Asr.PreloadOnSceneLoad)
            {
                EnqueueLoadingStep("Preloading ASR...", () => _runtimeAsr.Preload());
            }

            _runtimeAudio = new RuntimeAudio(_audioSources);
            RuntimeCameraControllerComponent cameraController = _cameraController
                ?? throw new InvalidOperationException("Runtime camera controller is not initialized.");
            RuntimeDebugDrawComponent debugDraw = _debugDraw
                ?? throw new InvalidOperationException("Runtime debug draw is not initialized.");
            RuntimeLlm runtimeLlm = _runtimeLlm
                ?? throw new InvalidOperationException("Runtime LLM is not initialized.");
            RuntimeAsr runtimeAsr = _runtimeAsr
                ?? throw new InvalidOperationException("Runtime ASR is not initialized.");
            RuntimeRealtimeVoice runtimeRealtimeVoice = _runtimeRealtimeVoice
                ?? throw new InvalidOperationException("Runtime Realtime Voice is not initialized.");
                _runtimeScene = new RuntimeScene(
                Project.Scene,
                _entitiesById,
                _entitiesByName,
                new RuntimeWindowControl(this, Project.Window),
                new RuntimeProjectControl(this),
                new RuntimeCamera(_camera, cameraController, () => _entitiesById.Values, Project.Scene, _renderTextureManager),
                new RuntimeDebug(debugDraw),
                new RuntimeSaveStore(_projectDirectory),
                runtimeLlm,
                runtimeAsr,
                runtimeRealtimeVoice,
                new RuntimeDialogueBubbleManager(),
                new RuntimeNetwork(),
                _runtimePerformance,
                DispatchSpeechEvent,
                RequestSceneChange);
            PrepareLoadingScripts();
            DispatchLoadingEvent("loading_started", 0.0f, $"Loading scene: {Project.Scene.Name}");

            foreach (GameEntity entity in Project.Scene.Entities)
            {
                GameEntity captured = entity;
                EnqueueLoadingStep($"Loading entity: {captured.Name}", () => LoadEntity(captured));
            }

            foreach (AudioAsset audioAsset in Project.Scene.Audio)
            {
                AudioAsset captured = audioAsset;
                EnqueueLoadingStep($"Loading audio: {captured.Name}", () => LoadAudioAsset(captured));
            }

            EnqueueLoadingStep("Preparing runtime scene...", AttachRuntimeSceneToEntities);
            EnqueueLoadingStep("Starting scripts...", StartScripts);
            EnqueueLoadingStep("Binding PMX relations...", ApplyRelationBindings);
        });
    }

    private void PrepareLoadingScripts()
    {
        DisposeLoadingScripts();

        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        _loadingEntity = new RuntimeEntity(new GameEntity
        {
            Id = "__scene_loading__",
            Name = Project.Scene.Name,
            Type = "scene"
        });
        _loadingEntity.AttachScene(_runtimeScene);

        foreach (ScriptBinding binding in Project.Scene.LoadingScripts.Where(script => script.Enabled))
        {
            string scriptPath = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
            if (!File.Exists(scriptPath))
            {
                Console.Error.WriteLine($"Scene loading script file not found: {scriptPath}");
                continue;
            }

            try
            {
                _loadingScripts.Add(_scriptHost.Load(binding.Language, scriptPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load scene loading script '{binding.Path}': {ex.Message}");
            }
        }
    }

    private void DispatchLoadingEvent(string eventName, float progress, string message)
    {
        if (_loadingScripts.Count == 0 || _loadingEntity is null || _runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach (IScriptInstance script in _loadingScripts.ToArray())
        {
            try
            {
                script.LoadingEvent(_loadingEntity, _runtimeScene, _runtimeInput, _runtimeAudio, eventName, progress, message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Scene loading script event '{eventName}' failed: {ex.Message}");
            }
        }
    }

    private void DisposeLoadingScripts()
    {
        foreach (IScriptInstance script in _loadingScripts)
        {
            script.Dispose();
        }

        _loadingScripts.Clear();
        _loadingEntity = null;
    }

    private void RequestSceneChange(string scenePath)
    {
        _pendingScenePath = scenePath;
    }

    internal bool TryGetClipboardText(out string text)
    {
        text = string.Empty;

        try
        {
            string? imguiClipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrEmpty(imguiClipboard))
            {
                text = imguiClipboard;
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (Input.Context.Keyboards.Count > 0)
            {
                string? keyboardClipboard = Input.Context.Keyboards[0].ClipboardText;
                if (!string.IsNullOrEmpty(keyboardClipboard))
                {
                    text = keyboardClipboard;
                    return true;
                }
            }
        }
        catch
        {
        }

        try
        {
            object windowObject = Window;
            Type windowType = windowObject.GetType();

            string[] propertyNames = ["ClipboardText", "ClipboardString", "Clipboard"];
            foreach (string propertyName in propertyNames)
            {
                PropertyInfo? property = windowType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property?.CanRead != true || property.PropertyType != typeof(string))
                {
                    continue;
                }

                string? clipboard = (string?)property.GetValue(windowObject);
                if (!string.IsNullOrEmpty(clipboard))
                {
                    text = clipboard;
                    return true;
                }
            }

            MethodInfo? method = windowType.GetMethod("GetClipboardText", BindingFlags.Public | BindingFlags.Instance);
            if (method is not null && method.ReturnType == typeof(string) && method.GetParameters().Length == 0)
            {
                string? clipboard = (string?)method.Invoke(windowObject, null);
                if (!string.IsNullOrEmpty(clipboard))
                {
                    text = clipboard;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    internal bool TrySetClipboardText(string text)
    {
        string clipboardText = text ?? string.Empty;
        bool success = false;

        try
        {
            ImGui.SetClipboardText(clipboardText);
            success = true;
        }
        catch
        {
        }

        try
        {
            if (Input.Context.Keyboards.Count > 0)
            {
                Input.Context.Keyboards[0].ClipboardText = clipboardText;
                success = true;
            }
        }
        catch
        {
        }

        try
        {
            object windowObject = Window;
            Type windowType = windowObject.GetType();

            string[] propertyNames = ["ClipboardText", "ClipboardString", "Clipboard"];
            foreach (string propertyName in propertyNames)
            {
                PropertyInfo? property = windowType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property?.CanWrite == true && property.PropertyType == typeof(string))
                {
                    property.SetValue(windowObject, clipboardText);
                    success = true;
                }
            }

            MethodInfo? method = windowType.GetMethod(
                "SetClipboardText",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (method is not null)
            {
                _ = method.Invoke(windowObject, [clipboardText]);
                success = true;
            }
        }
        catch
        {
        }

        return success;
    }

    private void ApplyWindowSettings()
    {
        Title = ResolveWindowTitle();
        Project.Window.TimingMode = RuntimeWindowControl.NormalizeTimingMode(Project.Window.TimingMode);
        AnimationTimingMode = RuntimeWindowControl.ToAnimationTimingMode(Project.Window.TimingMode);
        SetWindowSize(Project.Window.Width, Project.Window.Height);
        SetResizable(Project.Window.Resizable);
        ApplyDesktopSpriteWindowSettings();

        string iconPath = string.IsNullOrWhiteSpace(Project.Window.IconPath)
            ? Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png")
            : ResolveProjectPath(Project.Window.IconPath);
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
    }

    private void ApplyDesktopSpriteWindowSettings()
    {
        bool desktopSpriteMode = Project.Window.DesktopSpriteMode;
        SetFullscreen(desktopSpriteMode ? false : Project.Window.Fullscreen);
        SetTopMost(desktopSpriteMode);
        SetWindowBorderHidden(desktopSpriteMode);
        DesktopSpritePlatform.ApplyClickThrough(Window, desktopSpriteMode && Project.Window.DesktopSpriteClickThrough);
    }

    internal void ApplyRuntimeSettings()
    {
        Options.UseOpenCL = Project.Runtime.UseOpenCL;
        Zhengyan.DigitalWife.Mmd.Kernel.UseOpenCL = Project.Runtime.UseOpenCL;
        Zhengyan.DigitalWife.Mmd.Kernel.ResetOpenClProbe();
        bool openClRequested = Project.Runtime.UseOpenCL;
        bool openClActive = openClRequested && Zhengyan.DigitalWife.Mmd.Kernel.CanUseOpenClSafely();
        Console.WriteLine(openClRequested
            ? openClActive
                ? "[GamePlayer] PMX compute backend: OpenCL"
                : "[GamePlayer] OpenCL requested but unavailable; falling back to CPU"
            : "[GamePlayer] OpenCL disabled by project/runtime setting; using CPU");

        foreach (PlayerPmxObject pmxObject in _pmxObjects.ToArray())
        {
            pmxObject.Model.ReloadForCurrentOpenClSetting();
        }
    }

    internal bool IsUsingOpenClRuntime => string.Equals(CurrentComputeBackend, "OpenCL", StringComparison.Ordinal);

    internal string CurrentComputeBackend => _pmxObjects.Count != 0
        ? _pmxObjects[0].Model.ComputeBackend
        : Project.Runtime.UseOpenCL && Zhengyan.DigitalWife.Mmd.Kernel.CanUseOpenClSafely()
            ? "OpenCL"
            : "CPU";

    internal void SetConfiguredTitle(string title)
    {
        Project.Window.Title = title;
        Title = ResolveWindowTitle();
    }

    private string ResolveWindowTitle()
    {
        if (!string.IsNullOrWhiteSpace(Project.Window.Title))
        {
            return Project.Window.Title;
        }

        return string.IsNullOrWhiteSpace(Project.Name)
            ? "Zhengyan.DigitalWife Game Player"
            : Project.Name;
    }

    private void ClearRuntimeScene()
    {
        DisposeLoadingScripts();

        foreach ((_, List<IScriptInstance> scripts, _) in _scriptTargets)
        {
            foreach (IScriptInstance script in scripts)
            {
                script.Dispose();
            }
        }

        _scriptTargets.Clear();
        _hoveredSpriteId = string.Empty;
        _pressedSpriteId = string.Empty;
        _wasLeftMouseDown = false;
        _runtimeVoice?.ClearScene();
        _runtimeAsr?.ClearScene();
        _runtimeRealtimeVoice?.ClearScene();

        foreach (PlayerPmxObject item in _pmxObjects.ToArray())
        {
            RemoveComponent(item.Model);
        }

        _pmxObjects.Clear();

        foreach (RuntimeParticleObject item in _particleObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _particleObjects.Clear();

        foreach (RuntimeWaterObject item in _waterObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _waterObjects.Clear();

        foreach (RuntimePlaneObject item in _planeObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _planeObjects.Clear();
        _entitiesById.Clear();
        _entitiesByName.Clear();
        _waterRippleTimes.Clear();

        DisposeAudioRuntime();
    }

    private void LoadEntities()
    {
        foreach (GameEntity entity in Project.Scene.Entities)
        {
            LoadEntity(entity);
        }
    }

    private void LoadEntity(GameEntity entity)
    {
        if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
        {
            LoadParticleEntity(entity);
            return;
        }

        if (string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
        {
            LoadWaterEntity(entity);
            return;
        }

        if (string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
        {
            LoadPlaneEntity(entity);
            return;
        }

        if (IsEmptyEntity(entity))
        {
            RegisterRuntimeEntity(new RuntimeEntity(entity));
            return;
        }

        if (!string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unsupported entity type '{entity.Type}' for entity '{entity.Name}'.");
            return;
        }

        string fullPath = GameProjectPath.ToAbsolute(_projectDirectory, entity.AssetPath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"PMX file not found: {fullPath}");
            return;
        }

        try
        {
            PmxModelComponent model = AddComponent(new PmxModelComponent(fullPath)
            {
                Camera = _camera,
                RuntimeTextureProvider = _renderTextureManager,
                DrawOrder = 100,
                ShouldUpdatePoseEvaluator = ShouldUpdatePmxPose,
                OffscreenPoseUpdateIntervalSeconds = 0.12f
            });
            ApplyEntityToModel(entity, model);
            ApplyLightingToModel(model);

            RuntimeEntity runtimeEntity = new(entity, model, ResolveProjectPath);
            if (_runtimeVoice is not null)
            {
                runtimeEntity.AttachVoice(_runtimeVoice);
            }

            PlayerPmxObject runtimeObject = new()
            {
                Definition = entity,
                Model = model,
                RuntimeEntity = runtimeEntity
            };

            _pmxObjects.Add(runtimeObject);
            RegisterRuntimeEntity(runtimeEntity);
            Console.WriteLine($"[GamePlayer] Loaded PMX '{entity.Name}' with {model.ComputeBackend} backend");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load PMX entity '{entity.Name}': {ex.Message}");
        }
    }

    private bool ShouldUpdatePmxPose(PmxModelComponent model)
    {
        float radius = MathF.Max(Vector3.Distance(model.BoundsMin, model.BoundsMax) * 0.5f, 0.5f);
        Vector3 center = Vector3.Transform((model.BoundsMin + model.BoundsMax) * 0.5f, model.World);

        if (VisibilityCulling.IsBoundingSphereVisible(_camera, center, radius))
        {
            return true;
        }

        if (_renderTextureManager is not null)
        {
            foreach (OrbitCamera camera in _renderTextureManager.Cameras.Values)
            {
                if (VisibilityCulling.IsBoundingSphereVisible(camera, center, radius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void LoadParticleEntity(GameEntity entity)
    {
        try
        {
            ParticleSystemComponent component = AddComponent(new ParticleSystemComponent(
                _camera,
                ParticleEntitySettingsMapper.ToSettings(entity.Particle))
            {
                RuntimeTextureProvider = _renderTextureManager,
                DrawOrder = 130
            });
            ApplyEntityToParticle(entity, component, resetParticles: true);
            RuntimeEntity runtimeEntity = new(entity, component);
            _particleObjects.Add(new RuntimeParticleObject
            {
                Definition = entity,
                Component = component,
                Entity = runtimeEntity
            });
            RegisterRuntimeEntity(runtimeEntity);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load particle entity '{entity.Name}': {ex.Message}");
        }
    }

    private void LoadWaterEntity(GameEntity entity)
    {
        try
        {
            WaterSurfaceComponent component = AddComponent(new WaterSurfaceComponent(_camera, Math.Max(entity.Water.Size, 0.1f))
            {
                DrawOrder = 120
            });
            ApplyEntityToWater(entity, component);
            RuntimeEntity runtimeEntity = new(entity, component);
            _waterObjects.Add(new RuntimeWaterObject
            {
                Definition = entity,
                Component = component
            });
            RegisterRuntimeEntity(runtimeEntity);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load water entity '{entity.Name}': {ex.Message}");
        }
    }

    private void LoadPlaneEntity(GameEntity entity)
    {
        try
        {
            TexturedPlaneComponent component = AddComponent(new TexturedPlaneComponent(_camera, ResolvePlaneTexturePath(entity))
            {
                RuntimeTextureProvider = _renderTextureManager,
                DrawOrder = 115
            });
            ApplyEntityToPlane(entity, component);
            RuntimeEntity runtimeEntity = new(entity, component);
            _planeObjects.Add(new RuntimePlaneObject
            {
                Definition = entity,
                Component = component
            });
            RegisterRuntimeEntity(runtimeEntity);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load textured plane '{entity.Name}': {ex.Message}");
        }
    }

    private static bool IsEmptyEntity(GameEntity entity)
    {
        string normalized = (entity.Type ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "empty" or "empty_object" or "game_object";
    }

    private void LoadAudio()
    {
        if (Audio is null)
        {
            Console.Error.WriteLine(AudioStatusMessage ?? "Audio is unavailable.");
            return;
        }

        foreach (AudioAsset audioAsset in Project.Scene.Audio)
        {
            LoadAudioAsset(audioAsset);
        }
    }

    private void LoadAudioAsset(AudioAsset audioAsset)
    {
        if (Audio is null)
        {
            Console.Error.WriteLine(AudioStatusMessage ?? "Audio is unavailable.");
            return;
        }

        string fullPath = GameProjectPath.ToAbsolute(_projectDirectory, audioAsset.Path);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Audio file not found: {fullPath}");
            return;
        }

        AudioClip? clip = null;
        AudioSource? source = null;
        bool registered = false;

        try
        {
            clip = Audio.LoadClip(fullPath);
            source = Audio.CreateSource(clip);
            source.Volume = audioAsset.Volume;
            source.Looping = audioAsset.Loop;
            RegisterAudioRuntime(audioAsset, clip, source);
            registered = true;

            if (audioAsset.PlayOnStart)
            {
                source.Play();
            }
        }
        catch (Exception ex)
        {
            if (!registered)
            {
                source?.Dispose();
                clip?.Dispose();
            }

            Console.Error.WriteLine($"Failed to load audio '{audioAsset.Name}': {ex.Message}");
        }
    }

    private void RegisterAudioRuntime(AudioAsset audioAsset, AudioClip clip, AudioSource source)
    {
        string[] aliases = GetAudioAliases(audioAsset).ToArray();
        HashSet<AudioSource> replacedSources = [];
        HashSet<AudioClip> replacedClips = [];

        foreach (string alias in aliases)
        {
            if (_audioSources.TryGetValue(alias, out AudioSource? replacedSource) && !ReferenceEquals(replacedSource, source))
            {
                replacedSources.Add(replacedSource);
            }

            if (_audioClips.TryGetValue(alias, out AudioClip? replacedClip) && !ReferenceEquals(replacedClip, clip))
            {
                replacedClips.Add(replacedClip);
            }

            _audioSources[alias] = source;
            _audioClips[alias] = clip;
        }

        DisposeUnreferencedAudio(replacedSources, replacedClips);
    }

    private void DisposeAudioRuntime()
    {
        foreach (AudioSource source in _audioSources.Values.ToHashSet())
        {
            source.Dispose();
        }

        foreach (AudioClip clip in _audioClips.Values.ToHashSet())
        {
            clip.Dispose();
        }

        _audioSources.Clear();
        _audioClips.Clear();
    }

    private void DisposeUnreferencedAudio(IEnumerable<AudioSource> replacedSources, IEnumerable<AudioClip> replacedClips)
    {
        foreach (AudioSource source in replacedSources)
        {
            if (!_audioSources.Values.Any(item => ReferenceEquals(item, source)))
            {
                source.Dispose();
            }
        }

        foreach (AudioClip clip in replacedClips)
        {
            if (!_audioClips.Values.Any(item => ReferenceEquals(item, clip)))
            {
                clip.Dispose();
            }
        }
    }

    private static IEnumerable<string> GetAudioAliases(AudioAsset audioAsset)
    {
        if (!string.IsNullOrWhiteSpace(audioAsset.Name))
        {
            yield return audioAsset.Name;
        }

        if (!string.IsNullOrWhiteSpace(audioAsset.Path)
            && !string.Equals(audioAsset.Path, audioAsset.Name, StringComparison.OrdinalIgnoreCase))
        {
            yield return audioAsset.Path;
        }
    }

    private void StartScripts()
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach (GameEntity entity in Project.Scene.Entities)
        {
            RuntimeEntity? runtimeEntity = _entitiesById.TryGetValue(entity.Id, out RuntimeEntity? byId)
                ? byId
                : null;
            if (runtimeEntity is null)
            {
                continue;
            }

            List<IScriptInstance> scripts = [];
            _scriptTargets.Add((runtimeEntity, scripts, entity.Name));
            foreach (ScriptBinding binding in entity.Scripts.Where(script => script.Enabled))
            {
                string scriptPath = GameProjectPath.ToAbsolute(_projectDirectory, binding.Path);
                if (!File.Exists(scriptPath))
                {
                    Console.Error.WriteLine($"Script file not found: {scriptPath}");
                    continue;
                }

                try
                {
                    IScriptInstance script = _scriptHost.Load(binding.Language, scriptPath);
                    scripts.Add(script);
                    script.Start(runtimeEntity, _runtimeScene, _runtimeInput, _runtimeAudio);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to start script '{binding.Path}': {ex.Message}");
                }
            }
        }
    }

    private void AttachRuntimeSceneToEntities()
    {
        if (_runtimeScene is null)
        {
            return;
        }

        foreach (RuntimeEntity entity in _entitiesById.Values)
        {
            entity.AttachScene(_runtimeScene);
        }
    }

    private void DispatchGuiEvent(GuiControlSettings control, string eventName)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            Console.Error.WriteLine($"GUI event '{eventName}' ignored because runtime scene/input/audio is not ready.");
            return;
        }

        RuntimeEntity? target = ResolveGuiEventTarget(control, eventName);

        if (target is null)
        {
            Console.Error.WriteLine($"GUI event '{eventName}' from '{control.Name}' has no target entity and no script target fallback.");
            return;
        }

        bool dispatched = false;
        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.GuiEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, control.Id, control.Name, eventName);
                    dispatched = true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script GUI event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }

        if (!dispatched)
        {
            Console.Error.WriteLine($"GUI event '{eventName}' from '{control.Name}' found target '{target.Name}', but no script handled it.");
        }
    }

    private void UpdateSpritePointerEvents()
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        SpriteSettings? hoveredSprite = ResolveHoveredSprite();
        string hoveredSpriteId = hoveredSprite?.Id ?? string.Empty;

        if (!string.Equals(_hoveredSpriteId, hoveredSpriteId, StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetSpriteById(_hoveredSpriteId, out SpriteSettings? previousHovered))
            {
                DispatchSpriteEvent(previousHovered!, "exited");
            }

            if (hoveredSprite is not null)
            {
                DispatchSpriteEvent(hoveredSprite, "entered");
            }

            _hoveredSpriteId = hoveredSpriteId;
        }

        bool isLeftMouseDown = _runtimeInput.IsMouseButtonDown("left");
        if (!_wasLeftMouseDown && isLeftMouseDown && hoveredSprite is not null)
        {
            _pressedSpriteId = hoveredSprite.Id;
            DispatchSpriteEvent(hoveredSprite, "pressed");
        }
        else if (_wasLeftMouseDown && !isLeftMouseDown)
        {
            if (TryGetSpriteById(_pressedSpriteId, out SpriteSettings? pressedSprite))
            {
                DispatchSpriteEvent(pressedSprite!, "released");
                if (hoveredSprite is not null && string.Equals(hoveredSprite.Id, _pressedSpriteId, StringComparison.OrdinalIgnoreCase))
                {
                    DispatchSpriteEvent(hoveredSprite, "clicked");
                }
            }

            _pressedSpriteId = string.Empty;
        }

        _wasLeftMouseDown = isLeftMouseDown;
    }

    private SpriteSettings? ResolveHoveredSprite()
    {
        if (_runtimeInput is null)
        {
            return null;
        }

        int actualWidth = Math.Max(Window.Size.X, 1);
        int actualHeight = Math.Max(Window.Size.Y, 1);
        int referenceWidth = Math.Max(Project.Window.Width, 1);
        int referenceHeight = Math.Max(Project.Window.Height, 1);

        return Project.Scene.Sprites
            .Where(sprite =>
                sprite.Visible
                && !string.IsNullOrWhiteSpace(sprite.Path)
                && !string.IsNullOrWhiteSpace(sprite.TargetEntity)
                && SpriteLayoutResolver.ContainsPoint(
                    sprite,
                    _runtimeInput.MouseX,
                    _runtimeInput.MouseY,
                    actualWidth,
                    actualHeight,
                    referenceWidth,
                    referenceHeight))
            .OrderByDescending(sprite => sprite.DrawOrder)
            .ThenByDescending(sprite => Project.Scene.Sprites.IndexOf(sprite))
            .FirstOrDefault();
    }

    private bool TryGetSpriteById(string spriteId, out SpriteSettings? sprite)
    {
        sprite = null;
        if (string.IsNullOrWhiteSpace(spriteId))
        {
            return false;
        }

        sprite = Project.Scene.Sprites.FirstOrDefault(item =>
            string.Equals(item.Id, spriteId, StringComparison.OrdinalIgnoreCase));
        return sprite is not null;
    }

    private void DispatchSpriteEvent(SpriteSettings sprite, string eventName)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null || string.IsNullOrWhiteSpace(sprite.TargetEntity))
        {
            return;
        }

        RuntimeEntity? target = _runtimeScene.GetEntity(sprite.TargetEntity);
        if (target is null)
        {
            return;
        }

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.SpriteEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, sprite.Id, sprite.Name, eventName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script sprite event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
    }

    private RuntimeEntity? ResolveGuiEventTarget(GuiControlSettings control, string eventName)
    {
        if (_runtimeScene is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(control.TargetEntity))
        {
            RuntimeEntity? configuredTarget = _runtimeScene.GetEntity(control.TargetEntity);
            if (configuredTarget is not null)
            {
                return configuredTarget;
            }

            Console.Error.WriteLine($"GUI event '{eventName}' from '{control.Name}' has missing target entity '{control.TargetEntity}'. Falling back to a scripted PMX entity.");
        }

        RuntimeEntity? scriptedPmx = SelectBestScriptedPmxTarget();
        if (scriptedPmx is not null)
        {
            return scriptedPmx;
        }

        return _scriptTargets
            .Where(target => target.Scripts.Count > 0)
            .Select(target => target.Entity)
            .FirstOrDefault();
    }

    private RuntimeEntity? SelectBestScriptedPmxTarget()
    {
        List<RuntimeEntity> scriptedPmxTargets = _scriptTargets
            .Where(target => target.Scripts.Count > 0 && target.Entity.IsPmxModel)
            .Select(target => target.Entity)
            .ToList();
        if (scriptedPmxTargets.Count <= 1)
        {
            return scriptedPmxTargets.FirstOrDefault();
        }

        HashSet<string> relationTargets = _entitiesById.Values
            .Where(entity => entity.IsPmxModel && entity.RelationEnabled && !string.IsNullOrWhiteSpace(entity.RelationEntity))
            .Select(entity => ResolveRuntimeEntity(entity.RelationEntity))
            .Where(entity => entity is not null)
            .Select(entity => entity!.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scriptedPmxTargets
            .OrderByDescending(entity => relationTargets.Contains(entity.Id))
            .ThenBy(entity => entity.RelationEnabled)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void DispatchSpeechEvent(RuntimeEntity target, string callbackName)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.SpeechEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, callbackName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script speech event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
    }

    private void DispatchLlmEvent(RuntimeEntity target, RuntimeLlmScriptEvent llmEvent)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.LlmEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, llmEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script LLM event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
    }

    private void DispatchAsrEvent(RuntimeEntity target, RuntimeAsrScriptEvent asrEvent)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.AsrEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, asrEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script ASR event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
    }

    private void DispatchRealtimeVoiceEvent(RuntimeEntity target, RuntimeRealtimeVoiceScriptEvent realtimeVoiceEvent)
    {
        if (_runtimeScene is null || _runtimeInput is null || _runtimeAudio is null)
        {
            return;
        }

        foreach ((RuntimeEntity entity, List<IScriptInstance> scripts, _) in _scriptTargets.ToArray())
        {
            if (!ReferenceEquals(entity, target))
            {
                continue;
            }

            foreach (IScriptInstance script in scripts)
            {
                try
                {
                    script.RealtimeVoiceEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, realtimeVoiceEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script Realtime Voice event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
    }

    private void ApplyRelationBindings()
    {
        foreach (RuntimeEntity entity in _entitiesById.Values)
        {
            if (!entity.RelationEnabled)
            {
                continue;
            }

            string relationTarget = entity.RelationEntity;
            if (string.IsNullOrWhiteSpace(relationTarget))
            {
                List<RuntimeEntity> candidates = _entitiesById.Values
                    .Where(candidate => !ReferenceEquals(candidate, entity) && candidate.IsPmxModel)
                    .ToList();
                if (candidates.Count == 1)
                {
                    relationTarget = candidates[0].Id;
                }
            }

            if (!string.IsNullOrWhiteSpace(relationTarget))
            {
                _ = entity.TryBindRelation(relationTarget, entity.RelationBindComponentTransform, entity.RelationBindLighting);
            }
        }
    }

    private void ApplyEntityToModel(GameEntity entity, PmxModelComponent model)
    {
        model.Position = entity.Transform.Position.ToVector3();
        model.Scale = entity.Transform.Scale.ToVector3();
        model.Rotation = ToQuaternion(entity.Transform.RotationDegrees.ToVector3());
        model.IsPlaying = entity.IsPlaying;
        model.PlaybackSpeed = entity.PlaybackSpeed;
        model.LoopMotion = entity.LoopMotion;
        model.ResetPhysicsOnMotionLoop = entity.ResetPhysicsOnMotionLoop;
        model.EnableEdge = entity.EnableEdge;
        model.EnableShadow = entity.EnableShadow;
        model.DrawShadowInMainPass = entity.DrawShadowInMainPass;
        if (entity.MotionLayers.Count != 0)
        {
            model.SetMotionLayers(entity.MotionLayers
                .Where(layer => !string.IsNullOrWhiteSpace(layer.Path))
                .Select(layer => new MotionLayerDefinition(
                    GameProjectPath.ToAbsolute(_projectDirectory, layer.Path),
                    layer.Weight,
                    layer.ResetPhysicsOnLoop)));
        }
    }

    private static void ApplyEntityToParticle(GameEntity entity, ParticleSystemComponent component, bool resetParticles)
    {
        component.Position = entity.Transform.Position.ToVector3();
        component.Enabled = entity.IsPlaying;
        component.Visible = entity.IsPlaying;
        component.SimulationSpeed = Math.Clamp(entity.Particle.SimulationSpeed, 0.0f, 10.0f);
        component.Opacity = Math.Clamp(entity.Particle.Opacity, 0.0f, 1.0f);
        component.ApplySettings(ParticleEntitySettingsMapper.ToSettings(entity.Particle), resetParticles);
    }

    private static void ApplyEntityToWater(GameEntity entity, WaterSurfaceComponent component)
    {
        component.Position = entity.Transform.Position.ToVector3();
        component.Scale = entity.Transform.Scale.ToVector3();
        component.Rotation = ToQuaternion(entity.Transform.RotationDegrees.ToVector3());
        component.Enabled = entity.IsPlaying;
        component.Visible = entity.IsPlaying;
        component.Alpha = entity.Water.Alpha;
        component.AnimationSpeed = entity.Water.AnimationSpeed;
        component.NormalTiling = Math.Max(entity.Water.NormalTiling, 0.001f);
        component.DeepColor = entity.Water.DeepColor.ToVector3();
        component.ReflectionTint = entity.Water.ReflectionTint.ToVector3();
        component.SkyReflectionStrength = entity.Water.SkyReflectionStrength;
        component.RippleLifetimeSeconds = Math.Max(0.05f, entity.Water.RippleLifetimeSeconds);
        component.RippleWaveSpeed = entity.Water.RippleWaveSpeed;
        component.RippleFrequency = Math.Max(0.0f, entity.Water.RippleFrequency);
        component.RippleNormalStrength = Math.Max(0.0f, entity.Water.RippleNormalStrength);
    }

    private void ApplyEntityToPlane(GameEntity entity, TexturedPlaneComponent component)
    {
        component.Position = entity.Transform.Position.ToVector3();
        component.Rotation = ToQuaternion(entity.Transform.RotationDegrees.ToVector3());
        component.Scale = entity.Transform.Scale.ToVector3();
        component.Visible = entity.IsPlaying;
        component.TexturePath = ResolvePlaneTexturePath(entity);
        component.Width = Math.Max(entity.Plane.Width, 0.001f);
        component.Height = Math.Max(entity.Plane.Height, 0.001f);
        component.Billboard = entity.Plane.Billboard;
        component.Tint = entity.Plane.Tint.ToVector4();
        component.Opacity = entity.Plane.Opacity;
    }

    private string ResolvePlaneTexturePath(GameEntity entity)
    {
        string path = !string.IsNullOrWhiteSpace(entity.Plane.TexturePath)
            ? entity.Plane.TexturePath
            : entity.AssetPath;
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().StartsWith("rt:", StringComparison.OrdinalIgnoreCase)
                ? path.Trim()
                : ResolveProjectPath(path);
    }

    private void ApplySceneSettings()
    {
        LightingSettings lighting = Project.Scene.Lighting;
        Options.ClearColor = Project.Window.DesktopSpriteMode
            ? new Vector4(0.0f, 0.0f, 0.0f, 0.0f)
            : lighting.ClearColor.ToVector4();
        if (GraphicsDevice is not null)
        {
            GraphicsDevice.ClearColor = Options.ClearColor;
        }

        ApplySkyboxSettings();
    }

    private void ApplySkyboxSettings()
    {
        SkyboxSettings skybox = Project.Scene.Skybox;
        if (!skybox.Enabled || Project.Window.DesktopSpriteMode)
        {
            if (_skybox is not null)
            {
                RemoveComponent(_skybox);
                _skybox = null;
            }

            return;
        }

        string texturePath = ResolveProjectPath(skybox.TexturePath);
        if (_skybox is null)
        {
            _skybox = AddComponent(new SkyboxComponent(_camera, texturePath)
            {
                DrawOrder = -10000
            });
        }

        _skybox.TexturePath = texturePath;
        _skybox.Exposure = skybox.Exposure;
        _skybox.Tint = skybox.Tint.ToVector3();
        _skybox.Visible = true;
    }

    private void UpdateWaterInteractions(GameTime gameTime)
    {
        double now = gameTime.TotalSeconds;
        foreach (RuntimeWaterObject waterObject in _waterObjects)
        {
            GameEntity waterEntity = waterObject.Definition;
            if (!waterEntity.Water.EnableInteraction)
            {
                continue;
            }

            float waterY = waterEntity.Transform.Position.Y;
            float waterHalfSize = Math.Max(waterEntity.Water.Size, 0.1f) * MathF.Max(MathF.Abs(waterEntity.Transform.Scale.X), MathF.Abs(waterEntity.Transform.Scale.Z));
            foreach (RuntimeEntity entity in _entitiesById.Values)
            {
                if (string.Equals(entity.Id, waterEntity.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase)
                    && !entity.EnableWaterInteraction)
                {
                    continue;
                }

                if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeParticleObject? particleObject = _particleObjects.FirstOrDefault(item => string.Equals(item.Entity.Id, entity.Id, StringComparison.OrdinalIgnoreCase));
                    if (particleObject is not null)
                    {
                        ProcessParticleWaterInteractions(waterObject, particleObject, waterEntity, waterY, waterHalfSize, now);
                    }

                    continue;
                }

                foreach (RuntimeCollider collider in RuntimePhysics.CreateColliders(entity))
                {
                    Vector3 center = collider.Shape == "box" ? collider.Box.Center : collider.Capsule.Center;
                    float radius = collider.Shape == "box"
                        ? collider.Box.HalfExtents.Length()
                        : collider.Capsule.Radius + (Vector3.Distance(collider.Capsule.Start, collider.Capsule.End) * 0.5f);
                    if (MathF.Abs(center.Y - waterY) > radius)
                    {
                        continue;
                    }

                    Vector3 delta = center - waterEntity.Transform.Position.ToVector3();
                    if (MathF.Abs(delta.X) <= waterHalfSize && MathF.Abs(delta.Z) <= waterHalfSize)
                    {
                        string rippleKey = $"{waterEntity.Id}:{entity.Id}:{collider.Id}";
                        if (!_waterRippleTimes.TryGetValue(rippleKey, out double lastRippleTime) || now - lastRippleTime >= 0.35)
                        {
                            _waterRippleTimes[rippleKey] = now;
                            waterObject.Component.AddRipple(new Vector3(center.X, waterY, center.Z), waterEntity.Water.InteractionRadius, waterEntity.Water.InteractionStrength);
                        }
                    }
                }
            }
        }
    }

    private void ProcessParticleWaterInteractions(
        RuntimeWaterObject waterObject,
        RuntimeParticleObject particleObject,
        GameEntity waterEntity,
        float waterY,
        float waterHalfSize,
        double now)
    {
        foreach (ParticleCollisionSample sample in particleObject.Component.GetCollisionSamples())
        {
            float verticalDistance = sample.Position.Y - waterY;
            if (verticalDistance > sample.Radius)
            {
                continue;
            }

            Vector3 delta = sample.Position - waterEntity.Transform.Position.ToVector3();
            if (MathF.Abs(delta.X) > waterHalfSize || MathF.Abs(delta.Z) > waterHalfSize)
            {
                continue;
            }

            string rippleKey = BuildParticleRippleKey(waterEntity, particleObject.Definition, sample.Position, waterEntity.Water.ParticleRippleMergeDistance);
            double minInterval = Math.Max(0.0, waterEntity.Water.ParticleRippleMinIntervalSeconds);
            if (!_waterRippleTimes.TryGetValue(rippleKey, out double lastRippleTime) || now - lastRippleTime >= minInterval)
            {
                _waterRippleTimes[rippleKey] = now;
                waterObject.Component.AddRipple(new Vector3(sample.Position.X, waterY, sample.Position.Z), waterEntity.Water.InteractionRadius, waterEntity.Water.InteractionStrength);
            }

            if (particleObject.Definition.Particle.KillOnWaterContact)
            {
                particleObject.Component.KillParticle(sample.Index);
            }
        }
    }

    private static string BuildParticleRippleKey(GameEntity waterEntity, GameEntity particleEntity, Vector3 position, float mergeDistance)
    {
        if (mergeDistance <= 0.0001f)
        {
            return $"{waterEntity.Id}:{particleEntity.Id}:particle";
        }

        int cellX = (int)MathF.Floor(position.X / mergeDistance);
        int cellZ = (int)MathF.Floor(position.Z / mergeDistance);
        return $"{waterEntity.Id}:{particleEntity.Id}:particle:{cellX}:{cellZ}";
    }

    private void ApplyCameraSettings()
    {
        CameraSettings camera = Project.Scene.Camera;
        EnsureSceneCameras();
        SceneCameraSettings mainCamera = Project.Scene.Cameras.First(item => item.IsMain);
        camera = mainCamera.Camera;
        Project.Scene.Camera = camera;
        Project.Scene.MainCamera = mainCamera.Name;
        _camera.SetLookAt(camera.Position.ToVector3(), camera.Target.ToVector3());
        _cameraController?.EditorOrbit(0.2f, 1.0f, 1.0f);
        _camera.ProjectionMode = NormalizeProjectionMode(camera.ProjectionMode) == "orthographic"
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        _camera.Fov = camera.Fov;
        _camera.OrthographicSize = camera.OrthographicSize;
        _camera.NearClipPlane = camera.NearClipPlane;
        _camera.FarClipPlane = camera.FarClipPlane;
        _renderTextureManager?.SyncCameras(_camera);
    }

    private void EnsureSceneCameras()
    {
        if (Project.Scene.Cameras.Count == 0)
        {
            Project.Scene.Cameras.Add(new SceneCameraSettings
            {
                Name = string.IsNullOrWhiteSpace(Project.Scene.MainCamera) ? "Main Camera" : Project.Scene.MainCamera,
                IsMain = true,
                Camera = Project.Scene.Camera
            });
        }

        SceneCameraSettings? main = Project.Scene.Cameras.FirstOrDefault(camera => camera.IsMain)
            ?? Project.Scene.Cameras.FirstOrDefault(camera => string.Equals(camera.Name, Project.Scene.MainCamera, StringComparison.OrdinalIgnoreCase))
            ?? Project.Scene.Cameras[0];
        foreach (SceneCameraSettings camera in Project.Scene.Cameras)
        {
            camera.IsMain = ReferenceEquals(camera, main);
        }

        Project.Scene.MainCamera = main.Name;
        Project.Scene.Camera = main.Camera;
    }

    private void ApplyRuntimeCamera(OrbitCamera camera)
    {
        foreach (PlayerPmxObject item in _pmxObjects)
        {
            item.Model.Camera = camera;
        }

        foreach (RuntimeParticleObject item in _particleObjects)
        {
            item.Component.Camera = camera;
        }

        foreach (RuntimeWaterObject item in _waterObjects)
        {
            item.Component.Camera = camera;
        }

        foreach (RuntimePlaneObject item in _planeObjects)
        {
            item.Component.Camera = camera;
        }

        if (_skybox is not null)
        {
            _skybox.Camera = camera;
        }
    }

    private IReadOnlyList<DrawableGameComponent> GetRenderTextureExcludedComponents()
    {
        return GetOverlayComponents();
    }

    private IReadOnlyList<DrawableGameComponent> GetOverlayComponents()
    {
        List<DrawableGameComponent> excluded = [];
        if (_guiOverlay is not null)
        {
            excluded.Add(_guiOverlay);
        }

        if (_loadingScreen is not null)
        {
            excluded.Add(_loadingScreen);
        }

        if (_debugDraw is not null)
        {
            excluded.Add(_debugDraw);
        }

        return excluded;
    }

    public override bool ShouldDrawComponent(DrawableGameComponent component)
    {
        if (!_renderedSceneThisFrame)
        {
            return true;
        }

        return GetOverlayComponents().Contains(component);
    }

    private static string NormalizeProjectionMode(string projectionMode)
    {
        string normalized = (projectionMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "orthographic" or "ortho"
            ? "orthographic"
            : "perspective";
    }

    private RuntimeEntity? ResolveRuntimeEntity(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        if (_runtimeScene is not null)
        {
            return _runtimeScene.GetEntity(idOrName);
        }

        return _entitiesById.TryGetValue(idOrName, out RuntimeEntity? byId)
            ? byId
            : _entitiesByName.TryGetValue(idOrName, out RuntimeEntity? byName)
                ? byName
                : null;
    }

    private void ApplyLightingToModel(PmxModelComponent model)
    {
        LightingSettings lighting = Project.Scene.Lighting;
        model.LightColor = lighting.LightColor.ToVector3();
        model.LightDirection = lighting.LightDirection.ToVector3();
        model.AmbientLightColor = lighting.AmbientColor.ToVector3();
        model.AmbientLightStrength = lighting.AmbientStrength;
        model.ShadowColor = lighting.ShadowColor.ToVector4();
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private void RegisterRuntimeEntity(RuntimeEntity runtimeEntity)
    {
        _entitiesById[runtimeEntity.Id] = runtimeEntity;
        if (!string.IsNullOrWhiteSpace(runtimeEntity.Name))
        {
            _entitiesByName[runtimeEntity.Name] = runtimeEntity;
        }
    }

    private string ResolveProjectPath(string path)
    {
        return GameProjectPath.ToAbsolute(_projectDirectory, path);
    }
}
