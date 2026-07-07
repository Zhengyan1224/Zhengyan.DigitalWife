using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ImGuiNET;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class GameEditorGame : Zhengyan.DigitalWife.Mmd.Game.Game
{
    private readonly OrbitCamera _camera = new();
    private readonly List<EditorPmxObject> _pmxObjects = [];
    private readonly List<EditorParticleObject> _particleObjects = [];
    private readonly List<EditorWaterObject> _waterObjects = [];
    private readonly List<EditorPlaneObject> _planeObjects = [];
    private readonly Dictionary<string, double> _waterRippleTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _audioClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioSource> _audioSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resourceImportCache = new(StringComparer.OrdinalIgnoreCase);

    private SceneRenderTarget? _sceneRenderTarget;
    private OrbitCameraController? _cameraController;
    private GameEditorOverlayComponent? _overlay;
    private SceneRenderTextureManager? _renderTextureManager;
    private PlanarReflectionRenderer? _planarReflectionRenderer;
    private ShadowMapRenderer? _shadowMapRenderer;
    private UnderwaterPostProcessRenderer? _underwaterPostProcessRenderer;
    private SkyboxComponent? _skybox;
    private string _statusMessage = "Ready.";
    private bool _renderedSceneThisFrame;
    private int _selectedEntityIndex = -1;
    private int _debugDrawVersion = 1;

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

    public IReadOnlyList<EditorPlaneObject> PlaneObjects => _planeObjects;

    public string StatusMessage => _statusMessage;

    public string ActiveScenePath => GameProjectStore.NormalizeScenePath(Project.EditorScene);

    public int SelectedEntityIndex
    {
        get => _selectedEntityIndex;
        set
        {
            if (_selectedEntityIndex == value)
            {
                return;
            }

            _selectedEntityIndex = value;
            InvalidateDebugDraw();
        }
    }

    public SceneRenderTarget SceneRenderTarget => _sceneRenderTarget ?? throw new InvalidOperationException("Scene render target has not been created.");

    public SceneRenderTextureManager? RenderTextureManager => _renderTextureManager;

    public int DebugDrawVersion => _debugDrawVersion;

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
        _renderTextureManager = new SceneRenderTextureManager(this, () => Project.Scene, GetRenderTextureExcludedComponents);
        _planarReflectionRenderer = new PlanarReflectionRenderer(this);
        _shadowMapRenderer = new ShadowMapRenderer(this);
        _underwaterPostProcessRenderer = new UnderwaterPostProcessRenderer(GraphicsDevice.Gl, "EditorUnderwater");

        ApplyCameraSettings();
        ApplySceneSettings();

        _cameraController = AddComponent(new OrbitCameraController(_camera)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f
        });

        _ = AddComponent(new EditorDebugAxesComponent(_camera)
        {
            DrawOrder = 900
        });

        _ = AddComponent(new EditorColliderWireframeComponent(this, _camera)
        {
            DrawOrder = 905
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

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void LateUpdate(GameTime gameTime)
    {
        UpdateWaterInteractions(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (_sceneRenderTarget is null)
        {
            return;
        }

        _renderTextureManager?.RenderAll(gameTime, _camera, ApplyRuntimeCamera, ApplyRuntimeCamera);

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
        _renderedSceneThisFrame = false;
        if (TryDrawCameraViewports(gameTime))
        {
            _renderedSceneThisFrame = true;
            return;
        }

        if (TryDrawUnderwaterCameraToSceneTarget(gameTime, _camera, 0, 0, _sceneRenderTarget.Width, _sceneRenderTarget.Height, scissorEnabled: false))
        {
            _renderedSceneThisFrame = true;
            return;
        }

        ApplyRuntimeCamera(_camera);
        RenderShadowMap(
            gameTime,
            _sceneRenderTarget.Width,
            _sceneRenderTarget.Height,
            () => _sceneRenderTarget.Bind());
        RenderPlanarWaterReflections(
            gameTime,
            _camera,
            _sceneRenderTarget.Width,
            _sceneRenderTarget.Height,
            () => _sceneRenderTarget.Bind());
    }

    private bool TryDrawCameraViewports(GameTime gameTime)
    {
        if (_sceneRenderTarget is null || _renderTextureManager is null || GraphicsDevice is null)
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

        GL gl = GraphicsDevice.Gl;
        int screenWidth = Math.Max(_sceneRenderTarget.Width, 1);
        int screenHeight = Math.Max(_sceneRenderTarget.Height, 1);
        gl.Enable(GLEnum.ScissorTest);

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
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            if (TryDrawUnderwaterCameraToSceneTarget(gameTime, camera, x, y, width, height, scissorEnabled: true))
            {
                continue;
            }

            ApplyRuntimeCamera(camera);
            RenderShadowMap(
                gameTime,
                width,
                height,
                () =>
                {
                    _sceneRenderTarget.Bind();
                    gl.Enable(GLEnum.ScissorTest);
                    gl.Viewport(x, y, (uint)width, (uint)height);
                    gl.Scissor(x, y, (uint)width, (uint)height);
                });
            RenderPlanarWaterReflections(
                gameTime,
                camera,
                width,
                height,
                () =>
                {
                    _sceneRenderTarget.Bind();
                    gl.Enable(GLEnum.ScissorTest);
                    gl.Viewport(x, y, (uint)width, (uint)height);
                    gl.Scissor(x, y, (uint)width, (uint)height);
                });
            DrawSceneComponentsOnce(gameTime);
        }

        gl.Disable(GLEnum.ScissorTest);
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

    private void RenderPlanarWaterReflections(
        GameTime gameTime,
        OrbitCamera camera,
        int targetWidth,
        int targetHeight,
        Action? restoreRenderTarget = null)
    {
        if (_planarReflectionRenderer is null || (_waterObjects.Count == 0 && _planeObjects.Count == 0))
        {
            return;
        }

        Vector4 clearColor = Project.Scene.Lighting.ClearColor.ToVector4();
        _planarReflectionRenderer.RenderAll(
            gameTime,
            camera,
            _waterObjects.Select(item => item.Component).ToArray(),
            _planeObjects.Select(item => item.Component).ToArray(),
            GetOverlayComponents(),
            ApplyRuntimeCamera,
            ApplyRuntimeCamera,
            clearColor,
            targetWidth,
            targetHeight,
            restoreRenderTarget);
    }

    private void RenderShadowMap(
        GameTime gameTime,
        int targetWidth,
        int targetHeight,
        Action? restoreRenderTarget = null)
    {
        if (_shadowMapRenderer is null || _sceneRenderTarget is null)
        {
            return;
        }

        Action restore = restoreRenderTarget ?? (() => _sceneRenderTarget.Bind());
        _shadowMapRenderer.Render(
            gameTime,
            _pmxObjects.Select(item => item.Model).ToArray(),
            _planeObjects.Select(item => item.Component).ToArray(),
            Project.Scene.Lighting.LightDirection.ToVector3(),
            Project.Scene.Lighting.ShadowColor.ToVector4(),
            targetWidth,
            targetHeight,
            restore);

        ShadowMapBinding? binding = _shadowMapRenderer.CurrentBinding;
        foreach (EditorPmxObject item in _pmxObjects)
        {
            item.Model.ShadowMap = binding;
        }

        foreach (EditorPlaneObject item in _planeObjects)
        {
            item.Component.ShadowMap = binding;
        }
    }

    private bool TryDrawUnderwaterCameraToSceneTarget(
        GameTime gameTime,
        OrbitCamera camera,
        int x,
        int y,
        int width,
        int height,
        bool scissorEnabled)
    {
        if (_sceneRenderTarget is null
            || _underwaterPostProcessRenderer is null
            || !TryResolveUnderwaterSettings(camera, out UnderwaterPostProcessSettings settings))
        {
            return false;
        }

        GL gl = GraphicsDevice.Gl;
        DrawUnderwaterCamera(
            gameTime,
            camera,
            width,
            height,
            settings,
            Project.Scene.Lighting.ClearColor.ToVector4(),
            () =>
            {
                _sceneRenderTarget.Bind();
                if (scissorEnabled)
                {
                    gl.Enable(GLEnum.ScissorTest);
                    gl.Scissor(x, y, (uint)width, (uint)height);
                }
                else
                {
                    gl.Disable(GLEnum.ScissorTest);
                }

                gl.Viewport(x, y, (uint)width, (uint)height);
            });
        return true;
    }

    private void DrawUnderwaterCamera(
        GameTime gameTime,
        OrbitCamera camera,
        int width,
        int height,
        UnderwaterPostProcessSettings settings,
        Vector4 clearColor,
        Action bindOutputTarget)
    {
        if (_underwaterPostProcessRenderer is null)
        {
            return;
        }

        GL gl = GraphicsDevice.Gl;
        camera.Width = width;
        camera.Height = height;

        _underwaterPostProcessRenderer.BeginCapture(width, height);
        gl.Disable(GLEnum.ScissorTest);
        gl.Disable(GLEnum.StencilTest);
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.StencilMask(0xFF);
        gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        ApplyRuntimeCamera(camera);
        Action restoreCaptureTarget = () =>
        {
            _underwaterPostProcessRenderer.CaptureTarget.Bind();
            gl.Disable(GLEnum.ScissorTest);
        };
        RenderShadowMap(gameTime, width, height, restoreCaptureTarget);
        RenderPlanarWaterReflections(gameTime, camera, width, height, restoreCaptureTarget);
        DrawSceneComponentsOnce(gameTime);

        bindOutputTarget();
        _underwaterPostProcessRenderer.Draw(camera, settings, gameTime.TotalSeconds, width, height);
    }

    private bool TryResolveUnderwaterSettings(OrbitCamera camera, out UnderwaterPostProcessSettings settings)
    {
        settings = default;
        EditorWaterObject? activeWater = null;
        float activeDepth = float.MaxValue;

        foreach (EditorWaterObject waterObject in _waterObjects)
        {
            WaterSurfaceSettings water = waterObject.Entity.Water;
            if (!water.UnderwaterEffectEnabled
                || !waterObject.Component.Enabled
                || !waterObject.Component.Visible
                || !TryGetCameraWaterDepth(waterObject.Component, camera.Position, out float surfaceDepth))
            {
                continue;
            }

            if (surfaceDepth < activeDepth)
            {
                activeDepth = surfaceDepth;
                activeWater = waterObject;
            }
        }

        if (activeWater is null)
        {
            return false;
        }

        WaterSurfaceSettings settingsSource = activeWater.Entity.Water;
        settings = CreateUnderwaterSettings(settingsSource, activeDepth);
        return true;
    }

    private static bool TryGetCameraWaterDepth(WaterSurfaceComponent water, Vector3 cameraPosition, out float surfaceDepth)
    {
        surfaceDepth = 0.0f;
        if (!water.TryGetSurfaceHeight(cameraPosition, out float surfaceHeight)
            || cameraPosition.Y >= surfaceHeight + 0.03f)
        {
            return false;
        }

        surfaceDepth = Math.Max(surfaceHeight - cameraPosition.Y, 0.001f);
        return true;
    }

    private static UnderwaterPostProcessSettings CreateUnderwaterSettings(WaterSurfaceSettings water, float surfaceDepth)
    {
        return new UnderwaterPostProcessSettings(
            ClampVector3(water.UnderwaterTint.ToVector3(), 0.0f, 2.0f),
            ClampVector3(water.UnderwaterFogColor.ToVector3(), 0.0f, 2.0f),
            Math.Clamp(water.UnderwaterFogDensity, 0.0f, 4.0f),
            Math.Max(water.UnderwaterVisibilityDistance, 0.1f),
            Math.Clamp(water.UnderwaterDistortionStrength, 0.0f, 0.05f),
            Math.Clamp(water.UnderwaterCausticsStrength, 0.0f, 1.0f),
            Math.Clamp(water.UnderwaterBubbleStrength, 0.0f, 1.0f),
            Math.Max(surfaceDepth, 0.0f));
    }

    private static Vector3 ClampVector3(Vector3 value, float min, float max)
    {
        return new Vector3(
            Math.Clamp(value.X, min, max),
            Math.Clamp(value.Y, min, max),
            Math.Clamp(value.Z, min, max));
    }

    protected override void UnloadContent()
    {
        ClearSceneRuntime();
        _shadowMapRenderer?.Dispose();
        _shadowMapRenderer = null;
        _underwaterPostProcessRenderer?.Dispose();
        _underwaterPostProcessRenderer = null;
        _planarReflectionRenderer?.Dispose();
        _planarReflectionRenderer = null;
        _renderTextureManager?.Dispose();
        _renderTextureManager = null;
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
        ApplyRuntimeSettings();
        SaveProject();
        UpdateStatus($"Created project '{Project.Name}'.");
    }

    public void LoadProject()
    {
        ClearSceneRuntime();
        Project = GameProjectStore.Load(ProjectDirectory);
        ApplyRuntimeSettings();
        int relationFixes = ReloadActiveSceneRuntime(clearFirst: false);
        string relationMessage = relationFixes > 0 ? $"\nNormalized {relationFixes} PMX relation binding(s)." : string.Empty;
        UpdateStatus($"Loaded project: {Path.Combine(ProjectDirectory, GameProjectStore.ProjectFileName)}{relationMessage}");
    }

    public void SaveProject()
    {
        Directory.CreateDirectory(ProjectDirectory);
        int relationFixes = NormalizeRelationBindings();
        int guiTargetFixes = NormalizeGuiTargets();
        if (relationFixes > 0)
        {
            ApplyAllRelationsToRuntime();
        }

        ResourceImportSummary resourceImport = PrepareAndImportResources();
        ScriptValidationSummary validation = PrepareAndValidateScripts();
        GameProjectStore.Save(ProjectDirectory, Project);
        string projectPath = Path.Combine(ProjectDirectory, GameProjectStore.ProjectFileName);
        string relationMessage = relationFixes > 0 ? $"\nNormalized {relationFixes} PMX relation binding(s)." : string.Empty;
        string guiTargetMessage = guiTargetFixes > 0 ? $"\nNormalized {guiTargetFixes} GUI target binding(s)." : string.Empty;
        UpdateStatus(validation.HasErrors
            ? $"Saved project with script errors: {projectPath}\n{resourceImport.Message}\n{validation.Message}{relationMessage}{guiTargetMessage}"
            : $"Saved project: {projectPath}\n{resourceImport.Message}\n{validation.Message}{relationMessage}{guiTargetMessage}");
    }

    public GameProjectPackageBuildResult ExportProjectPackage(
        string outputPath,
        string? password = null,
        long splitPartSizeBytes = 0,
        bool includeSaves = false)
    {
        SaveProject();
        GameProjectPackageBuildResult result = GameProjectPackage.Create(
            ProjectDirectory,
            new GameProjectPackageBuildOptions
            {
                OutputPath = outputPath,
                Password = password,
                SplitPartSizeBytes = splitPartSizeBytes,
                IncludeSaves = includeSaves
            });

        string outputSummary = result.Split
            ? $"{result.PartPaths.Count} part(s), first: {result.PartPaths[0]}"
            : result.OutputPath;
        UpdateStatus($"Exported package: {outputSummary}\nEncrypted: {result.Encrypted}\nTotal bytes: {result.TotalBytes:N0}");
        return result;
    }

    public void CreateScene(string sceneName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(sceneName) ? "New Scene" : sceneName.Trim();
        SaveActiveSceneFile();

        GameProjectStore.NormalizeScenes(Project);
        string scenePath = GameProjectStore.CreateUniqueScenePath(ProjectDirectory, Project, normalizedName);
        Project.Scenes.Add(scenePath);
        Project.EditorScene = scenePath;
        Project.Scene = new GameProjectScene
        {
            Name = normalizedName
        };

        _ = ReloadActiveSceneRuntime(clearFirst: true);
        GameProjectStore.Save(ProjectDirectory, Project);
        UpdateStatus($"Created scene: {scenePath}");
    }

    public void SwitchScene(string scenePath)
    {
        string normalizedScenePath = GameProjectStore.NormalizeScenePath(scenePath);
        if (string.Equals(normalizedScenePath, ActiveScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveActiveSceneFile();
        Project.EditorScene = normalizedScenePath;
        Project.Scene = GameProjectStore.LoadScene(ProjectDirectory, normalizedScenePath);
        _ = ReloadActiveSceneRuntime(clearFirst: true);
        GameProjectStore.Save(ProjectDirectory, Project);
        UpdateStatus($"Switched scene: {normalizedScenePath}");
    }

    public void DeleteScene(string scenePath)
    {
        GameProjectStore.NormalizeScenes(Project);
        if (Project.Scenes.Count <= 1)
        {
            UpdateStatus("Cannot delete the last scene.");
            return;
        }

        string normalizedScenePath = GameProjectStore.NormalizeScenePath(scenePath);
        int removeIndex = Project.Scenes.FindIndex(path => string.Equals(
            GameProjectStore.NormalizeScenePath(path),
            normalizedScenePath,
            StringComparison.OrdinalIgnoreCase));
        if (removeIndex < 0)
        {
            UpdateStatus($"Scene not found: {normalizedScenePath}");
            return;
        }

        bool deletingActiveScene = string.Equals(normalizedScenePath, ActiveScenePath, StringComparison.OrdinalIgnoreCase);
        if (!deletingActiveScene)
        {
            SaveActiveSceneFile();
        }

        Project.Scenes.RemoveAt(removeIndex);
        GameProjectStore.DeleteScene(ProjectDirectory, normalizedScenePath);

        if (deletingActiveScene)
        {
            Project.EditorScene = Project.Scenes[Math.Min(removeIndex, Project.Scenes.Count - 1)];
            if (string.Equals(Project.DefaultScene, normalizedScenePath, StringComparison.OrdinalIgnoreCase))
            {
                Project.DefaultScene = Project.EditorScene;
            }

            Project.Scene = GameProjectStore.LoadScene(ProjectDirectory, Project.EditorScene);
            _ = ReloadActiveSceneRuntime(clearFirst: true);
        }

        GameProjectStore.Save(ProjectDirectory, Project);
        UpdateStatus($"Deleted scene: {normalizedScenePath}");
    }

    private void SaveActiveSceneFile()
    {
        Directory.CreateDirectory(ProjectDirectory);
        _ = PrepareAndImportResources();
        _ = PrepareAndValidateScripts();
        GameProjectStore.SaveScene(ProjectDirectory, ActiveScenePath, Project.Scene);
    }

    private int ReloadActiveSceneRuntime(bool clearFirst)
    {
        if (clearFirst)
        {
            ClearSceneRuntime();
        }

        int relationFixes = NormalizeRelationBindings();
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
        InvalidateDebugDraw();
        return relationFixes;
    }

    private int NormalizeRelationBindings()
    {
        List<GameEntity> pmxEntities = Project.Scene.Entities
            .Where(entity => string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
            .ToList();
        int changes = 0;

        foreach (GameEntity entity in pmxEntities)
        {
            if (!entity.Relation.Enabled)
            {
                continue;
            }

            string relationEntity = entity.Relation.RelationEntity.Trim();
            List<GameEntity> candidates = pmxEntities
                .Where(candidate => !ReferenceEquals(candidate, entity))
                .ToList();

            if (string.IsNullOrWhiteSpace(relationEntity))
            {
                if (candidates.Count == 1)
                {
                    entity.Relation.RelationEntity = candidates[0].Id;
                    changes++;
                }

                continue;
            }

            GameEntity? match = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, relationEntity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, relationEntity, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !string.Equals(entity.Relation.RelationEntity, match.Id, StringComparison.Ordinal))
            {
                entity.Relation.RelationEntity = match.Id;
                changes++;
            }
            else if (!string.Equals(entity.Relation.RelationEntity, relationEntity, StringComparison.Ordinal))
            {
                entity.Relation.RelationEntity = relationEntity;
                changes++;
            }
        }

        return changes;
    }

    private int NormalizeGuiTargets()
    {
        int changes = 0;
        foreach (GuiControlSettings control in Project.Scene.GuiControls)
        {
            string targetEntity = control.TargetEntity.Trim();
            if (string.IsNullOrWhiteSpace(targetEntity))
            {
                continue;
            }

            GameEntity? match = Project.Scene.Entities.FirstOrDefault(entity =>
                string.Equals(entity.Id, targetEntity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.Name, targetEntity, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                if (!string.Equals(control.TargetEntity, match.Id, StringComparison.Ordinal))
                {
                    control.TargetEntity = match.Id;
                    changes++;
                }

                continue;
            }

            List<GameEntity> scriptedPmxEntities = Project.Scene.Entities
                .Where(entity => string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase)
                    && entity.Scripts.Any(script => script.Enabled))
                .ToList();
            if (scriptedPmxEntities.Count == 1)
            {
                control.TargetEntity = scriptedPmxEntities[0].Id;
                changes++;
            }
        }

        return changes;
    }

    public void AddPmxEntityFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = ResolveAssetSourcePath(sourcePath);
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
        string normalizedSourcePath = ResolveAssetSourcePath(sourcePath);
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
        string normalizedSourcePath = ResolveAssetSourcePath(sourcePath);
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
        string normalizedSourcePath = ResolveAssetSourcePath(sourcePath);
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

    public void AddTexturedPlaneFromPath(string sourcePath, bool copyIntoProject)
    {
        string normalizedSourcePath = ResolveAssetSourcePath(sourcePath);
        string assetPath = copyIntoProject
            ? GameProjectPath.CopyAssetIntoProject(ProjectDirectory, normalizedSourcePath, "textures")
            : GameProjectPath.ToProjectRelative(ProjectDirectory, normalizedSourcePath);

        GameEntity entity = new()
        {
            Name = Path.GetFileNameWithoutExtension(assetPath),
            Type = "textured_plane",
            AssetPath = assetPath,
            Transform = new TransformSettings
            {
                Position = new Vector3Dto(0.0f, 1.0f, 0.0f),
                RotationDegrees = Vector3Dto.Zero,
                Scale = Vector3Dto.One
            },
            Plane = new TexturedPlaneSettings
            {
                TexturePath = assetPath,
                Width = 2.0f,
                Height = 2.0f
            },
            Scripts =
            [
                new ScriptBinding
                {
                    Language = Project.ScriptRuntime.PreferredLanguage,
                    Path = Project.ScriptRuntime.PreferredLanguage == "python"
                        ? $"scripts/{SafeFileStem(Path.GetFileNameWithoutExtension(assetPath))}_plane.py"
                        : $"scripts/{SafeFileStem(Path.GetFileNameWithoutExtension(assetPath))}_plane.csx"
                }
            ]
        };

        Project.Scene.Entities.Add(entity);
        SelectedEntityIndex = Project.Scene.Entities.Count - 1;
        bool runtimeLoaded = TryLoadEntityRuntime(entity);
        EnsureCleanEntityScriptTemplate(entity.Scripts[0]);
        if (runtimeLoaded)
        {
            UpdateStatus($"Added textured plane: {assetPath}");
        }
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

    public void AddEmptyEntity()
    {
        GameEntity entity = new()
        {
            Name = "Empty Object",
            Type = "empty_object",
            Transform = new TransformSettings
            {
                Position = Vector3Dto.Zero,
                RotationDegrees = Vector3Dto.Zero,
                Scale = Vector3Dto.One
            },
            Scripts =
            [
                new ScriptBinding
                {
                    Language = Project.ScriptRuntime.PreferredLanguage,
                    Path = Project.ScriptRuntime.PreferredLanguage == "python"
                        ? "scripts/empty_object.py"
                        : "scripts/empty_object.csx"
                }
            ]
        };

        Project.Scene.Entities.Add(entity);
        SelectedEntityIndex = Project.Scene.Entities.Count - 1;
        EnsureCleanEntityScriptTemplate(entity.Scripts[0]);
        TryLoadEntityRuntime(entity);
        UpdateStatus("Added empty object.");
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
        UpdateStatus(ValidateScriptBinding(binding, "entity script").ToStatusMessage($"Added {language} script: {binding.Path}"));
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
        UpdateStatus(ValidateScriptBinding(binding, "scene loading script").ToStatusMessage($"Added scene loading {normalizedLanguage} script: {binding.Path}"));
    }

    public void NormalizeAndValidateScriptBinding(ScriptBinding binding, string context)
    {
        ScriptValidationResult result = PrepareScriptBinding(binding, context);
        UpdateStatus(result.ToStatusMessage($"Script ready: {binding.Path}"));
    }

    public GameEntity? SelectedEntity => SelectedEntityIndex >= 0 && SelectedEntityIndex < Project.Scene.Entities.Count
        ? Project.Scene.Entities[SelectedEntityIndex]
        : null;

    public IReadOnlyList<string> GetPmxBoneNames(GameEntity entity)
    {
        return FindPmxRuntime(entity)?.Model.NodeNames ?? [];
    }

    internal bool TryCreateColliderGeometry(GameEntity entity, ColliderSettings collider, out ColliderGeometry geometry)
    {
        geometry = default;
        if (string.Equals(collider.Shape, "mesh", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        geometry = CollisionGeometry.CreateCollider(collider, GetColliderParentWorld(entity, collider));
        return true;
    }

    internal Matrix4x4 GetColliderParentWorld(GameEntity entity, ColliderSettings collider)
    {
        if (string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(collider.BoundBoneName)
            && FindPmxRuntime(entity)?.Model.TryGetNodeWorld(collider.BoundBoneName, out Matrix4x4 boneWorld) == true)
        {
            return boneWorld;
        }

        return CreateEntityWorld(entity);
    }

    internal bool HasBoneBoundColliders()
    {
        foreach (GameEntity entity in Project.Scene.Entities)
        {
            if (!string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(entity))
            {
                if (collider.Enabled
                    && !string.IsNullOrWhiteSpace(collider.BoundBoneName)
                    && !string.Equals(collider.Shape, "mesh", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

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

        EditorPlaneObject? planeRuntime = _planeObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (planeRuntime is not null)
        {
            RemoveComponent(planeRuntime.Component);
            _planeObjects.Remove(planeRuntime);
        }

        RemoveWaterRippleEntries(entity.Id);
        ApplyAllRelationsToRuntime();
        SelectedEntityIndex = Math.Min(SelectedEntityIndex, Project.Scene.Entities.Count - 1);
        InvalidateDebugDraw();
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

        if (string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
        {
            ApplySelectedPlaneToRuntime();
            return;
        }

        InvalidateDebugDraw();

        if (IsEmptyEntity(entity))
        {
            UpdateStatus($"Updated empty object: {entity.Name}");
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

        ApplySkyboxSettings();
    }

    private void ApplySkyboxSettings()
    {
        SkyboxSettings skybox = Project.Scene.Skybox;
        if (!skybox.Enabled)
        {
            if (_skybox is not null)
            {
                RemoveComponent(_skybox);
                _skybox = null;
            }

            return;
        }

        string texturePath = GameProjectPath.ToAbsolute(ProjectDirectory, skybox.TexturePath);
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

    public void ApplyRuntimeSettings()
    {
        Options.UseOpenCL = Project.Runtime.UseOpenCL;
        Zhengyan.DigitalWife.Mmd.Kernel.UseOpenCL = Project.Runtime.UseOpenCL;
        Zhengyan.DigitalWife.Mmd.Kernel.ResetOpenClProbe();
        bool openClRequested = Project.Runtime.UseOpenCL;
        bool openClActive = openClRequested && Zhengyan.DigitalWife.Mmd.Kernel.CanUseOpenClSafely();
        Console.WriteLine(openClRequested
            ? openClActive
                ? "[GameEditor] PMX compute backend: OpenCL"
                : "[GameEditor] OpenCL requested but unavailable; falling back to CPU"
            : "[GameEditor] OpenCL disabled by project/runtime setting; using CPU");

        foreach (EditorPmxObject pmxObject in _pmxObjects.ToArray())
        {
            pmxObject.Model.ReloadForCurrentOpenClSetting();
        }
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

    private ResourceImportSummary PrepareAndImportResources()
    {
        int imported = 0;
        List<string> warnings = [];
        _resourceImportCache.Clear();

        Project.Window.IconPath = ImportOptionalResource(Project.Window.IconPath, "window-icons", ref imported, warnings);
        Project.Window.DesktopSpriteTrayIconPath = ImportOptionalResource(Project.Window.DesktopSpriteTrayIconPath, "tray-icons", ref imported, warnings);
        Project.Window.DesktopSpriteTrayWindowsIconPath = ImportOptionalResource(Project.Window.DesktopSpriteTrayWindowsIconPath, "tray-icons/windows", ref imported, warnings);
        Project.Window.DesktopSpriteTrayLinuxIconPath = ImportOptionalResource(Project.Window.DesktopSpriteTrayLinuxIconPath, "tray-icons/linux", ref imported, warnings);
        Project.Window.DesktopSpriteTrayMacOSIconPath = ImportOptionalResource(Project.Window.DesktopSpriteTrayMacOSIconPath, "tray-icons/macos", ref imported, warnings);

        GameProjectVoiceSettings voice = Project.Voice;
        voice.ModelPath = ImportOptionalResource(voice.ModelPath, "tts", ref imported, warnings);
        voice.TokensPath = ImportOptionalResource(voice.TokensPath, "tts", ref imported, warnings);
        voice.LexiconPath = ImportOptionalNullableResource(voice.LexiconPath, "tts", ref imported, warnings);
        voice.DataDirectory = ImportOptionalNullableResource(voice.DataDirectory, "tts", ref imported, warnings, copyDirectory: true);
        voice.DictDirectory = ImportOptionalNullableResource(voice.DictDirectory, "tts", ref imported, warnings, copyDirectory: true);
        voice.VocoderPath = ImportOptionalNullableResource(voice.VocoderPath, "tts", ref imported, warnings);
        voice.RuleFars = ImportOptionalNullableResource(voice.RuleFars, "tts", ref imported, warnings);
        voice.RuleFsts = ImportRuleFsts(voice.RuleFsts, ref imported, warnings);
        NormalizeMatchaVoiceDataDirectory(voice);
        voice.LipSync.DictionaryDirectory = ImportOptionalResource(voice.LipSync.DictionaryDirectory, "tts/lip-sync", ref imported, warnings, copyDirectory: true);

        GameProjectAsrSettings asr = Project.Asr;
        asr.Sherpa.TokensPath = ImportOptionalResource(asr.Sherpa.TokensPath, "asr/sherpa", ref imported, warnings);
        asr.Sherpa.EncoderPath = ImportOptionalNullableResource(asr.Sherpa.EncoderPath, "asr/sherpa", ref imported, warnings);
        asr.Sherpa.DecoderPath = ImportOptionalNullableResource(asr.Sherpa.DecoderPath, "asr/sherpa", ref imported, warnings);
        asr.Sherpa.JoinerPath = ImportOptionalNullableResource(asr.Sherpa.JoinerPath, "asr/sherpa", ref imported, warnings);
        asr.Sherpa.ModelPath = ImportOptionalNullableResource(asr.Sherpa.ModelPath, "asr/sherpa", ref imported, warnings);
        asr.Whisper.ModelPath = ImportOptionalResource(asr.Whisper.ModelPath, "asr/whisper", ref imported, warnings);

        GameProjectScene scene = Project.Scene;
        scene.LoadingScreen.BackgroundImagePath = ImportOptionalResource(scene.LoadingScreen.BackgroundImagePath, "loading", ref imported, warnings);
        scene.Skybox.TexturePath = ImportOptionalResource(scene.Skybox.TexturePath, "skybox", ref imported, warnings);

        foreach (AudioAsset audio in scene.Audio)
        {
            audio.Path = ImportOptionalResource(audio.Path, "audio", ref imported, warnings);
        }

        foreach (MotionAsset motion in scene.Motions)
        {
            motion.Path = ImportOptionalResource(motion.Path, "motions", ref imported, warnings);
        }

        foreach (SpriteSettings sprite in scene.Sprites)
        {
            sprite.Path = ImportOptionalResource(sprite.Path, "sprites", ref imported, warnings);
        }

        foreach (GameEntity entity in scene.Entities)
        {
            ImportEntityResources(entity, ref imported, warnings);
        }

        if (warnings.Count == 0)
        {
            return new ResourceImportSummary(false, $"Resource import passed: {imported} file(s) copied.");
        }

        return new ResourceImportSummary(
            true,
            $"Resource import completed with {warnings.Count} warning(s), {imported} file(s) copied.\n{string.Join('\n', warnings.Take(5))}");
    }

    private void ImportEntityResources(GameEntity entity, ref int imported, List<string> warnings)
    {
        if (string.Equals(entity.Type, "pmx_model", StringComparison.OrdinalIgnoreCase))
        {
            entity.AssetPath = ImportPmxResource(entity.AssetPath, ref imported, warnings);
        }
        else if (string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
        {
            string texture = ImportOptionalResource(
                string.IsNullOrWhiteSpace(entity.Plane.TexturePath) ? entity.AssetPath : entity.Plane.TexturePath,
                "textures",
                ref imported,
                warnings);
            entity.Plane.TexturePath = texture;
            entity.AssetPath = texture;
        }

        if (!string.IsNullOrWhiteSpace(entity.Particle.TexturePath))
        {
            entity.Particle.TexturePath = ImportOptionalNullableResource(entity.Particle.TexturePath, "particles", ref imported, warnings);
        }

        foreach (MotionLayerSettings layer in entity.MotionLayers)
        {
            layer.Path = ImportOptionalResource(layer.Path, "motions", ref imported, warnings);
        }
    }

    private string ImportPmxResource(string path, ref int imported, List<string> warnings)
    {
        if (!ShouldImportResource(path))
        {
            return path;
        }

        string fullPath = ResolveImportPath(path);
        if (!File.Exists(fullPath))
        {
            warnings.Add($"PMX not found: {path}");
            return path;
        }

        if (IsPathInsideDirectory(fullPath, ProjectDirectory))
        {
            return GameProjectPath.ToProjectRelative(ProjectDirectory, fullPath);
        }

        string importedPath = CopyPmxAssetDirectoryIntoProject(fullPath);
        imported++;
        return importedPath;
    }

    private string ImportOptionalResource(
        string? path,
        string assetSubdirectory,
        ref int imported,
        List<string> warnings,
        bool copyDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = GameProjectPath.NormalizePathText(path);
        if (!ShouldImportResource(normalizedPath))
        {
            return normalizedPath;
        }

        string fullPath = ResolveImportPath(normalizedPath);
        string cacheKey = GetResourceImportCacheKey(fullPath);
        if (_resourceImportCache.TryGetValue(cacheKey, out string? cachedPath))
        {
            return cachedPath;
        }

        if (File.Exists(fullPath))
        {
            if (IsPathInsideDirectory(fullPath, ProjectDirectory))
            {
                string projectRelative = GameProjectPath.ToProjectRelative(ProjectDirectory, fullPath);
                _resourceImportCache[cacheKey] = projectRelative;
                return projectRelative;
            }

            string importedPath = GameProjectPath.CopyAssetIntoProject(ProjectDirectory, fullPath, assetSubdirectory);
            _resourceImportCache[cacheKey] = importedPath;
            imported++;
            return importedPath;
        }

        if (Directory.Exists(fullPath))
        {
            if (IsPathInsideDirectory(fullPath, ProjectDirectory))
            {
                string projectRelative = GameProjectPath.ToProjectRelative(ProjectDirectory, fullPath);
                _resourceImportCache[cacheKey] = projectRelative;
                return projectRelative;
            }

            if (!copyDirectory)
            {
                warnings.Add($"Directory resource is not supported here: {normalizedPath}");
                return normalizedPath;
            }

            string importedPath = CopyDirectoryResourceIntoProject(fullPath, assetSubdirectory);
            _resourceImportCache[cacheKey] = importedPath;
            imported++;
            return importedPath;
        }

        warnings.Add($"Resource not found: {normalizedPath}");
        return normalizedPath;
    }

    private string? ImportOptionalNullableResource(
        string? path,
        string assetSubdirectory,
        ref int imported,
        List<string> warnings,
        bool copyDirectory = false)
    {
        string importedPath = ImportOptionalResource(path, assetSubdirectory, ref imported, warnings, copyDirectory);
        return string.IsNullOrWhiteSpace(importedPath) ? null : importedPath;
    }

    private string? ImportRuleFsts(string? ruleFsts, ref int imported, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(ruleFsts))
        {
            return null;
        }

        List<string> importedPaths = [];
        foreach (string path in ruleFsts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string importedPath = ImportOptionalResource(path, "tts", ref imported, warnings);
            if (!string.IsNullOrWhiteSpace(importedPath))
            {
                importedPaths.Add(importedPath);
            }
        }

        return importedPaths.Count == 0 ? null : string.Join(",", importedPaths);
    }

    private void NormalizeMatchaVoiceDataDirectory(GameProjectVoiceSettings voice)
    {
        if (!string.Equals(voice.ModelKind, "matcha", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(voice.DataDirectory))
        {
            return;
        }

        string dataDirectory = GameProjectPath.NormalizePathText(voice.DataDirectory);
        string dataDirectoryFullPath = GameProjectPath.ToAbsolute(ProjectDirectory, dataDirectory);
        if (File.Exists(Path.Combine(dataDirectoryFullPath, "phontab")))
        {
            voice.DataDirectory = dataDirectory;
            return;
        }

        string nested = Path.Combine(dataDirectoryFullPath, "espeak-ng-data");
        if (File.Exists(Path.Combine(nested, "phontab")))
        {
            voice.DataDirectory = GameProjectPath.ToProjectRelative(ProjectDirectory, nested);
        }
    }

    private string CopyDirectoryResourceIntoProject(string sourceDirectory, string assetSubdirectory)
    {
        string targetRoot = Path.Combine(ProjectDirectory, "assets", assetSubdirectory);
        Directory.CreateDirectory(targetRoot);
        string targetDirectory = MakeUniqueDirectory(Path.Combine(targetRoot, SafeFileStem(Path.GetFileName(sourceDirectory))));
        CopyDirectoryContents(sourceDirectory, targetDirectory);
        return GameProjectPath.ToProjectRelative(ProjectDirectory, targetDirectory);
    }

    private string ResolveImportPath(string path)
    {
        string normalized = GameProjectPath.NormalizePathText(path);
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        }

        string projectRelative = GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        if (File.Exists(projectRelative) || Directory.Exists(projectRelative))
        {
            return projectRelative;
        }

        return Path.GetFullPath(normalized);
    }

    private static string GetResourceImportCacheKey(string fullPath)
    {
        return Path.GetFullPath(fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool ShouldImportResource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string trimmed = GameProjectPath.NormalizePathText(path);
        return !trimmed.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Replace('\\', '/').StartsWith("Resources/", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ResourceImportSummary(bool HasWarnings, string Message);

    public void ApplyCameraSettings()
    {
        CameraSettings camera = Project.Scene.Camera;
        EnsureSceneCameras();
        SceneCameraSettings mainCamera = Project.Scene.Cameras.First(item => item.IsMain);
        camera = mainCamera.Camera;
        Project.Scene.Camera = camera;
        Project.Scene.MainCamera = mainCamera.Name;
        _camera.SetLookAt(camera.Position.ToVector3(), camera.Target.ToVector3());
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
        foreach (EditorPmxObject item in _pmxObjects)
        {
            item.Model.Camera = camera;
        }

        foreach (EditorParticleObject item in _particleObjects)
        {
            item.Component.Camera = camera;
        }

        foreach (EditorWaterObject item in _waterObjects)
        {
            item.Component.Camera = camera;
        }

        foreach (EditorPlaneObject item in _planeObjects)
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
        if (_overlay is not null)
        {
            excluded.Add(_overlay);
        }

        excluded.AddRange(Components.OfType<EditorDebugAxesComponent>());
        excluded.AddRange(Components.OfType<EditorColliderWireframeComponent>());
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

    private static bool IsEmptyEntity(GameEntity entity)
    {
        string normalized = (entity.Type ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "empty" or "empty_object" or "game_object";
    }

    public void PlayOrPauseAudio(AudioAsset audioAsset)
    {
        PruneAudioRuntime();
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
            if (IsEmptyEntity(entity))
            {
                UpdateStatus($"Loaded empty object: {entity.Name}");
                return true;
            }

            if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadParticleRuntime(entity);
            }

            if (string.Equals(entity.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadWaterRuntime(entity);
            }

            if (string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadPlaneRuntime(entity);
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
                RuntimeTextureProvider = _renderTextureManager,
                DrawOrder = 100,
                ShouldUpdatePoseEvaluator = ShouldUpdatePmxPose,
                OffscreenPoseUpdateIntervalSeconds = 0.12f
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

        InvalidateDebugDraw();

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

        InvalidateDebugDraw();

        EditorWaterObject? runtime = _waterObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is null
            || Math.Abs(runtime.Component.SurfaceSize - Math.Max(entity.Water.Size, 0.1f)) > 0.001f
            || runtime.Component.MeshResolution != Math.Clamp(entity.Water.GerstnerMeshResolution, 8, 256))
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

    public void ApplySelectedPlaneToRuntime()
    {
        GameEntity? entity = SelectedEntity;
        if (entity is null || !string.Equals(entity.Type, "textured_plane", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InvalidateDebugDraw();

        EditorPlaneObject? runtime = _planeObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
        if (runtime is null)
        {
            TryLoadPlaneRuntime(entity);
            return;
        }

        ApplyEntityToPlane(entity, runtime.Component);
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
                RuntimeTextureProvider = _renderTextureManager,
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
            component = new WaterSurfaceComponent(
                _camera,
                Math.Max(entity.Water.Size, 0.1f),
                meshResolution: Math.Clamp(entity.Water.GerstnerMeshResolution, 8, 256))
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

    private bool TryLoadPlaneRuntime(GameEntity entity)
    {
        TexturedPlaneComponent? component = null;
        try
        {
            component = new TexturedPlaneComponent(_camera, ResolvePlaneTexturePath(entity))
            {
                RuntimeTextureProvider = _renderTextureManager,
                DrawOrder = 115
            };
            _ = AddComponent(component);
            ApplyEntityToPlane(entity, component);
            _planeObjects.Add(new EditorPlaneObject
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

            UpdateStatus($"Failed to load textured plane: {ex.Message}");
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
        component.GerstnerWavesEnabled = entity.Water.GerstnerWavesEnabled;
        component.GerstnerWaveCount = entity.Water.GerstnerWaveCount;
        component.GerstnerAmplitude = entity.Water.GerstnerAmplitude;
        component.GerstnerWavelength = entity.Water.GerstnerWavelength;
        component.GerstnerSpeed = entity.Water.GerstnerSpeed;
        component.GerstnerSteepness = entity.Water.GerstnerSteepness;
        component.GerstnerDirectionDegrees = entity.Water.GerstnerDirectionDegrees;
        component.DeepColor = entity.Water.DeepColor.ToVector3();
        component.ReflectionTint = entity.Water.ReflectionTint.ToVector3();
        component.SkyReflectionStrength = entity.Water.SkyReflectionStrength;
        component.MirrorReflectionEnabled = entity.Water.MirrorReflectionEnabled;
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
        component.ReceiveShadow = entity.Plane.ReceiveShadow;
        component.MirrorReflectionEnabled = entity.Plane.MirrorReflectionEnabled;
        component.MirrorReflectionStrength = entity.Plane.MirrorReflectionStrength;
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
                : GameProjectPath.ToAbsolute(ProjectDirectory, path);
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

    private void UpdateWaterInteractions(GameTime gameTime)
    {
        double now = gameTime.TotalSeconds;
        foreach (EditorWaterObject waterObject in _waterObjects)
        {
            GameEntity waterEntity = waterObject.Entity;
            if (!waterEntity.Water.EnableInteraction)
            {
                continue;
            }

            float waterHalfSize = Math.Max(waterEntity.Water.Size, 0.1f) * MathF.Max(MathF.Abs(waterEntity.Transform.Scale.X), MathF.Abs(waterEntity.Transform.Scale.Z));
            foreach (GameEntity entity in Project.Scene.Entities)
            {
                if (ReferenceEquals(entity, waterEntity))
                {
                    continue;
                }

                if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase)
                    && !entity.Particle.EnableWaterInteraction)
                {
                    continue;
                }

                if (string.Equals(entity.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
                {
                    EditorParticleObject? particleObject = _particleObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
                    if (particleObject is not null)
                    {
                        ProcessParticleWaterInteractions(waterObject, particleObject, waterEntity, waterHalfSize, now);
                    }

                    continue;
                }

                foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(entity))
                {
                    if (!collider.Enabled || !TryGetColliderApproximation(entity, collider, out Vector3 center, out float radius))
                    {
                        continue;
                    }

                    if (!waterObject.Component.TryGetSurfaceHeight(center, out float surfaceY)
                        || MathF.Abs(center.Y - surfaceY) > radius)
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
                            waterObject.Component.AddRipple(new Vector3(center.X, surfaceY, center.Z), waterEntity.Water.InteractionRadius, waterEntity.Water.InteractionStrength);
                        }
                    }
                }
            }
        }
    }

    private void ProcessParticleWaterInteractions(
        EditorWaterObject waterObject,
        EditorParticleObject particleObject,
        GameEntity waterEntity,
        float waterHalfSize,
        double now)
    {
        foreach (ParticleCollisionSample sample in particleObject.Component.GetCollisionSamples())
        {
            if (!waterObject.Component.TryGetSurfaceHeight(sample.Position, out float surfaceY))
            {
                continue;
            }

            float verticalDistance = sample.Position.Y - surfaceY;
            if (verticalDistance > sample.Radius)
            {
                continue;
            }

            Vector3 delta = sample.Position - waterEntity.Transform.Position.ToVector3();
            if (MathF.Abs(delta.X) > waterHalfSize || MathF.Abs(delta.Z) > waterHalfSize)
            {
                continue;
            }

            string rippleKey = BuildParticleRippleKey(waterEntity, particleObject.Entity, sample, waterEntity.Water.ParticleRippleMergeDistance);
            double minInterval = Math.Max(0.0, waterEntity.Water.ParticleRippleMinIntervalSeconds);
            if (!_waterRippleTimes.TryGetValue(rippleKey, out double lastRippleTime) || now - lastRippleTime >= minInterval)
            {
                _waterRippleTimes[rippleKey] = now;
                waterObject.Component.AddRipple(
                    new Vector3(sample.Position.X, surfaceY, sample.Position.Z),
                    waterEntity.Water.InteractionRadius,
                    waterEntity.Water.InteractionStrength,
                    waterEntity.Water.ParticleRippleMergeDistance);
            }

            if (particleObject.Entity.Particle.KillOnWaterContact)
            {
                particleObject.Component.KillParticle(sample.Index);
            }
        }
    }

    private static string BuildParticleRippleKey(GameEntity waterEntity, GameEntity particleEntity, ParticleCollisionSample sample, float mergeDistance)
    {
        if (mergeDistance <= 0.0001f)
        {
            return $"{waterEntity.Id}:{particleEntity.Id}:particle:{sample.Index}";
        }

        int cellX = (int)MathF.Floor(sample.Position.X / mergeDistance);
        int cellZ = (int)MathF.Floor(sample.Position.Z / mergeDistance);
        return $"{waterEntity.Id}:{particleEntity.Id}:particle:{cellX}:{cellZ}";
    }

    private bool TryGetColliderApproximation(GameEntity entity, ColliderSettings collider, out Vector3 center, out float radius)
    {
        center = default;
        radius = 0.0f;
        if (!TryCreateColliderGeometry(entity, collider, out ColliderGeometry geometry))
        {
            return false;
        }

        if (geometry.Shape == "box")
        {
            center = geometry.Box.Center;
            radius = geometry.Box.HalfExtents.Length();
            return true;
        }

        center = geometry.Capsule.Center;
        radius = geometry.Capsule.Radius + (Vector3.Distance(geometry.Capsule.Start, geometry.Capsule.End) * 0.5f);
        return true;
    }

    private EditorPmxObject? FindPmxRuntime(GameEntity entity)
    {
        return _pmxObjects.FirstOrDefault(item => ReferenceEquals(item.Entity, entity));
    }

    private static Matrix4x4 CreateEntityWorld(GameEntity entity)
    {
        return Matrix4x4.CreateScale(entity.Transform.Scale.ToVector3())
            * Matrix4x4.CreateFromQuaternion(ToQuaternion(entity.Transform.RotationDegrees.ToVector3()))
            * Matrix4x4.CreateTranslation(entity.Transform.Position.ToVector3());
    }

    private void TryLoadAudioRuntime(AudioAsset audioAsset)
    {
        if (Audio is null)
        {
            UpdateStatus("Audio is unavailable on this machine.");
            return;
        }

        PruneAudioRuntime();
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

        AudioClip? clip = null;
        AudioSource? source = null;
        bool registered = false;

        try
        {
            clip = Audio.LoadClip(fullPath);
            source = Audio.CreateSource(clip);
            source.Volume = audioAsset.Volume;
            source.Looping = audioAsset.Loop;
            RegisterAudioRuntime(audioAsset.Path, clip, source);
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

        foreach (EditorPlaneObject item in _planeObjects.ToArray())
        {
            RemoveComponent(item.Component);
        }

        _planeObjects.Clear();
        _waterRippleTimes.Clear();

        DisposeAudioRuntime();
        InvalidateDebugDraw();
    }

    private void InvalidateDebugDraw()
    {
        unchecked
        {
            _debugDrawVersion++;
        }
    }

    private void RegisterAudioRuntime(string path, AudioClip clip, AudioSource source)
    {
        HashSet<AudioSource> replacedSources = [];
        HashSet<AudioClip> replacedClips = [];

        if (_audioSources.TryGetValue(path, out AudioSource? replacedSource) && !ReferenceEquals(replacedSource, source))
        {
            replacedSources.Add(replacedSource);
        }

        if (_audioClips.TryGetValue(path, out AudioClip? replacedClip) && !ReferenceEquals(replacedClip, clip))
        {
            replacedClips.Add(replacedClip);
        }

        _audioSources[path] = source;
        _audioClips[path] = clip;
        DisposeUnreferencedAudio(replacedSources, replacedClips);
    }

    private void PruneAudioRuntime()
    {
        HashSet<string> activePaths = Project.Scene.Audio
            .Select(audio => audio.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string stalePath in _audioSources.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            AudioSource source = _audioSources[stalePath];
            _audioSources.Remove(stalePath);
            if (!_audioSources.Values.Any(item => ReferenceEquals(item, source)))
            {
                source.Dispose();
            }
        }

        foreach (string stalePath in _audioClips.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            AudioClip clip = _audioClips[stalePath];
            _audioClips.Remove(stalePath);
            if (!_audioClips.Values.Any(item => ReferenceEquals(item, clip)))
            {
                clip.Dispose();
            }
        }
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

    private void RemoveWaterRippleEntries(string entityId)
    {
        foreach (string rippleKey in _waterRippleTimes.Keys
            .Where(key => key.StartsWith($"{entityId}:", StringComparison.OrdinalIgnoreCase)
                || key.Contains($":{entityId}:", StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            _waterRippleTimes.Remove(rippleKey);
        }
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

    private ScriptValidationSummary PrepareAndValidateScripts()
    {
        List<ScriptValidationResult> results = [];
        foreach (ScriptBinding binding in Project.Scene.LoadingScripts)
        {
            results.Add(PrepareScriptBinding(binding, "scene loading script"));
        }

        foreach (GameEntity entity in Project.Scene.Entities)
        {
            foreach (ScriptBinding binding in entity.Scripts)
            {
                results.Add(PrepareScriptBinding(binding, $"entity '{entity.Name}' script"));
            }
        }

        int errors = results.Count(result => !result.Success);
        int warnings = results.Count(result => result.Success && !string.IsNullOrWhiteSpace(result.Message));
        string message = errors > 0
            ? $"Script check failed: {errors} error(s), {warnings} warning(s).\n{string.Join('\n', results.Where(result => !result.Success).Take(5).Select(result => result.Message))}"
            : $"Script check passed: {results.Count} script(s).";
        return new ScriptValidationSummary(errors > 0, message);
    }

    private ScriptValidationResult PrepareScriptBinding(ScriptBinding binding, string context)
    {
        try
        {
            NormalizeScriptLanguageAndPath(binding);
            CopyExternalScriptIntoProject(binding);
            EnsureScriptTemplateForBinding(binding);
            return ValidateScriptBinding(binding, context);
        }
        catch (Exception ex)
        {
            return ScriptValidationResult.Error($"{context}: {ex.Message}");
        }
    }

    private void NormalizeScriptLanguageAndPath(ScriptBinding binding)
    {
        binding.Language = NormalizeScriptLanguage(binding.Language, binding.Path);
        if (string.IsNullOrWhiteSpace(binding.Path))
        {
            string extension = binding.Language == "python" ? ".py" : ".csx";
            binding.Path = $"scripts/script_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
        }
    }

    private void CopyExternalScriptIntoProject(ScriptBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Path))
        {
            return;
        }

        string originalPath = binding.Path.Trim().Trim('"');
        if (originalPath.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fullPath = ResolveScriptPath(originalPath);
        if (!File.Exists(fullPath))
        {
            return;
        }

        if (IsPathInsideDirectory(fullPath, ProjectDirectory))
        {
            return;
        }

        string targetDirectory = Path.Combine(ProjectDirectory, "scripts");
        Directory.CreateDirectory(targetDirectory);
        string targetPath = MakeUniqueFilePath(targetDirectory, Path.GetFileName(fullPath));
        File.Copy(fullPath, targetPath);
        binding.Path = GameProjectPath.ToProjectRelative(ProjectDirectory, targetPath);
    }

    private void EnsureScriptTemplateForBinding(ScriptBinding binding)
    {
        if (Path.IsPathRooted(binding.Path)
            || binding.Path.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsLoadingScriptTemplate(binding))
        {
            EnsureLoadingScriptTemplate(binding);
        }
        else
        {
            EnsureCleanEntityScriptTemplate(binding);
        }
    }

    private ScriptValidationResult ValidateScriptBinding(ScriptBinding binding, string context)
    {
        if (!binding.Enabled)
        {
            return ScriptValidationResult.Ok($"{context}: disabled.");
        }

        string fullPath = GameProjectPath.ToAbsolute(ProjectDirectory, binding.Path);
        if (!File.Exists(fullPath))
        {
            return ScriptValidationResult.Error($"{context}: script file not found: {fullPath}");
        }

        string language = NormalizeScriptLanguage(binding.Language, binding.Path);
        binding.Language = language;
        return language == "python"
            ? ValidatePythonScript(fullPath, context)
            : ValidateCSharpScript(fullPath, context);
    }

    private static ScriptValidationResult ValidateCSharpScript(string fullPath, string context)
    {
        string source = File.ReadAllText(fullPath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(kind: SourceCodeKind.Script),
            fullPath);
        Diagnostic? error = tree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (error is null)
        {
            return ScriptValidationResult.Ok($"{context}: C# syntax OK.");
        }

        FileLinePositionSpan span = error.Location.GetLineSpan();
        return ScriptValidationResult.Error($"{context}: C# syntax error {Path.GetFileName(fullPath)}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1} {error.GetMessage()}");
    }

    private static ScriptValidationResult ValidatePythonScript(string fullPath, string context)
    {
        try
        {
            string source = File.ReadAllText(fullPath);
            PythonSyntaxChecker.Check(source, fullPath);
            return ScriptValidationResult.Ok($"{context}: Python syntax OK.");
        }
        catch (Exception ex)
        {
            return ScriptValidationResult.Error($"{context}: Python syntax error {ex.Message}");
        }
    }

    private string ResolveScriptPath(string path)
    {
        string normalized = path.Trim().Trim('"');
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        }

        string projectRelative = GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        if (File.Exists(projectRelative))
        {
            return projectRelative;
        }

        return Path.GetFullPath(normalized);
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        string fullPath = Path.GetFullPath(path);
        string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeUniqueFilePath(string directory, string fileName)
    {
        string candidate = Path.Combine(directory, string.IsNullOrWhiteSpace(fileName) ? "script.csx" : fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string stem = Path.GetFileNameWithoutExtension(candidate);
        string extension = Path.GetExtension(candidate);
        string parent = Path.GetDirectoryName(candidate) ?? directory;
        for (int i = 1; ; i++)
        {
            string next = Path.Combine(parent, $"{stem}_{i}{extension}");
            if (!File.Exists(next))
            {
                return next;
            }
        }
    }

    private static bool IsLoadingScriptTemplate(ScriptBinding binding)
    {
        string path = (binding.Path ?? string.Empty).Replace('\\', '/');
        return path.Contains("scene_loading", StringComparison.OrdinalIgnoreCase)
            || path.Contains("loading", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeScriptLanguage(string language, string path)
    {
        string normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "python" or "py")
        {
            return "python";
        }

        if (normalized is "csharp" or "cs" or "csx")
        {
            return "csharp";
        }

        return string.Equals(Path.GetExtension(path), ".py", StringComparison.OrdinalIgnoreCase)
            ? "python"
            : "csharp";
    }

    private readonly record struct ScriptValidationSummary(bool HasErrors, string Message);

    private readonly record struct ScriptValidationResult(bool Success, string Message)
    {
        public static ScriptValidationResult Ok(string message) => new(true, message);

        public static ScriptValidationResult Error(string message) => new(false, message);

        public string ToStatusMessage(string prefix)
        {
            return string.IsNullOrWhiteSpace(Message)
                ? prefix
                : $"{prefix}\n{Message}";
        }
    }

    private static class PythonSyntaxChecker
    {
        public static void Check(string source, string filePath)
        {
            string? python = ResolvePythonExecutable();
            if (string.IsNullOrWhiteSpace(python))
            {
                throw new InvalidOperationException("python executable was not found in PATH.");
            }

            string tempPath = Path.Combine(Path.GetTempPath(), $"dw_python_syntax_{Guid.NewGuid():N}.py");
            try
            {
                File.WriteAllText(tempPath, source);
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    }
                };

                process.StartInfo.ArgumentList.Add("-m");
                process.StartInfo.ArgumentList.Add("py_compile");
                process.StartInfo.ArgumentList.Add(tempPath);
                if (!process.Start())
                {
                    throw new InvalidOperationException("failed to start python syntax checker.");
                }

                string stderr = process.StandardError.ReadToEnd();
                string stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("python syntax checker timed out.");
                }

                if (process.ExitCode != 0)
                {
                    string message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new InvalidOperationException($"{Path.GetFileName(filePath)}: {NormalizePythonError(message)}");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }
            }
        }

        private static string? ResolvePythonExecutable()
        {
            string[] candidates = OperatingSystem.IsWindows()
                ? ["python.exe", "py.exe"]
                : ["python3", "python"];
            foreach (string candidate in candidates)
            {
                if (CanStart(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool CanStart(string fileName)
        {
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    }
                };
                process.StartInfo.ArgumentList.Add("--version");
                if (!process.Start())
                {
                    return false;
                }

                process.WaitForExit(3000);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizePythonError(string message)
        {
            return string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()));
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
              # Called by Zhengyan.DigitalWife.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
                  # entity.bind_relation("body", bind_component_transform=True, bind_lighting=False)
                  pass

              def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
                  if event_name == "clicked":
                      entity.speak("按钮被点击了")

              def update(entity, scene, input, audio, delta_seconds):
                  pass
              """
            : """
              // Called by Zhengyan.DigitalWife.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds, GuiControlId, GuiControlName.
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
              # Called by Zhengyan.DigitalWife.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0)
                  # entity.bind_relation("body", bind_component_transform=True, bind_lighting=False)
                  pass

              def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
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
              // Called by Zhengyan.DigitalWife.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds, GuiControlId, GuiControlName.
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
              # Called by Zhengyan.DigitalWife.GamePlayer.
              def start(entity, scene, input, audio):
                  # entity.speak("你好，我是小雨", speaker_id=0, speed=1.0, volume=1.0, on_completed="after_speak")
                  pass

              def after_speak(entity, scene, input, audio):
                  print("speech completed")

              def gui_event(entity, scene, input, audio, control_id, control_name, event_name):
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
              // Called by Zhengyan.DigitalWife.GamePlayer.
              // Available globals: Entity, Scene, Input, Audio, DeltaSeconds, GuiControlId, GuiControlName.
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
            Runtime = new GameRuntimeSettings
            {
                UseOpenCL = true
            },
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
        string sourceFullPath = ResolveAssetSourcePath(sourcePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"PMX file not found: {sourceFullPath}", sourceFullPath);
        }

        if (IsPathInsideDirectory(sourceFullPath, ProjectDirectory))
        {
            return GameProjectPath.ToProjectRelative(ProjectDirectory, sourceFullPath);
        }

        string sourceDirectory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException($"Cannot resolve PMX directory: {sourceFullPath}");
        string sourceFileName = Path.GetFileName(sourceFullPath);
        string modelDirectoryName = SafeFileStem(Path.GetFileNameWithoutExtension(sourceFullPath));
        string modelsRoot = Path.Combine(ProjectDirectory, "assets", "models");
        Directory.CreateDirectory(modelsRoot);

        foreach (string candidateDirectory in Directory.EnumerateDirectories(modelsRoot))
        {
            string candidatePmxPath = Path.Combine(candidateDirectory, sourceFileName);
            if (File.Exists(candidatePmxPath) && AreDirectoriesEquivalent(sourceDirectory, candidateDirectory))
            {
                return GameProjectPath.ToProjectRelative(ProjectDirectory, candidatePmxPath);
            }
        }

        string preferredTargetDirectory = Path.Combine(modelsRoot, modelDirectoryName);
        string targetDirectory = MakeUniqueDirectory(preferredTargetDirectory);
        CopyDirectoryContents(sourceDirectory, targetDirectory);

        string targetPmxPath = Path.Combine(targetDirectory, sourceFileName);
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

    private string ResolveAssetSourcePath(string sourcePath)
    {
        string normalized = GameProjectPath.NormalizePathText(sourcePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Asset path cannot be empty.", nameof(sourcePath));
        }

        if (IsProjectAssetsRelativePath(normalized)
            || normalized.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(normalized))
        {
            return GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        }

        string projectCandidate = GameProjectPath.ToAbsolute(ProjectDirectory, normalized);
        if (File.Exists(projectCandidate) || Directory.Exists(projectCandidate))
        {
            return projectCandidate;
        }

        return NormalizeInputPath(normalized);
    }

    private static bool IsProjectAssetsRelativePath(string path)
    {
        return path.Replace('\\', '/').StartsWith("assets/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreDirectoriesEquivalent(string firstDirectory, string secondDirectory)
    {
        string[] firstFiles = Directory.EnumerateFiles(firstDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(firstDirectory, file).Replace('\\', '/'))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] secondFiles = Directory.EnumerateFiles(secondDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(secondDirectory, file).Replace('\\', '/'))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!firstFiles.SequenceEqual(secondFiles, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string relativePath in firstFiles)
        {
            string firstPath = Path.Combine(firstDirectory, relativePath);
            string secondPath = Path.Combine(secondDirectory, relativePath);
            if (!AreFilesEquivalent(firstPath, secondPath))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreFilesEquivalent(string firstPath, string secondPath)
    {
        FileInfo first = new(firstPath);
        FileInfo second = new(secondPath);
        if (first.Length != second.Length)
        {
            return false;
        }

        const int bufferSize = 81920;
        using FileStream firstStream = File.OpenRead(first.FullName);
        using FileStream secondStream = File.OpenRead(second.FullName);
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];

        while (true)
        {
            int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            for (int i = 0; i < firstRead; i++)
            {
                if (firstBuffer[i] != secondBuffer[i])
                {
                    return false;
                }
            }
        }
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
