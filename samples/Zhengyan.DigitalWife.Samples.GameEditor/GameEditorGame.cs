using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed class GameEditorGame : Zhengyan.DigitalWife.Mmd.Game.Game
{
    private readonly OrbitCamera _camera = new();
    private readonly List<EditorPmxObject> _pmxObjects = [];
    private readonly List<EditorParticleObject> _particleObjects = [];
    private readonly List<EditorWaterObject> _waterObjects = [];
    private readonly Dictionary<string, AudioClip> _audioClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioSource> _audioSources = new(StringComparer.OrdinalIgnoreCase);

    private SceneRenderTarget? _sceneRenderTarget;
    private OrbitCameraController? _cameraController;
    private GameEditorOverlayComponent? _overlay;
    private string _statusMessage = "Ready.";

    public GameEditorGame()
        : base(new GameOptions
        {
            Title = "Zhengyan.DigitalWife Game Editor",
            WindowSize = new Silk.NET.Maths.Vector2D<int>(1440, 860),
            VSync = true,
            Samples = 4,
            UseOpenCL = true,
            EnableAudio = true,
            ClearColor = new Vector4(0.08f, 0.09f, 0.12f, 1.0f),
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        })
    {
        ProjectDirectory = GameProjectStore.CreateDefaultProjectDirectory();
        Project = CreateDefaultProject();
    }

    public GameProject Project { get; private set; }

    public string ProjectDirectory { get; private set; }

    public OrbitCamera Camera => _camera;

    public IReadOnlyList<EditorPmxObject> PmxObjects => _pmxObjects;

    public IReadOnlyList<EditorParticleObject> ParticleObjects => _particleObjects;

    public IReadOnlyList<EditorWaterObject> WaterObjects => _waterObjects;

    public string StatusMessage => _statusMessage;

    public int SelectedEntityIndex { get; set; } = -1;

    public SceneRenderTarget SceneRenderTarget => _sceneRenderTarget ?? throw new InvalidOperationException("Scene render target has not been created.");

    public string AudioSummary => Audio is null
        ? AudioStatusMessage ?? "Audio disabled."
        : $"{_audioSources.Count} source(s), {AudioStatusMessage ?? "audio enabled"}";

    protected override void Initialize()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png");
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
    }

    protected override void LoadContent()
    {
        _sceneRenderTarget = new SceneRenderTarget(GraphicsDevice.Gl);
        _sceneRenderTarget.EnsureSize(GraphicsDevice.BackBufferSize.X, GraphicsDevice.BackBufferSize.Y);

        ApplyCameraSettings();
        ApplySceneSettings();

        _cameraController = AddComponent(new OrbitCameraController(_camera)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f
        });

        _ = AddComponent(new GroundShadowPassComponent(this)
        {
            DrawOrder = 110
        });

        _ = AddComponent(new EditorDebugAxesComponent(_camera)
        {
            DrawOrder = 900
        });

        _overlay = AddComponent(new GameEditorOverlayComponent(this)
        {
            DrawOrder = int.MaxValue,
            UpdateOrder = int.MaxValue
        });

        _cameraController.CanProcessPointerInput = () => _overlay?.CanInteractWithScenePointer ?? true;
        _cameraController.CanProcessKeyboardInput = () => _overlay?.CanInteractWithSceneKeyboard ?? true;

        UpdateStatus($"Project ready: {ProjectDirectory}");
    }

    protected override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (_sceneRenderTarget is null)
        {
            return;
        }

        _sceneRenderTarget.Bind();
        GraphicsDevice.Gl.Disable(GLEnum.ScissorTest);
        GraphicsDevice.Gl.Disable(GLEnum.StencilTest);
        GraphicsDevice.Gl.ColorMask(true, true, true, true);
        GraphicsDevice.Gl.DepthMask(true);
        GraphicsDevice.Gl.StencilMask(0xFF);
        GraphicsDevice.Gl.ClearColor(Options.ClearColor.X, Options.ClearColor.Y, Options.ClearColor.Z, Options.ClearColor.W);
        GraphicsDevice.Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        _camera.Width = _sceneRenderTarget.Width;
        _camera.Height = _sceneRenderTarget.Height;
    }

    protected override void UnloadContent()
    {
        ClearSceneRuntime();
        _sceneRenderTarget?.Dispose();
        _sceneRenderTarget = null;
    }

    public void PresentSceneToBackBuffer()
    {
        if (_sceneRenderTarget is null)
        {
            return;
        }

        _sceneRenderTarget.ForceOpaqueAlpha();
        _sceneRenderTarget.Unbind(GraphicsDevice.BackBufferSize.X, GraphicsDevice.BackBufferSize.Y);
    }

    public void SetSceneViewportSize(int width, int height)
    {
        _sceneRenderTarget?.EnsureSize(width, height);
    }

    public void SetProjectDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        ProjectDirectory = Path.GetFullPath(directory.Trim().Trim('"'));
        UpdateStatus($"Project directory set: {ProjectDirectory}");
    }

    public void NewProject(string projectName)
    {
        Project = CreateDefaultProject();
        Project.Name = string.IsNullOrWhiteSpace(projectName) ? "Untitled Game" : projectName.Trim();
        Project.Window.Title = Project.Name;
        SelectedEntityIndex = -1;
        ClearSceneRuntime();
        ApplyCameraSettings();
        ApplySceneSettings();
        SaveProject();
        UpdateStatus($"Created project '{Project.Name}'.");
    }

    public void LoadProject()
    {
        ClearSceneRuntime();
        Project = GameProjectStore.Load(ProjectDirectory);
        ApplyCameraSettings();
        ApplySceneSettings();

        foreach (GameEntity entity in Project.Scene.Entities)
        {
            TryLoadEntityRuntime(entity);
        }

        foreach (AudioAsset audioAsset in Project.Scene.Audio)
        {
            TryLoadAudioRuntime(audioAsset);
        }

        ApplyAllRelationsToRuntime();
        SelectedEntityIndex = Project.Scene.Entities.Count > 0 ? 0 : -1;
        UpdateStatus($"Loaded project: {Path.Combine(ProjectDirectory, GameProjectStore.ProjectFileName)}");
    }

    public void SaveProject()
    {
        Directory.CreateDirectory(ProjectDirectory);
        GameProjectStore.Save(ProjectDirectory, Project);
        EnsureDefaultScripts();
        UpdateStatus($"Saved project: {Path.Combine(ProjectDirectory, GameProjectStore.ProjectFileName)}");
    }

    public void AddPmxEntityFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = NormalizeInputPath(sourcePath);
        string assetPath = copyIntoProject
            ? CopyPmxAssetDirectoryIntoProject(normalizedSourcePath)
            : GameProjectPath.ToProjectRelative(ProjectDirectory, normalizedSourcePath);

        GameEntity entity = new()
        {
            Name = Path.GetFileNameWithoutExtension(assetPath),
            Type = "pmx_model",
            AssetPath = assetPath,
            Transform = new TransformSettings
            {
                Position = Vector3Dto.Zero,
                RotationDegrees = Vector3Dto.Zero,
                Scale = new Vector3Dto(0.2f, 0.2f, 0.2f)
            },
            Scripts =
            [
                new ScriptBinding
                {
                    Language = Project.ScriptRuntime.PreferredLanguage,
                    Path = Project.ScriptRuntime.PreferredLanguage == "python"
                        ? $"scripts/{SafeFileStem(Path.GetFileNameWithoutExtension(assetPath))}.py"
                        : $"scripts/{SafeFileStem(Path.GetFileNameWithoutExtension(assetPath))}.csx"
                }
            ]
        };

        Project.Scene.Entities.Add(entity);
        SelectedEntityIndex = Project.Scene.Entities.Count - 1;
        bool runtimeLoaded = TryLoadEntityRuntime(entity);
        if (runtimeLoaded)
        {
            ApplyAllRelationsToRuntime();
            EnsureCleanEntityScriptTemplate(entity.Scripts[0]);
            UpdateStatus($"Added PMX entity: {assetPath}");
            return;
        }

        Project.Scene.Entities.Remove(entity);
        SelectedEntityIndex = Math.Min(Project.Scene.Entities.Count - 1, SelectedEntityIndex);
    }

    public void AddAudioFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = NormalizeInputPath(sourcePath);
        string assetPath = copyIntoProject
            ? GameProjectPath.CopyAssetIntoProject(ProjectDirectory, normalizedSourcePath, "audio")
            : GameProjectPath.ToProjectRelative(ProjectDirectory, normalizedSourcePath);

        AudioAsset audioAsset = new()
        {
            Name = Path.GetFileNameWithoutExtension(assetPath),
            Path = assetPath,
            Loop = true,
            Volume = 0.8f,
            PlayOnStart = false
        };

        Project.Scene.Audio.Add(audioAsset);
        TryLoadAudioRuntime(audioAsset);
        UpdateStatus($"Added audio asset: {assetPath}");
    }

    public void AddMotionFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = NormalizeInputPath(sourcePath);
        string assetPath = copyIntoProject
            ? GameProjectPath.CopyAssetIntoProject(ProjectDirectory, normalizedSourcePath, "motions")
            : GameProjectPath.ToProjectRelative(ProjectDirectory, normalizedSourcePath);

        MotionAsset motionAsset = new()
        {
            Name = Path.GetFileNameWithoutExtension(assetPath),
            Path = assetPath
        };

        Project.Scene.Motions.Add(motionAsset);
        UpdateStatus($"Added motion asset: {assetPath}");
    }

    public void AddSpriteFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = NormalizeInputPath(sourcePath);
        string assetPath = copyIntoProject
            ? GameProjectPath.CopyAssetIntoProject(ProjectDirectory, normalizedSourcePath, "sprites")
            : GameProjectPath.ToProjectRelative(ProjectDirectory, normalizedSourcePath);

        Project.Scene.Sprites.Add(new SpriteSettings
        {
            Name = Path.GetFileNameWithoutExtension(assetPath),
            Path = assetPath,
            X = 24.0f,
            Y = 24.0f,
            Width = 128.0f,
            Height = 128.0f
        });
        UpdateStatus($"Added sprite: {assetPath}");
    }

    public void AddParticleEntity(string preset)
    {
        ParticleEntitySettings particle = ParticleEntitySettingsMapper.FromPreset(preset);
        GameEntity entity = new()
        {
            Name = $"{particle.Preset} particles",
            Type = "particle_system",
            Transform = new TransformSettings
            {
                Position = new Vector3Dto(0.0f, 4.0f, 0.0f),
                RotationDegrees = Vector3Dto.Zero,
                Scale = Vector3Dto.One
            },
            Particle = particle,
            Scripts =
            [
                new ScriptBinding
                {
                    Language = Project.ScriptRuntime.PreferredLanguage,
                    Path = Project.ScriptRuntime.PreferredLanguage == "python"
                        ? $"scripts/{SafeFileStem(particle.Preset)}_particles.py"
                        : $"scripts/{SafeFileStem(particle.Preset)}_particles.csx"
                }
            ]
        };

        Project.Scene.Entities.Add(entity);
        SelectedEntityIndex = Project.Scene.Entities.Count - 1;
        bool runtimeLoaded = TryLoadEntityRuntime(entity);
        EnsureCleanEntityScriptTemplate(entity.Scripts[0]);
        if (runtimeLoaded)
        {
            UpdateStatus($"Added particle entity: {particle.Preset}");
        }
    }

    public void AddWaterSurfaceEntity()
    {
        GameEntity entity = new()
        {
            Name = "Water Surface",
            Type = "water_surface",
            Transform = new TransformSettings
            {
                Position = Vector3Dto.Zero,
                RotationDegrees = Vector3Dto.Zero,
                Scale = Vector3Dto.One
            },
            Water = new WaterSurfaceSettings
            {
                Size = 20.0f,
                Alpha = 0.55f,
                AnimationSpeed = 0.03f,
                NormalTiling = 40.0f
            },
            Scripts =
            [
                new ScriptBinding
                {
                    Language = Project.ScriptRuntime.PreferredLanguage,
                    Path = Project.ScriptRuntime.PreferredLanguage == "python"
                        ? "scripts/water_surface.py"
                        : "scripts/water_surface.csx"
                }
            ]
        };

        Project.Scene.Entities.Add(entity);
        SelectedEntityIndex = Project.Scene.Entities.Count - 1;
        bool runtimeLoaded = TryLoadEntityRuntime(entity);
        EnsureCleanEntityScriptTemplate(entity.Scripts[0]);
        if (runtimeLoaded)
        {
            UpdateStatus("Added water surface entity.");
        }
    }

    public void AddScriptToSelected(string language)
    {
        GameEntity? entity = SelectedEntity;
        if (entity is null)
        {
            UpdateStatus("Select an entity before adding a script.");
            return;
        }

        string extension = language == "python" ? ".py" : ".csx";
        ScriptBinding binding = new()
        {
            Language = language,
            Path = $"scripts/{SafeFileStem(entity.Name)}_{entity.Scripts.Count + 1}{extension}",
            Enabled = true
        };

        entity.Scripts.Add(binding);
        EnsureCleanEntityScriptTemplate(binding);
        UpdateStatus($"Added {language} script: {binding.Path}");
    }

    public void AddSceneLoadingScript(string language)
    {
        string normalizedLanguage = string.Equals(language, "python", StringComparison.OrdinalIgnoreCase)
            ? "python"
            : "csharp";
        string extension = normalizedLanguage == "python" ? ".py" : ".csx";
        ScriptBinding binding = new()
        {
            Language = normalizedLanguage,
            Path = $"scripts/scene_loading_{Project.Scene.LoadingScripts.Count + 1}{extension}",
            Enabled = true
        };

        Project.Scene.LoadingScripts.Add(binding);
        EnsureLoadingScriptTemplate(binding);
        UpdateStatus($"Added scene loading {normalizedLanguage} script: {binding.Path}");
    }

    public GameEntity? SelectedEntity => SelectedEntityIndex >= 0 && SelectedEntityIndex < Project.Scene.Entities.Count
        ? Project.Scene.Entities[SelectedEntityIndex]
        : null;

    public void RemoveSelectedEntity()
    {
        if (SelectedEntityIndex < 0 || SelectedEntityIndex >= Project.Scene.Entities.Count)
        {
            return;
        }

        GameEntity entity = Project.Scene.Entities[SelectedEntityIndex];
        Project.Scene.Entities.RemoveAt(SelectedEntityIndex);

        EditorPmxObject? runtime = _pmxObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is not null)
        {
            RemoveComponent(runtime.Model);
            _pmxObjects.Remove(runtime);
        }

        EditorParticleObject? particleRuntime = _particleObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (particleRuntime is not null)
        {
            RemoveComponent(particleRuntime.Component);
            _particleObjects.Remove(particleRuntime);
        }

        EditorWaterObject? waterRuntime = _waterObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (waterRuntime is not null)
        {
            RemoveComponent(waterRuntime.Component);
            _waterObjects.Remove(waterRuntime);
        }

        SelectedEntityIndex = Math.Min(SelectedEntityIndex, Project.Scene.Entities.Count - 1);
        UpdateStatus($"Removed entity: {entity.Name}");
    }

    public void ApplySelectedEntityToRuntime()
    {
        GameEntity? entity = SelectedEntity;
        if (entity is null)
        {
            return;
        }

        if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
        {
            ApplySelectedParticleToRuntime();
            return;
        }

        if (string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
        {
            ApplySelectedWaterToRuntime();
            return;
        }

        EditorPmxObject? runtime = _pmxObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is null)
        {
            TryLoadEntityRuntime(entity);
            return;
        }

        ApplyEntityToModel(entity, runtime.Model);
        ApplyRelationToModel(entity, runtime);
    }

    public void ApplySceneSettings()
    {
        LightingSettings lighting = Project.Scene.Lighting;
        Options.ClearColor = lighting.ClearColor.ToVector4();
        if (GraphicsDevice is not null)
        {
            GraphicsDevice.ClearColor = Options.ClearColor;
        }

        foreach (EditorPmxObject item in _pmxObjects)
        {
            ApplyLightingToModel(item.Model);
        }
    }

    public void ApplyWindowSettings()
    {
        Title = ResolveWindowTitle();
        Project.Window.TimingMode = NormalizeTimingMode(Project.Window.TimingMode);
        AnimationTimingMode = ToAnimationTimingMode(Project.Window.TimingMode);
        SetWindowSize(Project.Window.Width, Project.Window.Height);
        SetResizable(Project.Window.Resizable);
        SetFullscreen(Project.Window.Fullscreen);

        string iconPath = string.IsNullOrWhiteSpace(Project.Window.IconPath)
            ? Path.Combine(AppContext.BaseDirectory, "Resources", "Logo", "logo.png")
            : GameProjectPath.ToAbsolute(ProjectDirectory, Project.Window.IconPath);
        WindowIconLoader.TrySetWindowIconFromFile(Window, iconPath);
    }

    private string ResolveWindowTitle()
    {
        if (!string.IsNullOrWhiteSpace(Project.Window.Title))
        {
            return Project.Window.Title;
        }

        return string.IsNullOrWhiteSpace(Project.Name) ? "Demo Game" : Project.Name;
    }

    private static AnimationTimingMode ToAnimationTimingMode(string timingMode)
    {
        return NormalizeTimingMode(timingMode) == "frame_rate_dependent"
            ? AnimationTimingMode.FrameRateDependent
            : AnimationTimingMode.TimeSynchronized;
    }

    private static string NormalizeTimingMode(string timingMode)
    {
        string normalized = (timingMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "frame_rate_dependent" or "framerate_dependent" or "frame"
            ? "frame_rate_dependent"
            : "time_synchronized";
    }

    public void ApplyCameraSettings()
    {
        CameraSettings camera = Project.Scene.Camera;
        _camera.SetLookAt(camera.Position.ToVector3(), camera.Target.ToVector3());
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

    public void PlayOrPauseAudio(AudioAsset audioAsset)
    {
        if (!_audioSources.TryGetValue(audioAsset.Path, out AudioSource? source))
        {
            TryLoadAudioRuntime(audioAsset);
            if (!_audioSources.TryGetValue(audioAsset.Path, out source))
            {
                return;
            }
        }

        source.Volume = audioAsset.Volume;
        source.Looping = audioAsset.Loop;
        if (source.State == Silk.NET.OpenAL.SourceState.Playing)
        {
            source.Pause();
        }
        else
        {
            source.Play();
        }
    }

    public void UpdateStatus(string message)
    {
        _statusMessage = message;
    }

    public bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        string? lastError = null;

        try
        {
            string imguiClipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(imguiClipboard))
            {
                text = NormalizeClipboardText(imguiClipboard);
                return true;
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from ImGui backend: {ex.Message}";
        }

        try
        {
            if (Input.Context.Keyboards.Count > 0)
            {
                string keyboardClipboard = Input.Context.Keyboards[0].ClipboardText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(keyboardClipboard))
                {
                    text = NormalizeClipboardText(keyboardClipboard);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from input backend: {ex.Message}";
        }

        try
        {
            object windowObject = Window;
            Type windowType = windowObject.GetType();

            string[] propertyNames = ["ClipboardText", "ClipboardString", "Clipboard"];
            foreach (string propertyName in propertyNames)
            {
                PropertyInfo? property = windowType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property?.CanRead == true && property.PropertyType == typeof(string))
                {
                    string clipboard = (string?)property.GetValue(windowObject) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(clipboard))
                    {
                        text = NormalizeClipboardText(clipboard);
                        return true;
                    }
                }
            }

            MethodInfo? method = windowType.GetMethod("GetClipboardText", BindingFlags.Public | BindingFlags.Instance);
            if (method is not null && method.ReturnType == typeof(string) && method.GetParameters().Length == 0)
            {
                string clipboard = (string?)method.Invoke(windowObject, null) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(clipboard))
                {
                    text = NormalizeClipboardText(clipboard);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            lastError = $"Failed to read clipboard from window backend: {ex.Message}";
        }

        UpdateStatus(lastError ?? "Clipboard is empty or unavailable on this runtime.");
        return false;
    }

    private bool TryLoadEntityRuntime(GameEntity entity)
    {
        if (!string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadParticleRuntime(entity);
            }

            if (string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadWaterRuntime(entity);
            }

            UpdateStatus($"Unsupported entity type: {entity.Type}");
            return false;
        }

        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, entity.AssetPath);
        if (!File.Exists(fullPath))
        {
            UpdateStatus($"PMX file not found: {fullPath}");
            return false;
        }

        PmxModelComponent? model = null;
        try
        {
            model = new PmxModelComponent(fullPath)
            {
                Camera = _camera,
                DrawOrder = 100
            };
            _ = AddComponent(model);
            ApplyEntityToModel(entity, model);
            ApplyLightingToModel(model);
            EditorPmxObject runtime = new()
            {
                Entity = entity,
                Model = model
            };
            _pmxObjects.Add(runtime);
            ApplyRelationToModel(entity, runtime);
            return true;
        }
        catch (Exception ex)
        {
            if (model is not null)
            {
                _ = RemoveComponent(model);
            }

            UpdateStatus($"Failed to load PMX: {ex.Message}");
            return false;
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
        model.SetMotionLayers(entity.MotionLayers
            .Where(layer => !string.IsNullOrWhiteSpace(layer.Path))
            .Select(layer => new MotionLayerDefinition(
                GameProjectPath.ToAbsolute(ProjectDirectory, layer.Path),
                layer.Weight,
                layer.ResetPhysicsOnLoop)));
        ApplyLightingToModel(model);
    }

    private void ApplyRelationToModel(GameEntity entity, EditorPmxObject runtime)
    {
        if (runtime.RelationUpdater is not null)
        {
            _ = runtime.Model.RemoveTransformUpdater(runtime.RelationUpdater);
            runtime.RelationUpdater = null;
        }

        if (!entity.Relation.Enabled || string.IsNullOrWhiteSpace(entity.Relation.RelationEntity))
        {
            return;
        }

        EditorPmxObject? relation = _pmxObjects.FirstOrDefault(item =>
            !ReferenceEquals(item, runtime)
            && (string.Equals(item.Entity.Id, entity.Relation.RelationEntity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Entity.Name, entity.Relation.RelationEntity, StringComparison.OrdinalIgnoreCase)));
        if (relation is null)
        {
            UpdateStatus($"Relation PMX not found: {entity.Relation.RelationEntity}");
            return;
        }

        RelationTransformUpdater updater = runtime.Model.CreateRelationTransformUpdater(
            relation.Model,
            entity.Relation.BindComponentTransform);
        updater.BindLighting = entity.Relation.BindLighting;
        runtime.RelationUpdater = updater;
    }

    private void ApplyAllRelationsToRuntime()
    {
        foreach (EditorPmxObject runtime in _pmxObjects)
        {
            ApplyRelationToModel(runtime.Entity, runtime);
        }
    }

    public void ApplySelectedParticleToRuntime()
    {
        GameEntity? entity = SelectedEntity;
        if (entity is null || !string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EditorParticleObject? runtime = _particleObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is null)
        {
            TryLoadParticleRuntime(entity);
            return;
        }

        ApplyEntityToParticle(entity, runtime.Component, resetParticles: false);
    }

    public void ApplySelectedWaterToRuntime()
    {
        GameEntity? entity = SelectedEntity;
        if (entity is null || !string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EditorWaterObject? runtime = _waterObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is null || Math.Abs(runtime.Component.SurfaceSize - Math.Max(entity.Water.Size, 0.1f)) > 0.001f)
        {
            if (runtime is not null)
            {
                RemoveComponent(runtime.Component);
                _waterObjects.Remove(runtime);
            }

            TryLoadWaterRuntime(entity);
            return;
        }

        ApplyEntityToWater(entity, runtime.Component);
    }

    private bool TryLoadParticleRuntime(GameEntity entity)
    {
        ParticleSystemComponent? component = null;
        try
        {
            component = new ParticleSystemComponent(
                _camera,
                ParticleEntitySettingsMapper.ToSettings(entity.Particle))
            {
                DrawOrder = 130
            };
            _ = AddComponent(component);
            ApplyEntityToParticle(entity, component, resetParticles: true);
            _particleObjects.Add(new EditorParticleObject
            {
                Entity = entity,
                Component = component
            });
            return true;
        }
        catch (Exception ex)
        {
            if (component is not null)
            {
                _ = RemoveComponent(component);
            }

            UpdateStatus($"Failed to load particle entity: {ex.Message}");
            return false;
        }
    }

    private bool TryLoadWaterRuntime(GameEntity entity)
    {
        WaterSurfaceComponent? component = null;
        try
        {
            component = new WaterSurfaceComponent(_camera, Math.Max(entity.Water.Size, 0.1f))
            {
                DrawOrder = 120
            };
            _ = AddComponent(component);
            ApplyEntityToWater(entity, component);
            _waterObjects.Add(new EditorWaterObject
            {
                Entity = entity,
                Component = component
            });
            return true;
        }
        catch (Exception ex)
        {
            if (component is not null)
            {
                _ = RemoveComponent(component);
            }

            UpdateStatus($"Failed to load water surface: {ex.Message}");
            return false;
        }
    }

    private static void ApplyEntityToParticle(GameEntity entity, ParticleSystemComponent component, bool resetParticles)
    {
        component.Position = entity.Transform.Position.ToVector3();
        component.Visible = entity.IsPlaying;
        component.SimulationSpeed = Math.Clamp(entity.Particle.SimulationSpeed, 0.0f, 10.0f);
        component.Opacity = Math.Clamp(entity.Particle.Opacity, 0.0f, 1.0f);
        component.ApplySettings(ParticleEntitySettingsMapper.ToSettings(entity.Particle), resetParticles);
    }

    private static void ApplyEntityToWater(GameEntity entity, WaterSurfaceComponent component)
    {
        component.Position = entity.Transform.Position.ToVector3();
        component.Rotation = ToQuaternion(entity.Transform.RotationDegrees.ToVector3());
        component.Scale = entity.Transform.Scale.ToVector3();
        component.Enabled = entity.IsPlaying;
        component.Visible = entity.IsPlaying;
        component.Alpha = entity.Water.Alpha;
        component.AnimationSpeed = entity.Water.AnimationSpeed;
        component.NormalTiling = Math.Max(entity.Water.NormalTiling, 0.001f);
        component.DeepColor = entity.Water.DeepColor.ToVector3();
        component.ReflectionTint = entity.Water.ReflectionTint.ToVector3();
        component.SkyReflectionStrength = entity.Water.SkyReflectionStrength;
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

    private void TryLoadAudioRuntime(AudioAsset audioAsset)
    {
        if (Audio is null)
        {
            UpdateStatus("Audio is unavailable on this machine.");
            return;
        }

        if (_audioSources.ContainsKey(audioAsset.Path))
        {
            return;
        }

        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, audioAsset.Path);
        if (!File.Exists(fullPath))
        {
            UpdateStatus($"Audio file not found: {fullPath}");
            return;
        }

        try
        {
            AudioClip clip = Audio.LoadClip(fullPath);
            AudioSource source = Audio.CreateSource(clip);
            source.Volume = audioAsset.Volume;
            source.Looping = audioAsset.Loop;
            _audioClips[audioAsset.Path] = clip;
            _audioSources[audioAsset.Path] = source;
            if (audioAsset.PlayOnStart)
            {
                source.Play();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to load audio: {ex.Message}");
        }
    }

    private void ClearSceneRuntime()
    {
        foreach (EditorPmxObject item in _pmxObjects.ToArray())
        {
            RemoveComponent(item.Model);
        }

        _pmxObjects.Clear();

        foreach (EditorParticleObject item in _particleObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _particleObjects.Clear();

        foreach (EditorWaterObject item in _waterObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _waterObjects.Clear();

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

    private void EnsureDefaultScripts()
    {
        foreach (ScriptBinding binding in Project.Scene.LoadingScripts)
        {
            EnsureLoadingScriptTemplate(binding);
        }

        foreach (GameEntity entity in Project.Scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Scripts)
            {
                EnsureCleanEntityScriptTemplate(binding);
            }
        }
    }

    private void EnsureScriptTemplate(ScriptBinding binding)
    {
        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, binding.Path);
        if (File.Exists(fullPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string content = binding.Language == "python"
            ? """
              # Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
                  # entity.bind_relation("body", bind_component_transform=True, bind_lighting=False)
                  pass

              def gui_event(entity, scene, input, audio, control_id, event_name):
                  if event_name == "clicked":
                      entity.speak("按钮被点击了")

              def update(entity, scene, input, audio, delta_seconds):
                  pass
              """
            : """
              // Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds.
              if (IsStart)
              {
                  // Entity.SetPosition(0, 0, 0);
                  // Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f);
                  // Entity.BindRelation("body", bindComponentTransform: true, bindLighting: false);
              }

              if (IsGuiEvent && GuiEventName == "clicked")
              {
                  Entity.Speak("按钮被点击了");
              }

              if (IsUpdate)
              {
                  // Entity.RotateY(30.0f * (float)DeltaSeconds);
              }
              """;

        File.WriteAllText(fullPath, content);
    }

    private void EnsureEntityScriptTemplate(ScriptBinding binding)
    {
        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, binding.Path);
        if (File.Exists(fullPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string content = binding.Language == "python"
            ? """
              # Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
                  # entity.bind_relation("body", bind_component_transform=True, bind_lighting=False)
                  pass

              def gui_event(entity, scene, input, audio, control_id, event_name):
                  if event_name == "clicked":
                      entity.speak("按钮被点击了")

              def update(entity, scene, input, audio, delta_seconds):
                  # Example: click the ground plane and move this entity there.
                  # if input.is_mouse_button_down("left"):
                  #     ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
                  #     hit = ray.intersect_plane_y(0.0)
                  #     if hit is not None:
                  #         entity.set_position(hit[0], hit[1], hit[2])
                  pass
              """
            : """
              // Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds.
              if (IsStart)
              {
                  // Entity.SetPosition(0, 0, 0);
                  // Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f);
                  // Entity.BindRelation("body", bindComponentTransform: true, bindLighting: false);
              }

              if (IsGuiEvent && GuiEventName == "clicked")
              {
                  Entity.Speak("按钮被点击了");
              }

              if (IsUpdate)
              {
                  // Example: click the ground plane and move this entity there.
                  // if (Input.IsMouseButtonDown("left"))
                  // {
                  //     RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
                  //     if (ray.TryIntersectPlaneY(0.0f, out Vector3 hit))
                  //     {
                  //         Entity.SetPosition(hit.X, hit.Y, hit.Z);
                  //     }
                  // }
              }
              """;

        File.WriteAllText(fullPath, content);
    }

    private void EnsureLoadingScriptTemplate(ScriptBinding binding)
    {
        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, binding.Path);
        if (File.Exists(fullPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string content = binding.Language == "python"
            ? """
              # Scene loading lifecycle script.
              # GamePlayer calls these functions when a scene transition loads.

              def loading_started(entity, scene, input, audio, progress, message):
                  print(f"scene loading started: {scene.name}")

              def loading_progress(entity, scene, input, audio, progress, message):
                  # progress is 0.0 - 1.0.
                  pass

              def loading_completed(entity, scene, input, audio, progress, message):
                  print(f"scene loading completed: {scene.name}")
              """
            : """
              // Scene loading lifecycle script.
              // Available globals: Entity, Scene, Input, Audio, IsLoadingEvent,
              // LoadingEventName, LoadingProgress, LoadingMessage.

              if (IsLoadingEvent && LoadingEventName == "loading_started")
              {
                  Console.WriteLine($"scene loading started: {Scene.Name}");
              }

              if (IsLoadingEvent && LoadingEventName == "loading_progress")
              {
                  // LoadingProgress is 0.0 - 1.0.
              }

              if (IsLoadingEvent && LoadingEventName == "loading_completed")
              {
                  Console.WriteLine($"scene loading completed: {Scene.Name}");
              }
              """;

        File.WriteAllText(fullPath, content);
    }

    private void EnsureCleanEntityScriptTemplate(ScriptBinding binding)
    {
        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, binding.Path);
        if (File.Exists(fullPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string content = binding.Language == "python"
            ? """
              # Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_speak")
                  pass

              def after_speak(entity, scene, input, audio):
                  print("speech completed")

              def gui_event(entity, scene, input, audio, control_id, event_name):
                  if event_name == "clicked":
                      entity.speak("按钮被点击了", on_completed="after_speak")

              def update(entity, scene, input, audio, delta_seconds):
                  # Example: click the ground plane and move this entity there.
                  # if input.is_mouse_button_down("left"):
                  #     ray = scene.camera.screen_point_to_ray(input.mouse_x, input.mouse_y)
                  #     hit = ray.intersect_plane_y(0.0)
                  #     if hit is not None:
                  #         entity.set_position(hit[0], hit[1], hit[2])
                  pass
              """
            : """
              // Called by Zhengyan.DigitalWife.Samples.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds.
              if (IsStart)
              {
                  // Entity.SetPosition(0, 0, 0);
                  // Entity.Speak("你好，我是小雨", speakerId: 0, speed: 1.0f, volume: 1.0f, onCompleted: () =>
                  // {
                  //     Console.WriteLine("speech completed");
                  // });
              }

              if (IsGuiEvent && GuiEventName == "clicked")
              {
                  Entity.Speak("按钮被点击了", () =>
                  {
                      Console.WriteLine("speech completed");
                  });
              }

              if (IsSpeechEvent && SpeechCallbackName == "after_speak")
              {
                  Console.WriteLine("speech completed by named callback");
              }

              if (IsUpdate)
              {
                  // Example: click the ground plane and move this entity there.
                  // if (Input.IsMouseButtonDown("left"))
                  // {
                  //     RuntimeRay ray = Scene.Camera.ScreenPointToRay(Input.MouseX, Input.MouseY);
                  //     if (ray.TryIntersectPlaneY(0.0f, out Vector3 hit))
                  //     {
                  //         Entity.SetPosition(hit.X, hit.Y, hit.Z);
                  //     }
                  // }
              }
              """;

        File.WriteAllText(fullPath, content);
    }

    private static GameProject CreateDefaultProject()
    {
        return new GameProject
        {
            Name = "Demo Game",
            Version = "0.1.0",
            Scene = new GameProjectScene
            {
                Name = "Main Scene"
            }
        };
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private static string SafeFileStem(string value)
    {
        string stem = string.IsNullOrWhiteSpace(value) ? "script" : value.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(ch, '_');
        }

        return stem.Replace(' ', '_');
    }

    private string CopyPmxAssetDirectoryIntoProject(string sourcePath)
    {
        string sourceFullPath = NormalizeInputPath(sourcePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"PMX file not found: {sourceFullPath}", sourceFullPath);
        }

        string sourceDirectory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException($"Cannot resolve PMX directory: {sourceFullPath}");
        string modelDirectoryName = SafeFileStem(Path.GetFileNameWithoutExtension(sourceFullPath));
        string targetDirectory = MakeUniqueDirectory(Path.Combine(ProjectDirectory, "assets", "models", modelDirectoryName));
        CopyDirectoryContents(sourceDirectory, targetDirectory);

        string targetPmxPath = Path.Combine(targetDirectory, Path.GetFileName(sourceFullPath));
        if (!File.Exists(targetPmxPath))
        {
            File.Copy(sourceFullPath, targetPmxPath);
        }

        return GameProjectPath.ToProjectRelative(ProjectDirectory, targetPmxPath);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            string targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(filePath, targetPath, overwrite: true);
        }
    }

    private static string MakeUniqueDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return directory;
        }

        string parent = Path.GetDirectoryName(directory) ?? string.Empty;
        string name = Path.GetFileName(directory);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(parent, $"{name}_{i}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeInputPath(string path)
    {
        string normalized = NormalizeClipboardText(path);
        if (normalized.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out Uri? fileUri) &&
            fileUri.IsFile)
        {
            normalized = fileUri.LocalPath;
        }

        normalized = normalized.TrimStart('\uFEFF', '\u200B', '\u200E', '\u200F', '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069', '?');
        int rootedPathStart = FindWindowsRootedPathStart(normalized);
        if (rootedPathStart > 0)
        {
            normalized = normalized[rootedPathStart..];
        }

        return Path.GetFullPath(normalized.Trim().Trim('"'));
    }

    private static int FindWindowsRootedPathStart(string path)
    {
        for (int i = 0; i + 2 < path.Length; i++)
        {
            if (char.IsAsciiLetter(path[i]) && path[i + 1] == ':' && (path[i + 2] == '\\' || path[i + 2] == '/'))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeClipboardText(string text)
    {
        string normalized = text.Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }
}
