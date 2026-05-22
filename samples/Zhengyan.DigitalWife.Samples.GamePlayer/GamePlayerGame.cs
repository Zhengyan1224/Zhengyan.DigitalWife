using System.Numerics;
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
    private readonly List<(RuntimeEntity Entity, List<IScriptInstance> Scripts, string Name)> _scriptTargets = [];
    private readonly List<IScriptInstance> _loadingScripts = [];
    private readonly Queue<LoadingStep> _loadingSteps = [];
    private readonly MainThreadDispatcher _dispatcher = new();
    private readonly Dictionary<string, RuntimeEntity> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEntity> _entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _audioClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioSource> _audioSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptHost _scriptHost;
    private LoadingScreenComponent? _loadingScreen;
    private RuntimeGuiOverlayComponent? _guiOverlay;
    private RuntimeCameraControllerComponent? _cameraController;
    private RuntimeScene? _runtimeScene;
    private RuntimeInput? _runtimeInput;
    private RuntimeAudio? _runtimeAudio;
    private RuntimeVoice? _runtimeVoice;
    private RuntimeEntity? _loadingEntity;
    private string? _pendingScenePath;
    private string _loadingMessage = "Loading...";
    private int _loadingTotalSteps;
    private int _loadingCompletedSteps;
    private int _loadingDelayFrames;
    private bool _loadingReadyToFinish;
    private bool _isLoading;

    public GamePlayerGame(string projectDirectory)
        : base(new GameOptions
        {
            Title = "Zhengyan.DigitalWife Game Player",
            WindowSize = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            VSync = true,
            Samples = 4,
            UseOpenCL = true,
            EnableAudio = true,
            ClearColor = new Vector4(0.08f, 0.09f, 0.12f, 1.0f),
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        })
    {
        _projectDirectory = projectDirectory;
        _scriptHost = new ScriptHost(projectDirectory);
    }

    public GameProject Project { get; private set; } = new();

    public OrbitCamera Camera => _camera;

    public IReadOnlyList<PlayerPmxObject> PmxObjects => _pmxObjects;

    public IReadOnlyList<RuntimeParticleObject> ParticleObjects => _particleObjects;

    public IReadOnlyList<RuntimeWaterObject> WaterObjects => _waterObjects;

    private float LoadingProgress => _loadingTotalSteps <= 0
        ? 0.0f
        : Math.Clamp(_loadingCompletedSteps / (float)_loadingTotalSteps, 0.0f, 1.0f);

    protected override void Initialize()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png");
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
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

        _ = AddComponent(new GroundShadowPassComponent(this)
        {
            DrawOrder = 110
        });

        _guiOverlay = AddComponent(new RuntimeGuiOverlayComponent(
            () => Project.Scene.GuiControls,
            () => Project.Scene.Sprites,
            ResolveProjectPath,
            DispatchGuiEvent)
        {
            DrawOrder = int.MaxValue - 10,
            UpdateOrder = int.MaxValue
        });

        BeginProjectLoad();
    }

    protected override void Update(GameTime gameTime)
    {
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

    protected override void UnloadContent()
    {
        ClearRuntimeScene();
        _runtimeVoice?.Dispose();
        _runtimeVoice = null;
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
            ApplyWindowSettings();
            _runtimeVoice?.Dispose();
            _runtimeVoice = new RuntimeVoice(this, _dispatcher, _projectDirectory, Project.Voice);
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

            _runtimeAudio = new RuntimeAudio(_audioSources);
            RuntimeCameraControllerComponent cameraController = _cameraController
                ?? throw new InvalidOperationException("Runtime camera controller is not initialized.");
            _runtimeScene = new RuntimeScene(
                Project.Scene,
                _entitiesById,
                _entitiesByName,
                new RuntimeWindowControl(this, Project.Window),
                new RuntimeCamera(_camera, cameraController, () => _entitiesById.Values),
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

    private void ApplyWindowSettings()
    {
        Title = ResolveWindowTitle();
        Project.Window.TimingMode = RuntimeWindowControl.NormalizeTimingMode(Project.Window.TimingMode);
        AnimationTimingMode = RuntimeWindowControl.ToAnimationTimingMode(Project.Window.TimingMode);
        SetWindowSize(Project.Window.Width, Project.Window.Height);
        SetResizable(Project.Window.Resizable);
        SetFullscreen(Project.Window.Fullscreen);

        string iconPath = string.IsNullOrWhiteSpace(Project.Window.IconPath)
            ? Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png")
            : ResolveProjectPath(Project.Window.IconPath);
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
    }

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
        _runtimeVoice?.ClearScene();

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
        _entitiesById.Clear();
        _entitiesByName.Clear();

        foreach (AudioSource source in _audioSources.Values)
        {
            source.Dispose();
        }

        foreach (AudioClip clip in _audioClips.Values)
        {
            clip.Dispose();
        }

        _audioSources.Clear();
        _audioClips.Clear();
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
                DrawOrder = 100
            });
            ApplyEntityToModel(entity, model);
            ApplyLightingToModel(model);

            RuntimeEntity runtimeEntity = new(entity, model, ResolveProjectPath);
            _runtimeVoice?.AttachEntity(runtimeEntity);
            PlayerPmxObject runtimeObject = new()
            {
                Definition = entity,
                Model = model,
                RuntimeEntity = runtimeEntity
            };

            _pmxObjects.Add(runtimeObject);
            RegisterRuntimeEntity(runtimeEntity);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load PMX entity '{entity.Name}': {ex.Message}");
        }
    }

    private void LoadParticleEntity(GameEntity entity)
    {
        try
        {
            ParticleSystemComponent component = AddComponent(new ParticleSystemComponent(
                _camera,
                ParticleEntitySettingsMapper.ToSettings(entity.Particle))
            {
                DrawOrder = 130
            });
            ApplyEntityToParticle(entity, component, resetParticles: true);
            RuntimeEntity runtimeEntity = new(entity, component);
            _particleObjects.Add(new RuntimeParticleObject
            {
                Definition = entity,
                Component = component
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

        try
        {
            AudioClip clip = Audio.LoadClip(fullPath);
            AudioSource source = Audio.CreateSource(clip);
            source.Volume = audioAsset.Volume;
            source.Looping = audioAsset.Loop;
            _audioClips[audioAsset.Name] = clip;
            _audioSources[audioAsset.Name] = source;
            if (!string.Equals(audioAsset.Name, audioAsset.Path, StringComparison.OrdinalIgnoreCase))
            {
                _audioSources[audioAsset.Path] = source;
            }

            if (audioAsset.PlayOnStart)
            {
                source.Play();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load audio '{audioAsset.Name}': {ex.Message}");
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
            return;
        }

        RuntimeEntity? target = !string.IsNullOrWhiteSpace(control.TargetEntity)
            ? _runtimeScene.GetEntity(control.TargetEntity)
            : null;
        if (target is null)
        {
            target = _scriptTargets.FirstOrDefault().Entity;
        }

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
                    script.GuiEvent(entity, _runtimeScene, _runtimeInput, _runtimeAudio, control.Id, eventName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Script GUI event failed for entity '{entity.Name}': {ex.Message}");
                }
            }
        }
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

    private void ApplyRelationBindings()
    {
        foreach (RuntimeEntity entity in _entitiesById.Values)
        {
            if (entity.RelationEnabled && !string.IsNullOrWhiteSpace(entity.RelationEntity))
            {
                entity.BindRelation(entity.RelationEntity, entity.RelationBindComponentTransform, entity.RelationBindLighting);
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
    }

    private void ApplySceneSettings()
    {
        LightingSettings lighting = Project.Scene.Lighting;
        Options.ClearColor = lighting.ClearColor.ToVector4();
        if (GraphicsDevice is not null)
        {
            GraphicsDevice.ClearColor = Options.ClearColor;
        }
    }

    private void ApplyCameraSettings()
    {
        CameraSettings camera = Project.Scene.Camera;
        _camera.SetLookAt(camera.Position.ToVector3(), camera.Target.ToVector3());
        _cameraController?.EditorOrbit(0.2f, 1.0f, 1.0f);
        _camera.ProjectionMode = NormalizeProjectionMode(camera.ProjectionMode) == "orthographic"
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        _camera.Fov = camera.Fov;
        _camera.OrthographicSize = camera.OrthographicSize;
        _camera.NearClipPlane = camera.NearClipPlane;
        _camera.FarClipPlane = camera.FarClipPlane;
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
