using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeScene : IDisposable
{
    private readonly Dictionary<string, RuntimeEntity> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEntity> _entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeSceneAnimationController _animations;
    private readonly Func<string, bool>? _resetPmxPhysics;
    private readonly Func<string, ColliderSettings, RuntimeMeshCollider?>? _meshColliderResolver;
    private readonly Func<string, string, Matrix4x4?>? _nodeWorldResolver;
    private readonly Action<string, RuntimeMotionCommand>? _motionCommand;
    private readonly Action<string, RuntimeSpeechRequest>? _speechRequest;
    private bool _disposed;

    public RuntimeScene(
        string scenePath,
        GameProjectScene definition,
        Func<string, string> resolvePath,
        Func<string, bool>? resetPmxPhysics = null,
        Func<string, ColliderSettings, RuntimeMeshCollider?>? meshColliderResolver = null,
        Func<string, string, Matrix4x4?>? nodeWorldResolver = null,
        Func<string, VmdParsing?>? animationLoader = null,
        Action<string, RuntimeMotionCommand>? motionCommand = null,
        Action<string, RuntimeSpeechRequest>? speechRequest = null,
        Zhengyan.DigitalWife.Scripting.RuntimeGraphicsQuality? graphics = null)
    {
        ScenePath = scenePath;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        NormalizeCameras(definition);
        Cameras = definition.Cameras.Select(camera => new RuntimeCamera(camera)).ToArray();
        Lighting = new RuntimeLighting(definition.Lighting);
        Graphics = graphics ?? new();
        _animations = new RuntimeSceneAnimationController(resolvePath, animationLoader);
        _resetPmxPhysics = resetPmxPhysics;
        _meshColliderResolver = meshColliderResolver;
        _nodeWorldResolver = nodeWorldResolver;
        _motionCommand = motionCommand;
        _speechRequest = speechRequest;
        Physics = new RuntimeScenePhysics(() => Entities);
        Navigation = new RuntimeSceneNavigation(() => Entities);
        Debug = new RuntimeDebug();
        PointLights = new RuntimeLightCollection(this, spot: false);
        SpotLights = new RuntimeLightCollection(this, spot: true);
        foreach (GameEntity entity in definition.Entities) Register(CreateEntity(entity));
    }

    public event Action<RuntimeEntity>? EntityAdded;
    public event Action<RuntimeEntity>? EntityRemoving;

    public string ScenePath { get; }
    public string Name => Definition.Name;
    public GameProjectScene Definition { get; }
    public IReadOnlyList<RuntimeCamera> Cameras { get; }
    public RuntimeLighting Lighting { get; }
    public IEnumerable<RuntimeEntity> Entities => _entitiesById.Values;
    public IEnumerable<RuntimeEntity> PmxModels => Entities.Where(entity => entity.IsPmxModel);
    public RuntimeLightCollection SpotLights { get; }
    public RuntimeLightCollection PointLights { get; }
    public IEnumerable<RuntimeEntity> TexturedPlanes => Entities.Where(entity => entity.IsTexturedPlane);
    public IEnumerable<RuntimeEntity> ParticleSystems => Entities.Where(entity => entity.IsParticleSystem);
    public IEnumerable<RuntimeEntity> WaterSurfaces => Entities.Where(entity => entity.IsWaterSurface);
    public long EntityRevision { get; private set; }
    public RuntimeScenePhysics Physics { get; }
    public Zhengyan.DigitalWife.Scripting.RuntimeGraphicsQuality Graphics { get; }
    public RuntimeSceneNavigation Navigation { get; }
    public RuntimeDebug Debug { get; }

    public RuntimeCamera MainCamera => Cameras.FirstOrDefault(camera => camera.Enabled && camera.IsMain)
        ?? Cameras.FirstOrDefault(camera => camera.Enabled)
        ?? Cameras[0];

    public RuntimeCamera Camera => MainCamera;

    public IEnumerable<RuntimeGuiControl> GuiControls
        => Definition.GuiControls.Select(control => new RuntimeGuiControl(control));

    public RuntimeGuiControl? GetGuiControl(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        GuiControlSettings? control = Definition.GuiControls.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return control is null ? null : new RuntimeGuiControl(control);
    }

    public IEnumerable<RuntimeSpriteControl> Sprites
        => Definition.Sprites.Select(sprite => new RuntimeSpriteControl(sprite));

    public RuntimeSpriteControl? GetSprite(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        SpriteSettings? sprite = Definition.Sprites.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        return sprite is null ? null : new RuntimeSpriteControl(sprite);
    }

    public bool IsScreenPointOverGui(float x, float y)
    {
        const float actualWidth = 1280.0f;
        const float actualHeight = 720.0f;
        const float referenceWidth = 1280.0f;
        const float referenceHeight = 720.0f;
        return Definition.GuiControls.Any(control =>
        {
            if (!control.Visible) return false;
            LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y,
                control.Width, control.Height, actualWidth, actualHeight, referenceWidth, referenceHeight);
            return x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
        });
    }

    public IReadOnlyList<RuntimeCamera> RenderCameras
    {
        get
        {
            RuntimeCamera[] viewportCameras = Cameras.Where(camera => camera.Enabled && camera.Definition.Viewport.Enabled).ToArray();
            RuntimeCamera main = MainCamera;
            if (viewportCameras.Length == 0)
            {
                return [main];
            }

            List<RuntimeCamera> result = [main];
            result.AddRange(viewportCameras.Where(camera => !ReferenceEquals(camera, main)));
            return result;
        }
    }

    public RuntimeEntity? GetEntity(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return _entitiesById.TryGetValue(idOrName, out RuntimeEntity? byId)
            ? byId
            : _entitiesByName.GetValueOrDefault(idOrName);
    }

    public RuntimeCamera? GetCamera(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return Cameras.FirstOrDefault(camera =>
            string.Equals(camera.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(camera.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public RuntimeEntity AddEntity(GameEntity definition)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(definition.Id)) definition.Id = Guid.NewGuid().ToString("N");
        if (_entitiesById.ContainsKey(definition.Id)) throw new InvalidOperationException($"Entity id already exists: {definition.Id}");
        Definition.Entities.Add(definition);
        RuntimeEntity entity = CreateEntity(definition);
        Register(entity);
        EntityAdded?.Invoke(entity);
        return entity;
    }

    public bool RemoveEntity(string idOrName)
    {
        ThrowIfDisposed();
        RuntimeEntity? entity = GetEntity(idOrName);
        if (entity is null) return false;
        EntityRemoving?.Invoke(entity);
        Definition.Entities.Remove(entity.Definition);
        _entitiesById.Remove(entity.Id);
        if (_entitiesByName.TryGetValue(entity.Name, out RuntimeEntity? named) && ReferenceEquals(named, entity))
            _entitiesByName.Remove(entity.Name);
        EntityRevision++;
        return true;
    }

    public RuntimeEntity AddPointLight(string name, Vector3 position, Vector3 color, float intensity = 1.0f, float range = 8.0f)
    {
        return AddEntity(new GameEntity
        {
            Name = name,
            Type = "point_light",
            Transform = new TransformSettings { Position = Vector3Dto.FromVector3(position) },
            PointLight = new PointLightSettings
            {
                Color = Vector3Dto.FromVector3(Vector3.Max(color, Vector3.Zero)),
                Intensity = Math.Max(intensity, 0.0f),
                Range = Math.Max(range, 0.001f)
            }
        });
    }

    public RuntimeEntity AddSpotLight(
        string name,
        Vector3 position,
        Vector3 rotationDegrees,
        Vector3 color,
        float intensity = 1.0f,
        float range = 12.0f,
        float innerCone = 18.0f,
        float outerCone = 28.0f)
    {
        return AddEntity(new GameEntity
        {
            Name = name,
            Type = "spot_light",
            Transform = new TransformSettings
            {
                Position = Vector3Dto.FromVector3(position),
                RotationDegrees = Vector3Dto.FromVector3(rotationDegrees)
            },
            SpotLight = new SpotLightSettings
            {
                Color = Vector3Dto.FromVector3(Vector3.Max(color, Vector3.Zero)),
                Intensity = Math.Max(intensity, 0.0f),
                Range = Math.Max(range, 0.001f),
                InnerConeAngleDegrees = Math.Clamp(innerCone, 0.0f, 89.0f),
                OuterConeAngleDegrees = Math.Clamp(Math.Max(outerCone, innerCone + 0.01f), 0.01f, 89.5f)
            }
        });
    }

    public void Update(float deltaSeconds) => Update(deltaSeconds, RuntimeCameraInput.None);

    public void Update(float deltaSeconds, RuntimeCameraInput input)
    {
        ThrowIfDisposed();
        _animations.Update(Definition, Math.Max(deltaSeconds, 0.0f));
        Debug.Update(Math.Max(deltaSeconds, 0.0f));
        RuntimeCamera mainCamera = MainCamera;
        foreach (RuntimeCamera camera in Cameras.Where(camera => camera.Enabled))
        {
            // Input belongs to the active/main camera. Viewport cameras are
            // render-only unless a script explicitly changes their settings.
            camera.UpdateControl(this, deltaSeconds,
                ReferenceEquals(camera, mainCamera) ? input : RuntimeCameraInput.None);
        }
        Definition.Camera = MainCamera.Settings;
        Definition.MainCamera = MainCamera.Name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animations.Dispose();
        foreach (RuntimeEntity entity in _entitiesById.Values.ToArray()) EntityRemoving?.Invoke(entity);
        _entitiesById.Clear();
        _entitiesByName.Clear();
        Debug.Clear();
    }

    private void Register(RuntimeEntity entity)
    {
        _entitiesById[entity.Id] = entity;
        if (!string.IsNullOrWhiteSpace(entity.Name)) _entitiesByName.TryAdd(entity.Name, entity);
        EntityRevision++;
    }

    private RuntimeEntity CreateEntity(GameEntity entity) => new(entity, _resetPmxPhysics,
        collider => _meshColliderResolver?.Invoke(entity.Id, collider),
        node => _nodeWorldResolver?.Invoke(entity.Id, node),
        _motionCommand,
        _speechRequest);

    private static void NormalizeCameras(GameProjectScene scene)
    {
        if (scene.Cameras.Count == 0)
        {
            scene.Cameras.Add(new SceneCameraSettings { Name = "Main Camera", IsMain = true, Camera = scene.Camera });
        }
        if (!scene.Cameras.Any(camera => camera.IsMain)) scene.Cameras[0].IsMain = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
