using System.Numerics;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidVulkanGame : Game
{
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
    private SkyboxComponent? _skybox;

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
    }

    protected override void Update(GameTime gameTime)
    {
        _ = gameTime;
        SyncCamera();
        SyncSceneComponents();
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shadowRenderer is null || _localLightShadowRenderer is null || _models.Count == 0)
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
    }

    protected override void UnloadContent()
    {
        _shadowRenderer?.Dispose();
        _shadowRenderer = null;
        _localLightShadowRenderer?.Dispose();
        _localLightShadowRenderer = null;
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
            AddComponent(new AndroidVulkanSpriteComponent(
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
        CameraSettings settings = camera.Settings;
        _camera.Width = Math.Max(GraphicsDevice.BackBufferSize.X, 1);
        _camera.Height = Math.Max(GraphicsDevice.BackBufferSize.Y, 1);
        _camera.SetLookAt(settings.Position.ToVector3(), settings.Target.ToVector3(),
            settings.VmdHasUp ? settings.VmdUp.ToVector3() : Vector3.UnitY);
        _camera.Fov = Math.Clamp(settings.Fov, 1.0f, 90.0f);
        _camera.NearClipPlane = settings.NearClipPlane;
        _camera.FarClipPlane = settings.FarClipPlane;
        _camera.ProjectionMode = string.Equals(settings.ProjectionMode, "orthographic", StringComparison.OrdinalIgnoreCase)
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        _camera.OrthographicSize = settings.OrthographicSize;
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
