using System.Numerics;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidVulkanGame : Game, IRuntimeTextureProvider
{
    private sealed class RenderTextureState
    {
        public required IRenderTarget Target { get; init; }
        public OrbitCamera Camera { get; } = new();
        public double LastRenderedSeconds { get; set; } = double.NegativeInfinity;
        public bool RefreshRequested { get; set; }
    }

    private readonly RuntimeScene _scene;
    private readonly string _projectDirectory;
    private readonly GameWindowSettings _windowSettings;
    private readonly OrbitCamera _camera = new();
    private readonly Dictionary<string, PmxModelComponent> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ParticleSystemComponent> _particles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WaterSurfaceComponent> _waters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TexturedPlaneComponent> _planes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PointLightData> _pointLights = [];
    private readonly List<SpotLightData> _spotLights = [];
    private ShadowMapRenderer? _shadowRenderer;
    private LocalLightShadowRenderer? _localLightShadowRenderer;
    private PlanarReflectionRenderer? _planarReflectionRenderer;
    private IUnderwaterPostProcessRenderer? _underwaterPostProcessRenderer;
    private SkyboxComponent? _skybox;
    private AndroidVulkanSpriteComponent? _spriteComponent;
    private IImGuiBackendController? _imgui;
    private DrawableGameComponent? _imguiComponent;
    private readonly Dictionary<string, RenderTextureState> _renderTextures = new(StringComparer.OrdinalIgnoreCase);
    private bool _renderingRenderTexture;
    private bool _sceneRenderedThisFrame;

    public AndroidVulkanGame(
        GameProject project,
        RuntimeScene scene,
        string projectDirectory,
        VulkanRenderer renderer,
        Vector2D<int> backBufferSize)
        : base(CreateOptions(project, backBufferSize), renderer, backBufferSize)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _projectDirectory = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
        _windowSettings = project.Window;
    }

    protected override void Initialize()
    {
        SyncCamera();
        LoadSceneComponents();
        _shadowRenderer = new ShadowMapRenderer(this)
        {
            Resolution = Math.Clamp(Options.Samples > 1 ? 1536 : 1024, 256, 2048)
        };
        _localLightShadowRenderer = new LocalLightShadowRenderer(this)
        {
            Resolution = 2048
        };
        _planarReflectionRenderer = new PlanarReflectionRenderer(this);
        _underwaterPostProcessRenderer = GraphicsDevice.Renderer.Services
            .CreateUnderwaterPostProcessRenderer("AndroidVulkanUnderwater");
        _imgui = GraphicsDevice.Renderer.Services.CreateImGuiController(this);
        _imguiComponent = AddComponent(new ImGuiDrawComponent(this)
        {
            DrawOrder = int.MaxValue
        });
    }

    protected override void Update(GameTime gameTime)
    {
        _ = gameTime;
        SyncCamera();
        SyncSceneComponents();
        _imgui?.Update((float)Math.Max(gameTime.ElapsedSeconds, 1.0 / 1000.0));
    }

    protected override void Draw(GameTime gameTime)
    {
        _sceneRenderedThisFrame = false;
        if (_shadowRenderer is null || _localLightShadowRenderer is null)
        {
            return;
        }

        LightingSettings lighting = _scene.Definition.Lighting;
        _shadowRenderer.Render(
            gameTime,
            _models.Values.ToArray(),
            _particles.Values.ToArray(),
            _planes.Values.ToArray(),
            lighting.LightDirection.ToVector3(),
            lighting.ShadowColor.ToVector4(),
            Math.Max(GraphicsDevice.BackBufferSize.X, 1),
            Math.Max(GraphicsDevice.BackBufferSize.Y, 1),
            GraphicsDevice.RestoreBackBuffer);
        _localLightShadowRenderer.Render(
            gameTime,
            _models.Values.ToArray(),
            _particles.Values.ToArray(),
            _pointLights,
            _spotLights,
            lighting.ShadowColor.W,
            GraphicsDevice.RestoreBackBuffer);
        ShadowMapBinding? binding = _shadowRenderer.CurrentBinding;
        foreach (PmxModelComponent model in _models.Values)
        {
            model.ShadowMap = binding;
            model.LocalLightShadowMap = _localLightShadowRenderer.CurrentBinding;
        }
        foreach (TexturedPlaneComponent plane in _planes.Values) plane.ShadowMap = binding;

        _planarReflectionRenderer?.RenderAll(
            gameTime,
            _camera,
            _waters.Values.ToArray(),
            _planes.Values.ToArray(),
            _spriteComponent is null ? [] : [_spriteComponent],
            ApplyCameraToComponents,
            RestoreMainCameraOnComponents,
            lighting.ClearColor.ToVector4(),
            Math.Max(GraphicsDevice.BackBufferSize.X, 1),
            Math.Max(GraphicsDevice.BackBufferSize.Y, 1));

        RenderSceneTextures(gameTime);

        if (TryDrawUnderwater(gameTime))
        {
            _sceneRenderedThisFrame = true;
        }
    }

    protected override void UnloadContent()
    {
        _shadowRenderer?.Dispose();
        _shadowRenderer = null;
        _localLightShadowRenderer?.Dispose();
        _localLightShadowRenderer = null;
        _planarReflectionRenderer?.Dispose();
        _planarReflectionRenderer = null;
        _underwaterPostProcessRenderer?.Dispose();
        _underwaterPostProcessRenderer = null;
        _imgui?.Dispose();
        _imgui = null;
        foreach (RenderTextureState state in _renderTextures.Values)
        {
            state.Target.Dispose();
        }
        _renderTextures.Clear();
    }

    public bool TrySetMotionLayerState(
        string entityIdOrName,
        int layerIndex,
        float? frame,
        bool? playing,
        bool? loop,
        float? playbackSpeed,
        float? weight)
    {
        PmxModelComponent? model = FindModel(entityIdOrName);
        if (model is null || (uint)layerIndex >= (uint)model.MotionLayerCount)
        {
            return false;
        }

        MotionLayerInfo layer = model.GetMotionLayers()[layerIndex];
        if (frame.HasValue) model.TrySetMotionLayerFrame(layer.MotionPath, frame.Value);
        if (playing.HasValue) model.TrySetMotionLayerPlaying(layer.MotionPath, playing.Value);
        if (weight.HasValue) model.TrySetMotionLayerWeight(layer.MotionPath, weight.Value);
        if (loop.HasValue) model.LoopMotion = loop.Value;
        if (playbackSpeed.HasValue) model.PlaybackSpeed = Math.Max(playbackSpeed.Value, 0.0f);
        return true;
    }

    public bool TrySetMotionState(string entityIdOrName, float? frame, bool? playing)
    {
        PmxModelComponent? model = FindModel(entityIdOrName);
        if (model is null)
        {
            return false;
        }

        foreach (MotionLayerInfo layer in model.GetMotionLayers())
        {
            if (frame.HasValue) model.TrySetMotionLayerFrame(layer.MotionPath, frame.Value);
            if (playing.HasValue) model.TrySetMotionLayerPlaying(layer.MotionPath, playing.Value);
        }
        return true;
    }

    public bool TryResetPhysics(string entityIdOrName)
    {
        PmxModelComponent? model = FindModel(entityIdOrName);
        if (model is null)
        {
            return false;
        }

        model.ResetPhysics();
        return true;
    }

    public void ApplyMotion(RuntimeEntity entity, string motionPath)
    {
        PmxModelComponent? model = FindModel(entity.Id);
        if (model is null)
        {
            return;
        }

        model.SetMotionLayers(
        [
            new MotionLayerDefinition(GameProjectPath.ToAbsolute(_projectDirectory, motionPath), 1.0f)
        ]);
        model.IsPlaying = true;
    }

    private void LoadSceneComponents()
    {
        foreach (RuntimeEntity entity in _scene.Entities)
        {
            if (entity.IsPmxModel) LoadPmx(entity);
            else if (entity.IsParticleSystem) LoadParticle(entity);
            else if (entity.IsWaterSurface) LoadWater(entity);
            else if (entity.IsTexturedPlane) LoadPlane(entity);
        }

        SkyboxSettings skybox = _scene.Definition.Skybox;
        if (skybox.Enabled)
        {
            string path = ResolvePath(skybox.TexturePath);
            if (File.Exists(path))
            {
                _skybox = AddComponent(new SkyboxComponent(_camera, path)
                {
                    Exposure = skybox.Exposure,
                    Tint = skybox.Tint.ToVector3(),
                    DrawOrder = -10000
                });
            }
        }

        if (_scene.Definition.Sprites.Count != 0)
        {
            _spriteComponent = AddComponent(new AndroidVulkanSpriteComponent(
                _scene.Definition,
                _windowSettings,
                ResolvePath)
            {
                DrawOrder = 10000
            });
        }
    }

    private void LoadPmx(RuntimeEntity entity)
    {
        string path = ResolvePath(entity.Definition.AssetPath);
        if (!File.Exists(path))
        {
            return;
        }

        PmxModelComponent model = AddComponent(new PmxModelComponent(path)
        {
            Camera = _camera,
            RuntimeTextureProvider = this,
            DrawOrder = 100
        });
        ApplyEntity(entity, model, includeMotions: true);
        _models[entity.Id] = model;
    }

    private void LoadParticle(RuntimeEntity entity)
    {
        ParticleSystemSettings settings = ToParticleSettings(entity.Definition.Particle);
        if (!string.IsNullOrWhiteSpace(settings.TexturePath)) settings.TexturePath = ResolvePath(settings.TexturePath);
        ParticleSystemComponent component = AddComponent(new ParticleSystemComponent(_camera, settings)
        {
            DrawOrder = 130
        });
        component.RuntimeTextureProvider = this;
        ApplyEntity(entity, component, resetParticles: false);
        _particles[entity.Id] = component;
    }

    private void LoadWater(RuntimeEntity entity)
    {
        WaterSurfaceSettings settings = entity.Definition.Water;
        WaterSurfaceComponent component = AddComponent(new WaterSurfaceComponent(
            _camera,
            Math.Max(settings.Size, 0.1f),
            meshResolution: Math.Clamp(settings.GerstnerMeshResolution, 8, 256))
        {
            DrawOrder = 120
        });
        ApplyEntity(entity, component);
        _waters[entity.Id] = component;
    }

    private void LoadPlane(RuntimeEntity entity)
    {
        TexturedPlaneComponent component = AddComponent(new TexturedPlaneComponent(_camera, ResolvePlaneTexture(entity))
        {
            DrawOrder = 115
        });
        component.RuntimeTextureProvider = this;
        ApplyEntity(entity, component);
        _planes[entity.Id] = component;
    }

    private void SyncSceneComponents()
    {
        foreach (RuntimeEntity entity in _scene.Entities)
        {
            if (_models.TryGetValue(entity.Id, out PmxModelComponent? model)) ApplyEntity(entity, model, includeMotions: false);
            if (_particles.TryGetValue(entity.Id, out ParticleSystemComponent? particle)) ApplyEntity(entity, particle, resetParticles: false);
            if (_waters.TryGetValue(entity.Id, out WaterSurfaceComponent? water)) ApplyEntity(entity, water);
            if (_planes.TryGetValue(entity.Id, out TexturedPlaneComponent? plane)) ApplyEntity(entity, plane);
        }

        RefreshLights();
        LightingSettings lighting = _scene.Definition.Lighting;
        GraphicsDevice.ClearColor = lighting.ClearColor.ToVector4();
        foreach (PmxModelComponent model in _models.Values)
        {
            model.LightColor = lighting.LightColor.ToVector3();
            model.LightDirection = lighting.LightDirection.ToVector3();
            model.AmbientLightColor = lighting.AmbientColor.ToVector3();
            model.AmbientLightStrength = lighting.AmbientStrength;
            model.ShadowColor = lighting.ShadowColor.ToVector4();
            model.PointLights = _pointLights;
            model.SpotLights = _spotLights;
        }
    }

    private void SyncCamera()
    {
        RuntimeCamera camera = _scene.MainCamera;
        _camera.Width = Math.Max(GraphicsDevice.BackBufferSize.X, 1);
        _camera.Height = Math.Max(GraphicsDevice.BackBufferSize.Y, 1);
        ApplyCameraSettings(_camera, camera.Settings, _camera.Width, _camera.Height);
    }

    public bool RequestRenderTextureRefresh(string idOrName)
    {
        RenderTextureSettings? settings = FindRenderTextureSettings(idOrName);
        if (settings is null) return false;
        RenderTextureState state = GetOrCreateRenderTexture(settings.Name);
        state.RefreshRequested = true;
        return true;
    }

    public bool ConfigureRenderTexture(string idOrName, string refreshMode, float intervalSeconds)
    {
        RenderTextureSettings? settings = FindRenderTextureSettings(idOrName);
        if (settings is null) return false;
        string normalized = NormalizeRefreshMode(refreshMode);
        settings.RefreshMode = normalized;
        settings.RefreshIntervalSeconds = Math.Max(intervalSeconds, 0.01f);
        RenderTextureState state = GetOrCreateRenderTexture(settings.Name);
        state.RefreshRequested = true;
        return true;
    }

    public AndroidRenderTextureInfo? GetRenderTexture(string idOrName)
    {
        RenderTextureSettings? settings = FindRenderTextureSettings(idOrName);
        if (settings is null || string.IsNullOrWhiteSpace(settings.Name)) return null;
        if (!_renderTextures.TryGetValue(settings.Name, out RenderTextureState? state)) return null;
        return ToRenderTextureInfo(settings, state);
    }

    public IReadOnlyList<AndroidRenderTextureInfo> GetRenderTextures()
    {
        List<AndroidRenderTextureInfo> result = [];
        foreach (RenderTextureSettings settings in _scene.Definition.RenderTextures.Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Name)))
        {
            if (_renderTextures.TryGetValue(settings.Name, out RenderTextureState? state))
            {
                result.Add(ToRenderTextureInfo(settings, state));
            }
        }
        return result;
    }

    public bool TryGetTexture(string textureReference, out uint textureId)
    {
        textureId = 0;
        return false;
    }

    public bool TryGetTextureHandle(string textureReference, out RuntimeTextureHandle handle)
    {
        string name = NormalizeRenderTextureName(textureReference);
        if (!string.IsNullOrWhiteSpace(name)
            && _renderTextures.TryGetValue(name, out RenderTextureState? state)
            && state.Target.NativeColorResource is not null)
        {
            handle = new RuntimeTextureHandle(
                state.Target.Backend,
                state.Target.LegacyColorTextureId,
                state.Target.NativeColorResource);
            return handle.IsValid;
        }

        handle = default;
        return false;
    }

    private void RenderSceneTextures(GameTime gameTime)
    {
        if (_renderingRenderTexture || _scene.Definition.RenderTextures.Count == 0)
        {
            return;
        }

        _renderingRenderTexture = true;
        try
        {
            HashSet<string> validNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (RenderTextureSettings settings in _scene.Definition.RenderTextures.Where(item => item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(settings.Name)) continue;
                validNames.Add(settings.Name);
                RenderTextureState state = GetOrCreateRenderTexture(settings.Name);
                state.Target.EnsureSize(Math.Max(settings.Width, 1), Math.Max(settings.Height, 1));
                if (!ShouldRenderRenderTexture(settings, state, gameTime.TotalSeconds)) continue;

                RuntimeCamera camera = _scene.GetCamera(settings.Camera) ?? _scene.MainCamera;
                state.Camera.Width = state.Target.Width;
                state.Camera.Height = state.Target.Height;
                ApplyCameraSettings(state.Camera, camera.Settings, state.Target.Width, state.Target.Height);
                state.Target.BeginPass(settings.ClearColor.ToVector4());
                try
                {
                    DrawSceneComponentsWithCamera(gameTime, state.Camera);
                    state.LastRenderedSeconds = gameTime.TotalSeconds;
                    state.RefreshRequested = false;
                }
                finally
                {
                    state.Target.EndPass();
                    GraphicsDevice.RestoreBackBuffer();
                }
            }

            foreach (string staleName in _renderTextures.Keys.Where(name => !validNames.Contains(name)).ToArray())
            {
                _renderTextures[staleName].Target.Dispose();
                _renderTextures.Remove(staleName);
            }
        }
        finally
        {
            GraphicsDevice.RestoreBackBuffer();
            _renderingRenderTexture = false;
        }
    }

    private void DrawSceneComponentsWithCamera(GameTime gameTime, OrbitCamera camera)
    {
        OrbitCamera? previousSkybox = _skybox?.Camera;
        OrbitCamera?[] previousModels = _models.Values.Select(model => model.Camera).ToArray();
        OrbitCamera[] previousParticles = _particles.Values.Select(particle => particle.Camera).ToArray();
        OrbitCamera[] previousWaters = _waters.Values.Select(water => water.Camera).ToArray();
        OrbitCamera[] previousPlanes = _planes.Values.Select(plane => plane.Camera).ToArray();
        try
        {
            ApplyCameraToComponents(camera);

            List<DrawableGameComponent> drawables = [];
            if (_skybox is not null) drawables.Add(_skybox);
            drawables.AddRange(_models.Values);
            drawables.AddRange(_planes.Values);
            drawables.AddRange(_waters.Values);
            drawables.AddRange(_particles.Values);
            foreach (DrawableGameComponent drawable in drawables.OrderBy(component => component.DrawOrder))
            {
                if (drawable.Visible) drawable.Draw(gameTime);
            }
        }
        finally
        {
            if (_skybox is not null && previousSkybox is not null) _skybox.Camera = previousSkybox;
            int modelIndex = 0;
            foreach (PmxModelComponent model in _models.Values) model.Camera = previousModels[modelIndex++];
            int particleIndex = 0;
            foreach (ParticleSystemComponent particle in _particles.Values) particle.Camera = previousParticles[particleIndex++];
            int waterIndex = 0;
            foreach (WaterSurfaceComponent water in _waters.Values) water.Camera = previousWaters[waterIndex++];
            int planeIndex = 0;
            foreach (TexturedPlaneComponent plane in _planes.Values) plane.Camera = previousPlanes[planeIndex++];
        }
    }

    private void ApplyCameraToComponents(OrbitCamera camera)
    {
        if (_skybox is not null) _skybox.Camera = camera;
        foreach (PmxModelComponent model in _models.Values) model.Camera = camera;
        foreach (ParticleSystemComponent particle in _particles.Values) particle.Camera = camera;
        foreach (WaterSurfaceComponent water in _waters.Values) water.Camera = camera;
        foreach (TexturedPlaneComponent plane in _planes.Values) plane.Camera = camera;
    }

    private void RestoreMainCameraOnComponents(OrbitCamera _)
    {
        ApplyCameraToComponents(_camera);
    }

    private bool TryDrawUnderwater(GameTime gameTime)
    {
        if (_underwaterPostProcessRenderer is null || !TryResolveUnderwaterSettings(_camera, out UnderwaterPostProcessSettings settings))
        {
            return false;
        }

        int width = Math.Max(GraphicsDevice.BackBufferSize.X, 1);
        int height = Math.Max(GraphicsDevice.BackBufferSize.Y, 1);
        _underwaterPostProcessRenderer.BeginCapture(width, height, GraphicsDevice.ClearColor);
        DrawSceneComponentsWithCamera(gameTime, _camera);
        GraphicsDevice.RestoreBackBuffer();
        GraphicsDevice.SetViewport(0, 0, width, height);
        GraphicsDevice.SetScissor(0, 0, width, height, enabled: false);
        _underwaterPostProcessRenderer.Draw(_camera, settings, gameTime.TotalSeconds, width, height);
        return true;
    }

    private bool TryResolveUnderwaterSettings(OrbitCamera camera, out UnderwaterPostProcessSettings settings)
    {
        settings = default;
        float nearestDepth = float.MaxValue;
        RuntimeEntity? activeWater = null;
        foreach ((string id, WaterSurfaceComponent component) in _waters)
        {
            RuntimeEntity? waterEntity = _scene.GetEntity(id);
            if (waterEntity is null || !waterEntity.Definition.Water.UnderwaterEffectEnabled
                || !component.Visible || !component.Enabled || !component.TryGetSurfaceHeight(camera.Position, out float surfaceHeight)
                || camera.Position.Y >= surfaceHeight + 0.03f)
            {
                continue;
            }

            float depth = Math.Max(surfaceHeight - camera.Position.Y, 0.001f);
            if (depth < nearestDepth)
            {
                nearestDepth = depth;
                activeWater = _scene.GetEntity(id);
            }
        }

        if (activeWater is null)
        {
            return false;
        }

        WaterSurfaceSettings water = activeWater.Definition.Water;
        settings = new UnderwaterPostProcessSettings(
            ClampVector3(water.UnderwaterTint.ToVector3(), 0.0f, 2.0f),
            ClampVector3(water.UnderwaterFogColor.ToVector3(), 0.0f, 2.0f),
            Math.Clamp(water.UnderwaterFogDensity, 0.0f, 4.0f),
            Math.Max(water.UnderwaterVisibilityDistance, 0.1f),
            Math.Clamp(water.UnderwaterDistortionStrength, 0.0f, 0.05f),
            Math.Clamp(water.UnderwaterCausticsStrength, 0.0f, 1.0f),
            Math.Clamp(water.UnderwaterBubbleStrength, 0.0f, 1.0f),
            nearestDepth);
        return true;
    }

    private static Vector3 ClampVector3(Vector3 value, float min, float max)
        => new(Math.Clamp(value.X, min, max), Math.Clamp(value.Y, min, max), Math.Clamp(value.Z, min, max));

    private RenderTextureState GetOrCreateRenderTexture(string name)
    {
        if (!_renderTextures.TryGetValue(name, out RenderTextureState? state))
        {
            state = new RenderTextureState
            {
                Target = GraphicsDevice.CreateRenderTarget($"AndroidVulkan-{name}"),
                RefreshRequested = true
            };
            _renderTextures[name] = state;
        }
        return state;
    }

    private RenderTextureSettings? FindRenderTextureSettings(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return _scene.Definition.RenderTextures.FirstOrDefault(settings =>
            string.Equals(settings.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(settings.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldRenderRenderTexture(RenderTextureSettings settings, RenderTextureState state, double nowSeconds)
    {
        if (state.RefreshRequested) return true;
        return NormalizeRefreshMode(settings.RefreshMode) switch
        {
            "on_demand" => double.IsNegativeInfinity(state.LastRenderedSeconds),
            "fixed_rate" => nowSeconds - state.LastRenderedSeconds >= Math.Max(settings.RefreshIntervalSeconds, 0.01f),
            _ => true
        };
    }

    private static AndroidRenderTextureInfo ToRenderTextureInfo(RenderTextureSettings settings, RenderTextureState state)
        => new(settings.Id, settings.Name, state.Target.Width, state.Target.Height, NormalizeRefreshMode(settings.RefreshMode),
            Math.Max(settings.RefreshIntervalSeconds, 0.01f), !double.IsNegativeInfinity(state.LastRenderedSeconds), state.LastRenderedSeconds);

    private static string NormalizeRenderTextureName(string textureReference)
    {
        string normalized = (textureReference ?? string.Empty).Trim();
        if (normalized.StartsWith("rt:", StringComparison.OrdinalIgnoreCase)) normalized = normalized[3..];
        return normalized.Trim();
    }

    private static string NormalizeRefreshMode(string refreshMode)
    {
        string normalized = (refreshMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "manual" or "on_demand" or "ondemand" => "on_demand",
            "fixed_rate" or "fixedrate" or "interval" => "fixed_rate",
            _ => "every_frame"
        };
    }

    private static void ApplyCameraSettings(OrbitCamera target, CameraSettings settings, int width, int height)
    {
        target.Width = Math.Max(width, 1);
        target.Height = Math.Max(height, 1);
        target.SetLookAt(settings.Position.ToVector3(), settings.Target.ToVector3(),
            settings.VmdHasUp ? settings.VmdUp.ToVector3() : Vector3.UnitY);
        target.Fov = Math.Clamp(settings.Fov, 1.0f, 90.0f);
        target.NearClipPlane = settings.NearClipPlane;
        target.FarClipPlane = settings.FarClipPlane;
        target.ProjectionMode = string.Equals(settings.ProjectionMode, "orthographic", StringComparison.OrdinalIgnoreCase)
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        target.OrthographicSize = settings.OrthographicSize;
    }

    private void RefreshLights()
    {
        _pointLights.Clear();
        _spotLights.Clear();
        foreach (RuntimeEntity light in _scene.PointLights)
        {
            _pointLights.Add(new PointLightData(
                light.Position, light.LightColor, light.LightIntensity, light.LightRange, light.Enabled, light.CastsShadows));
        }
        foreach (RuntimeEntity light in _scene.SpotLights)
        {
            _spotLights.Add(new SpotLightData(
                light.Position, light.SpotDirection, light.LightColor, light.LightIntensity, light.LightRange,
                light.SpotInnerConeAngleDegrees, light.SpotOuterConeAngleDegrees, light.Enabled, light.CastsShadows));
        }
    }

    private void ApplyEntity(RuntimeEntity entity, PmxModelComponent model, bool includeMotions)
    {
        GameEntity definition = entity.Definition;
        model.Position = entity.Position;
        model.Scale = entity.Scale;
        model.Rotation = ToQuaternion(entity.RotationDegrees);
        model.IsPlaying = definition.IsPlaying;
        model.PlaybackSpeed = definition.PlaybackSpeed;
        model.LoopMotion = definition.LoopMotion;
        model.EnablePhysical = definition.EnablePhysics;
        model.PhysicsGravity = definition.PhysicsGravity;
        model.ResetPhysicsOnMotionLoop = definition.ResetPhysicsOnMotionLoop;
        model.EnableEdge = definition.EnableEdge;
        model.EnableShadow = definition.EnableShadow;
        model.ReceiveShadow = definition.ReceiveShadow;
        model.ReceiveShadowMode = definition.ReceiveShadowMode;
        model.DrawShadowInMainPass = definition.DrawShadowInMainPass;
        if (includeMotions && definition.MotionLayers.Count != 0)
        {
            model.SetMotionLayers(definition.MotionLayers
                .Where(layer => !string.IsNullOrWhiteSpace(layer.Path))
                .Select(layer => new MotionLayerDefinition(
                    ResolvePath(layer.Path), layer.Weight, layer.ResetPhysicsOnLoop)));
        }
    }

    private static void ApplyEntity(RuntimeEntity entity, ParticleSystemComponent component, bool resetParticles)
    {
        component.Position = entity.Position;
        component.Enabled = entity.Definition.IsPlaying;
        component.Visible = entity.Definition.IsPlaying;
        component.SimulationSpeed = Math.Clamp(entity.Definition.Particle.SimulationSpeed, 0.0f, 10.0f);
        component.Opacity = Math.Clamp(entity.Definition.Particle.Opacity, 0.0f, 1.0f);
        if (resetParticles) component.ApplySettings(ToParticleSettings(entity.Definition.Particle), true);
    }

    private static void ApplyEntity(RuntimeEntity entity, WaterSurfaceComponent component)
    {
        WaterSurfaceSettings settings = entity.Definition.Water;
        component.Position = entity.Position;
        component.Scale = entity.Scale;
        component.Rotation = ToQuaternion(entity.RotationDegrees);
        component.Enabled = entity.Definition.IsPlaying;
        component.Visible = entity.Definition.IsPlaying;
        component.Alpha = settings.Alpha;
        component.AnimationSpeed = settings.AnimationSpeed;
        component.NormalTiling = Math.Max(settings.NormalTiling, 0.001f);
        component.GerstnerWavesEnabled = settings.GerstnerWavesEnabled;
        component.GerstnerWaveCount = settings.GerstnerWaveCount;
        component.GerstnerAmplitude = settings.GerstnerAmplitude;
        component.GerstnerWavelength = settings.GerstnerWavelength;
        component.GerstnerSpeed = settings.GerstnerSpeed;
        component.GerstnerSteepness = settings.GerstnerSteepness;
        component.GerstnerDirectionDegrees = settings.GerstnerDirectionDegrees;
        component.DeepColor = settings.DeepColor.ToVector3();
        component.ReflectionTint = settings.ReflectionTint.ToVector3();
        component.SkyReflectionStrength = settings.SkyReflectionStrength;
        component.MirrorReflectionEnabled = settings.MirrorReflectionEnabled;
        component.RippleLifetimeSeconds = Math.Max(0.05f, settings.RippleLifetimeSeconds);
        component.RippleWaveSpeed = settings.RippleWaveSpeed;
        component.RippleFrequency = Math.Max(0.0f, settings.RippleFrequency);
        component.RippleNormalStrength = Math.Max(0.0f, settings.RippleNormalStrength);
    }

    private void ApplyEntity(RuntimeEntity entity, TexturedPlaneComponent component)
    {
        TexturedPlaneSettings settings = entity.Definition.Plane;
        component.Position = entity.Position;
        component.Rotation = ToQuaternion(entity.RotationDegrees);
        component.Scale = entity.Scale;
        component.Visible = entity.Definition.IsPlaying;
        component.TexturePath = ResolvePlaneTexture(entity);
        component.Width = Math.Max(settings.Width, 0.001f);
        component.Height = Math.Max(settings.Height, 0.001f);
        component.Billboard = settings.Billboard;
        component.Tint = settings.Tint.ToVector4();
        component.Opacity = settings.Opacity;
        component.ReceiveShadow = settings.ReceiveShadow;
        component.MirrorReflectionEnabled = settings.MirrorReflectionEnabled;
        component.MirrorReflectionStrength = settings.MirrorReflectionStrength;
    }

    private PmxModelComponent? FindModel(string idOrName)
    {
        if (_models.TryGetValue(idOrName, out PmxModelComponent? model)) return model;
        RuntimeEntity? entity = _scene.GetEntity(idOrName);
        return entity is null ? null : _models.GetValueOrDefault(entity.Id);
    }

    private string ResolvePlaneTexture(RuntimeEntity entity)
    {
        string path = !string.IsNullOrWhiteSpace(entity.Definition.Plane.TexturePath)
            ? entity.Definition.Plane.TexturePath
            : entity.Definition.AssetPath;
        return ResolvePath(path);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = GameProjectPath.NormalizePathText(path);
        if (normalized.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(AndroidBundledResourceStore.RootDirectory))
        {
            string relative = normalized["app:".Length..].TrimStart('/', '\\');
            string bundledPath = Path.Combine(AndroidBundledResourceStore.RootDirectory, relative);
            if (File.Exists(bundledPath)) return bundledPath;
        }
        return GameProjectPath.ToAbsolute(_projectDirectory, normalized);
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        const float radians = MathF.PI / 180.0f;
        return Quaternion.CreateFromYawPitchRoll(degrees.Y * radians, degrees.X * radians, degrees.Z * radians);
    }

    private static ParticleSystemSettings ToParticleSettings(ParticleEntitySettings settings)
    {
        ParticleSystemSettings result = new()
        {
            Name = string.IsNullOrWhiteSpace(settings.Preset) ? "Particles" : settings.Preset,
            CastShadows = settings.CastShadows,
            ParticleCount = settings.ParticleCount,
            SpawnBoxHalfExtents = settings.SpawnBoxHalfExtents.ToVector3(),
            BaseVelocity = settings.BaseVelocity.ToVector3(),
            VelocityJitter = settings.VelocityJitter.ToVector3(),
            Acceleration = settings.Acceleration.ToVector3(),
            MinLifetime = settings.MinLifetime,
            MaxLifetime = settings.MaxLifetime,
            MinSize = settings.MinSize,
            MaxSize = settings.MaxSize,
            StartSizeScale = settings.StartSizeScale,
            EndSizeScale = settings.EndSizeScale,
            WidthScale = settings.WidthScale,
            HeightScale = settings.HeightScale,
            MinRotationSpeedRadians = settings.MinRotationSpeedRadians,
            MaxRotationSpeedRadians = settings.MaxRotationSpeedRadians,
            StartColor = settings.StartColor.ToVector4(),
            EndColor = settings.EndColor.ToVector4(),
            RandomizeInitialAge = settings.RandomizeInitialAge,
            BlendMode = string.Equals(settings.BlendMode, "additive", StringComparison.OrdinalIgnoreCase)
                ? ParticleBlendMode.Additive
                : ParticleBlendMode.Alpha,
            OrientationMode = string.Equals(settings.OrientationMode, "velocityAligned", StringComparison.OrdinalIgnoreCase)
                || string.Equals(settings.OrientationMode, "velocity_aligned", StringComparison.OrdinalIgnoreCase)
                    ? ParticleOrientationMode.VelocityAligned
                    : ParticleOrientationMode.Billboard,
            TexturePreset = settings.TexturePreset.Trim().ToLowerInvariant() switch
            {
                "streak" => ParticleTexturePreset.Streak,
                "flame" => ParticleTexturePreset.Flame,
                _ => ParticleTexturePreset.SoftCircle
            },
            TexturePath = string.IsNullOrWhiteSpace(settings.TexturePath) ? null : settings.TexturePath.Trim(),
            UseTextureColor = settings.UseTextureColor,
            PreventDarkening = settings.PreventDarkening
        };
        result.Validate();
        return result;
    }

    public override bool ShouldDrawComponent(DrawableGameComponent component)
    {
        if (!_sceneRenderedThisFrame)
        {
            return true;
        }

        // The underwater path renders world components into a capture target
        // and then composites it to the swapchain. Keep screen-space sprites on
        // top of that composite while suppressing the second world draw.
        return ReferenceEquals(component, _spriteComponent) || ReferenceEquals(component, _imguiComponent);
    }

    private sealed class ImGuiDrawComponent(AndroidVulkanGame owner) : DrawableGameComponent
    {
        public override void Draw(GameTime gameTime)
        {
            _ = gameTime;
            owner._imgui?.Render();
        }
    }

    private static GameOptions CreateOptions(GameProject project, Vector2D<int> size)
    {
        LightingSettings lighting = project.Scene.Lighting;
        return new GameOptions
        {
            GraphicsBackend = GraphicsBackend.Vulkan,
            WindowSize = size,
            Samples = project.Window.AntiAliasingSamples,
            VSync = true,
            ClearColor = lighting.ClearColor.ToVector4(),
            UseOpenCL = false,
            UseVulkanCompute = project.Runtime.UseVulkanCompute,
            EnableAudio = false,
            AnimationTimingMode = GameProjectTiming.NormalizeMode(project.Window.TimingMode) == GameProjectTiming.FrameRateDependent
                ? AnimationTimingMode.FrameRateDependent
                : AnimationTimingMode.TimeSynchronized
        };
    }
}
