using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeScene : IDisposable
{
    private readonly Dictionary<string, RuntimeEntity> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeEntity> _entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeSceneAnimationController _animations;
    private readonly Func<string, bool>? _resetPmxPhysics;
    private bool _disposed;

    public RuntimeScene(
        string scenePath,
        GameProjectScene definition,
        Func<string, string> resolvePath,
        Func<string, bool>? resetPmxPhysics = null)
    {
        ScenePath = scenePath;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        NormalizeCameras(definition);
        Cameras = definition.Cameras.Select(camera => new RuntimeCamera(camera)).ToArray();
        Lighting = new RuntimeLighting(definition.Lighting);
        _animations = new RuntimeSceneAnimationController(resolvePath);
        _resetPmxPhysics = resetPmxPhysics;
        foreach (GameEntity entity in definition.Entities) Register(new RuntimeEntity(entity, _resetPmxPhysics));
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
    public IEnumerable<RuntimeEntity> PointLights => Entities.Where(entity => entity.IsPointLight && entity.Enabled);
    public IEnumerable<RuntimeEntity> SpotLights => Entities.Where(entity => entity.IsSpotLight && entity.Enabled);
    public IEnumerable<RuntimeEntity> TexturedPlanes => Entities.Where(entity => entity.IsTexturedPlane);
    public IEnumerable<RuntimeEntity> ParticleSystems => Entities.Where(entity => entity.IsParticleSystem);
    public IEnumerable<RuntimeEntity> WaterSurfaces => Entities.Where(entity => entity.IsWaterSurface);
    public long EntityRevision { get; private set; }

    public RuntimeCamera MainCamera => Cameras.FirstOrDefault(camera => camera.Enabled && camera.IsMain)
        ?? Cameras.FirstOrDefault(camera => camera.Enabled)
        ?? Cameras[0];

    public IReadOnlyList<RuntimeCamera> RenderCameras
    {
        get
        {
            RuntimeCamera[] viewportCameras = Cameras.Where(camera => camera.Enabled && camera.Definition.Viewport.Enabled).ToArray();
            return viewportCameras.Length == 0 ? [MainCamera] : viewportCameras;
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
        RuntimeEntity entity = new(definition, _resetPmxPhysics);
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
        foreach (RuntimeCamera camera in Cameras.Where(camera => camera.Enabled))
            camera.UpdateControl(this, deltaSeconds, input);
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
    }

    private void Register(RuntimeEntity entity)
    {
        _entitiesById[entity.Id] = entity;
        if (!string.IsNullOrWhiteSpace(entity.Name)) _entitiesByName.TryAdd(entity.Name, entity);
        EntityRevision++;
    }

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
