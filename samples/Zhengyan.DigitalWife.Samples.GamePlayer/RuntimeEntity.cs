using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;
using Zhengyan.DigitalWife.Mmd.Game.Speech;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeEntity
{
    private readonly GameEntity _definition;
    private readonly PmxModelComponent? _model;
    private readonly ParticleSystemComponent? _particle;
    private readonly WaterSurfaceComponent? _water;
    private readonly TexturedPlaneComponent? _plane;
    private readonly Func<string, string> _resolvePath;
    private RuntimeScene? _scene;
    private RuntimeVoice? _voice;
    private RelationTransformUpdater? _relationUpdater;

    internal RuntimeEntity(GameEntity definition, PmxModelComponent model, Func<string, string> resolvePath)
    {
        _definition = definition;
        _model = model;
        _resolvePath = resolvePath;
    }

    internal RuntimeEntity(GameEntity definition, ParticleSystemComponent particle)
    {
        _definition = definition;
        _particle = particle;
        _resolvePath = static path => path;
    }

    internal RuntimeEntity(GameEntity definition, WaterSurfaceComponent water)
    {
        _definition = definition;
        _water = water;
        _resolvePath = static path => path;
    }

    internal RuntimeEntity(GameEntity definition, TexturedPlaneComponent plane)
    {
        _definition = definition;
        _plane = plane;
        _resolvePath = static path => path;
    }

    internal RuntimeEntity(GameEntity definition)
    {
        _definition = definition;
        _resolvePath = static path => path;
    }

    public string Id => _definition.Id;

    public string Name => _definition.Name;

    public string Type => _definition.Type;

    public bool IsPmxModel => _model is not null;

    public IReadOnlyList<string> MaterialNames => _model?.MaterialNames ?? [];

    public IReadOnlyList<string> MorphNames => _model?.MorphNames ?? [];

    public IReadOnlyList<string> NodeNames => _model?.NodeNames ?? [];

    public IReadOnlyDictionary<string, float> MorphWeights => _model?.MorphWeights ?? new Dictionary<string, float>();

    public IReadOnlyDictionary<string, float> MorphSaveAnimWeights => _model?.MorphSaveAnimWeights ?? new Dictionary<string, float>();

    public bool RelationEnabled => _definition.Relation.Enabled;

    public string RelationEntity => _definition.Relation.RelationEntity;

    public bool RelationBindComponentTransform => _definition.Relation.BindComponentTransform;

    public bool RelationBindLighting => _definition.Relation.BindLighting;

    public CollisionSettings Collision => _definition.Collision;

    public IList<ColliderSettings> Colliders => _definition.Colliders;

    internal IEnumerable<ColliderSettings> EffectiveColliders
    {
        get
        {
            if (_definition.Colliders.Count > 0)
            {
                foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(_definition))
                {
                    yield return collider;
                }

                yield break;
            }

            foreach (ColliderSettings collider in GameEntityCollision.GetEffectiveColliders(_definition))
            {
                yield return collider;
            }
        }
    }

    public bool CollisionEnabled
    {
        get => EffectiveColliders.Any(collider => collider.Enabled);
        set
        {
            if (_definition.Colliders.Count == 0)
            {
                _definition.Collision.Enabled = value;
                return;
            }

            foreach (ColliderSettings collider in _definition.Colliders)
            {
                collider.Enabled = value;
            }
        }
    }

    public string CollisionShape => EffectiveColliders.FirstOrDefault()?.Shape ?? _definition.Collision.Shape;

    public Vector3 CollisionCenter
    {
        get => GetPrimaryCollider().Position.ToVector3();
        set => GetPrimaryCollider().Position = Vector3Dto.FromVector3(value);
    }

    public float CollisionRadius
    {
        get => GetPrimaryCollider().Radius;
        set => GetPrimaryCollider().Radius = Math.Max(0.0001f, value);
    }

    public float CollisionHeight
    {
        get => GetPrimaryCollider().Height;
        set => GetPrimaryCollider().Height = Math.Max(0.0f, value);
    }

    public string CollisionAxis
    {
        get => GetPrimaryCollider().Axis;
        set => GetPrimaryCollider().Axis = NormalizeCollisionAxis(value);
    }

    public Vector3 Position
    {
        get => _model?.Position ?? _particle?.Position ?? _water?.Position ?? _plane?.Position ?? _definition.Transform.Position.ToVector3();
        set
        {
            bool applied = false;
            if (_model is not null)
            {
                _model.Position = value;
                applied = true;
            }

            if (_particle is not null)
            {
                _particle.Position = value;
                applied = true;
            }

            if (_water is not null)
            {
                _water.Position = value;
                applied = true;
            }

            if (_plane is not null)
            {
                _plane.Position = value;
                applied = true;
            }

            if (!applied)
            {
                _definition.Transform.Position = Vector3Dto.FromVector3(value);
            }
        }
    }

    public Vector3 Scale
    {
        get => _model?.Scale ?? _water?.Scale ?? _plane?.Scale ?? _definition.Transform.Scale.ToVector3();
        set
        {
            bool applied = false;
            if (_model is not null)
            {
                _model.Scale = value;
                applied = true;
            }

            if (_water is not null)
            {
                _water.Scale = value;
                applied = true;
            }

            if (_plane is not null)
            {
                _plane.Scale = value;
                applied = true;
            }

            if (!applied)
            {
                _definition.Transform.Scale = Vector3Dto.FromVector3(value);
            }
        }
    }

    public Quaternion Rotation
    {
        get => _model?.Rotation ?? _water?.Rotation ?? _plane?.Rotation ?? ToQuaternion(_definition.Transform.RotationDegrees.ToVector3());
        set
        {
            bool applied = false;
            if (_model is not null)
            {
                _model.Rotation = value;
                applied = true;
            }

            if (_water is not null)
            {
                _water.Rotation = value;
                applied = true;
            }

            if (_plane is not null)
            {
                _plane.Rotation = value;
                applied = true;
            }

            if (!applied)
            {
                _definition.Transform.RotationDegrees = Vector3Dto.FromVector3(ToEulerDegrees(value));
            }
        }
    }

    public bool IsPlaying
    {
        get => _model?.IsPlaying ?? _particle?.Enabled ?? _water?.Enabled ?? _plane?.Visible ?? _definition.IsPlaying;
        set
        {
            if (_model is not null)
            {
                _model.IsPlaying = value;
            }

            if (_particle is not null)
            {
                _particle.Enabled = value;
                _particle.Visible = value;
            }

            if (_water is not null)
            {
                _water.Enabled = value;
                _water.Visible = value;
            }

            if (_plane is not null)
            {
                _plane.Visible = value;
            }
        }
    }

    public float PlaybackSpeed
    {
        get => _model?.PlaybackSpeed ?? _particle?.SimulationSpeed ?? 1.0f;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 10.0f);
            if (_model is not null)
            {
                _model.PlaybackSpeed = clamped;
            }

            if (_particle is not null)
            {
                _particle.SimulationSpeed = clamped;
            }
        }
    }

    public bool LoopMotion
    {
        get => _model?.LoopMotion ?? _definition.LoopMotion;
        set
        {
            _definition.LoopMotion = value;
            if (_model is not null)
            {
                _model.LoopMotion = value;
            }
        }
    }

    public bool ResetPhysicsOnMotionLoop
    {
        get => _model?.ResetPhysicsOnMotionLoop ?? _definition.ResetPhysicsOnMotionLoop;
        set
        {
            _definition.ResetPhysicsOnMotionLoop = value;
            if (_model is not null)
            {
                _model.ResetPhysicsOnMotionLoop = value;
            }
        }
    }

    public bool EnableEdge
    {
        get => _model?.EnableEdge ?? _definition.EnableEdge;
        set
        {
            _definition.EnableEdge = value;
            if (_model is not null)
            {
                _model.EnableEdge = value;
            }
        }
    }

    public bool EnableShadow
    {
        get => _model?.EnableShadow ?? _definition.EnableShadow;
        set
        {
            _definition.EnableShadow = value;
            if (_model is not null)
            {
                _model.EnableShadow = value;
            }
        }
    }

    public bool EnableWaterInteraction
    {
        get => string.Equals(_definition.Type, "particle_system", StringComparison.OrdinalIgnoreCase)
            ? _definition.Particle.EnableWaterInteraction
            : false;
        set
        {
            if (string.Equals(_definition.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Particle.EnableWaterInteraction = value;
            }
        }
    }

    public bool KillOnWaterContact
    {
        get => string.Equals(_definition.Type, "particle_system", StringComparison.OrdinalIgnoreCase)
            ? _definition.Particle.KillOnWaterContact
            : false;
        set
        {
            if (string.Equals(_definition.Type, "particle_system", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Particle.KillOnWaterContact = value;
            }
        }
    }

    public bool WaterInteractionEnabled
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            && _definition.Water.EnableInteraction;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.EnableInteraction = value;
            }
        }
    }

    public float WaterInteractionRadius
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.InteractionRadius
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.InteractionRadius = Math.Max(0.001f, value);
            }
        }
    }

    public float WaterInteractionStrength
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.InteractionStrength
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.InteractionStrength = Math.Clamp(value, 0.0f, 4.0f);
            }
        }
    }

    public float ParticleRippleMinIntervalSeconds
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.ParticleRippleMinIntervalSeconds
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.ParticleRippleMinIntervalSeconds = Math.Max(0.0f, value);
            }
        }
    }

    public float ParticleRippleMergeDistance
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.ParticleRippleMergeDistance
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.ParticleRippleMergeDistance = Math.Max(0.0f, value);
            }
        }
    }

    public float RippleLifetimeSeconds
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.RippleLifetimeSeconds
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.RippleLifetimeSeconds = Math.Max(0.05f, value);
            }
        }
    }

    public float RippleWaveSpeed
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.RippleWaveSpeed
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.RippleWaveSpeed = value;
            }
        }
    }

    public float RippleFrequency
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.RippleFrequency
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.RippleFrequency = Math.Max(0.0f, value);
            }
        }
    }

    public float RippleNormalStrength
    {
        get => string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase)
            ? _definition.Water.RippleNormalStrength
            : 0.0f;
        set
        {
            if (string.Equals(_definition.Type, "water_surface", StringComparison.OrdinalIgnoreCase))
            {
                _definition.Water.RippleNormalStrength = Math.Max(0.0f, value);
            }
        }
    }

    public bool DrawShadowInMainPass
    {
        get => _model?.DrawShadowInMainPass ?? _definition.DrawShadowInMainPass;
        set
        {
            _definition.DrawShadowInMainPass = value;
            if (_model is not null)
            {
                _model.DrawShadowInMainPass = value;
            }
        }
    }

    public bool Visible
    {
        get => _model?.Visible ?? _particle?.Visible ?? _water?.Visible ?? _plane?.Visible ?? true;
        set
        {
            if (_model is not null)
            {
                _model.Visible = value;
            }

            if (_particle is not null)
            {
                _particle.Visible = value;
            }

            if (_water is not null)
            {
                _water.Visible = value;
            }

            if (_plane is not null)
            {
                _plane.Visible = value;
            }
        }
    }

    public float AnimationTimeSeconds
    {
        get => _model?.AnimationTimeSeconds ?? 0.0f;
        set
        {
            if (_model is not null)
            {
                _model.AnimationTimeSeconds = MathF.Max(0.0f, value);
            }
        }
    }

    public int MotionLayerCount => _model?.MotionLayerCount ?? 0;

    public void SetPosition(float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
    }

    public void Translate(float x, float y, float z)
    {
        Position += new Vector3(x, y, z);
    }

    public void SetScale(float x, float y, float z)
    {
        Scale = new Vector3(x, y, z);
    }

    public void RotateX(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, ToRadians(degrees)) * Rotation);
    }

    public void RotateY(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians(degrees)) * Rotation);
    }

    public void RotateZ(float degrees)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, ToRadians(degrees)) * Rotation);
    }

    public void SetCapsuleCollider(float radius, float height, float centerX = 0.0f, float centerY = 1.0f, float centerZ = 0.0f, string axis = "y")
    {
        _definition.Collision.Enabled = false;
        _definition.Colliders.Clear();
        _definition.Colliders.Add(CreateCapsuleCollider("Capsule Collider", radius, height, centerX, centerY, centerZ, axis));
    }

    public string AddCapsuleCollider(
        string name,
        float radius,
        float height,
        float centerX = 0.0f,
        float centerY = 1.0f,
        float centerZ = 0.0f,
        string axis = "y",
        float rotationX = 0.0f,
        float rotationY = 0.0f,
        float rotationZ = 0.0f)
    {
        ColliderSettings collider = CreateCapsuleCollider(name, radius, height, centerX, centerY, centerZ, axis);
        collider.RotationDegrees = new Vector3Dto(rotationX, rotationY, rotationZ);
        _definition.Colliders.Add(collider);
        _definition.Collision.Enabled = false;
        return collider.Id;
    }

    public string AddBoxCollider(
        string name,
        float sizeX,
        float sizeY,
        float sizeZ,
        float centerX = 0.0f,
        float centerY = 0.5f,
        float centerZ = 0.0f,
        float rotationX = 0.0f,
        float rotationY = 0.0f,
        float rotationZ = 0.0f)
    {
        ColliderSettings collider = new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Box Collider" : name,
            Enabled = true,
            Shape = "box",
            Position = new Vector3Dto(centerX, centerY, centerZ),
            RotationDegrees = new Vector3Dto(rotationX, rotationY, rotationZ),
            Size = new Vector3Dto(Math.Max(0.001f, sizeX), Math.Max(0.001f, sizeY), Math.Max(0.001f, sizeZ))
        };
        _definition.Colliders.Add(collider);
        _definition.Collision.Enabled = false;
        return collider.Id;
    }

    public bool RemoveCollider(string idOrName)
    {
        int index = _definition.Colliders.FindIndex(collider =>
            string.Equals(collider.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collider.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _definition.Colliders.RemoveAt(index);
        return true;
    }

    public void ClearColliders()
    {
        _definition.Colliders.Clear();
        _definition.Collision.Enabled = false;
    }

    public void DisableCollider()
    {
        foreach (ColliderSettings collider in _definition.Colliders)
        {
            collider.Enabled = false;
        }

        _definition.Collision.Enabled = false;
    }

    public bool TryGetCapsule(out RuntimeCapsule capsule)
    {
        return RuntimePhysics.TryCreateCapsule(this, out capsule);
    }

    public bool Raycast(RuntimeRay ray, out float distance, out Vector3 point)
    {
        distance = 0.0f;
        point = default;
        return RuntimePhysics.TryRaycastEntity(this, ray, out _, out distance, out point);
    }

    public bool CheckCollision(RuntimeEntity other)
    {
        return RuntimePhysics.CheckCollision(this, other);
    }

    public float DistanceToCollider(RuntimeEntity other)
    {
        return RuntimePhysics.DistanceBetween(this, other);
    }

    public void ApplyMotion(string motionPath)
    {
        _model?.ApplyMotion(_resolvePath(motionPath));
    }

    public void AddMotionLayer(string motionPath, float weight = 1.0f)
    {
        _model?.AddMotionLayer(_resolvePath(motionPath), weight);
    }

    public void SetMotionLayers(IEnumerable<MotionLayerDefinition> motionLayers)
    {
        if (_model is null)
        {
            return;
        }

        MotionLayerDefinition[] resolvedLayers = motionLayers
            .Select(layer => new MotionLayerDefinition(
                _resolvePath(layer.MotionPath),
                layer.Weight,
                layer.ResetPhysicsOnLoop))
            .ToArray();
        _model.SetMotionLayers(resolvedLayers);
    }

    public void SetMotionLayerWeight(string motionPath, float weight)
    {
        _model?.SetMotionLayerWeight(_resolvePath(motionPath), weight);
    }

    public void SetMotionLayerResetPhysicsOnLoop(string motionPath, bool resetPhysicsOnLoop)
    {
        _model?.SetMotionLayerResetPhysicsOnLoop(_resolvePath(motionPath), resetPhysicsOnLoop);
    }

    public void RemoveMotionLayer(string motionPath)
    {
        _model?.RemoveMotionLayer(_resolvePath(motionPath));
    }

    public void ClearMotion()
    {
        _model?.ClearMotion();
    }

    public IReadOnlyList<MotionLayerInfo> GetMotionLayers()
    {
        return _model?.GetMotionLayers() ?? [];
    }

    public MotionLayerInfo? GetMotionLayer(string motionPath)
    {
        string resolvedPath = _resolvePath(motionPath);
        return GetMotionLayers().FirstOrDefault(layer =>
            string.Equals(layer.MotionPath, resolvedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    public void PlayMotion()
    {
        _model?.PlayMotion();
    }

    public void PauseMotion()
    {
        _model?.PauseMotion();
    }

    public void StopMotion()
    {
        _model?.StopMotion();
    }

    public void ResetMotion()
    {
        _model?.ResetAnimation();
    }

    public void ResetMotionPhysics()
    {
        _model?.ResetPhysics();
    }

    public void SeekMotionTime(float timeSeconds)
    {
        _model?.SeekMotionTime(timeSeconds);
    }

    public void SeekMotionFrame(float frame)
    {
        _model?.SeekMotionFrame(frame);
    }

    public bool TrySetMotionLayerPlaying(string motionPath, bool isPlaying)
    {
        return _model?.TrySetMotionLayerPlaying(_resolvePath(motionPath), isPlaying) == true;
    }

    public void SetMotionLayerPlaying(string motionPath, bool isPlaying)
    {
        _model?.SetMotionLayerPlaying(_resolvePath(motionPath), isPlaying);
    }

    public bool TrySetMotionLayerTime(string motionPath, float timeSeconds)
    {
        return _model?.TrySetMotionLayerTime(_resolvePath(motionPath), timeSeconds) == true;
    }

    public void SetMotionLayerTime(string motionPath, float timeSeconds)
    {
        _model?.SetMotionLayerTime(_resolvePath(motionPath), timeSeconds);
    }

    public bool TrySetMotionLayerFrame(string motionPath, float frame)
    {
        return _model?.TrySetMotionLayerFrame(_resolvePath(motionPath), frame) == true;
    }

    public void SetMotionLayerFrame(string motionPath, float frame)
    {
        _model?.SetMotionLayerFrame(_resolvePath(motionPath), frame);
    }

    public bool PauseMotionLayer(string motionPath)
    {
        return TrySetMotionLayerPlaying(motionPath, false);
    }

    public bool PlayMotionLayer(string motionPath)
    {
        return TrySetMotionLayerPlaying(motionPath, true);
    }

    public bool TryGetMorphWeight(string morphName, out float weight)
    {
        weight = 0.0f;
        return _model?.TryGetMorphWeight(morphName, out weight) == true;
    }

    public float GetMorphWeight(string morphName)
    {
        return _model?.GetMorphWeight(morphName) ?? throw new InvalidOperationException("Entity is not a PMX model.");
    }

    public bool TrySetMorphWeight(string morphName, float weight, bool overrideAnimation = true)
    {
        return _model?.TrySetMorphWeight(morphName, weight, overrideAnimation) == true;
    }

    public void SetMorphWeight(string morphName, float weight, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetMorphWeight(morphName, weight, overrideAnimation);
    }

    public bool TryGetMorphSaveAnimWeight(string morphName, out float weight)
    {
        weight = 0.0f;
        return _model?.TryGetMorphSaveAnimWeight(morphName, out weight) == true;
    }

    public float GetMorphSaveAnimWeight(string morphName)
    {
        return _model?.GetMorphSaveAnimWeight(morphName) ?? throw new InvalidOperationException("Entity is not a PMX model.");
    }

    public bool TrySetMorphSaveAnimWeight(string morphName, float weight)
    {
        return _model?.TrySetMorphSaveAnimWeight(morphName, weight) == true;
    }

    public void SetMorphSaveAnimWeight(string morphName, float weight)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetMorphSaveAnimWeight(morphName, weight);
    }

    public bool SaveMorphAnimWeight(string morphName)
    {
        return _model?.SaveMorphAnimWeight(morphName) == true;
    }

    public bool SaveAnimWeight(string morphName)
    {
        return SaveMorphAnimWeight(morphName);
    }

    public bool LoadMorphAnimWeight(string morphName)
    {
        return _model?.LoadMorphAnimWeight(morphName) == true;
    }

    public bool ClearMorphAnimWeight(string morphName)
    {
        return _model?.ClearMorphAnimWeight(morphName) == true;
    }

    public bool ClearMorphWeightOverride(string morphName)
    {
        return _model?.ClearMorphWeightOverride(morphName) == true;
    }

    public void ClearMorphWeightOverrides()
    {
        _model?.ClearMorphWeightOverrides();
    }

    public void SaveBaseAnimation()
    {
        _model?.SaveBaseAnimation();
    }

    public void LoadBaseAnimation()
    {
        _model?.LoadBaseAnimation();
    }

    public void ClearBaseAnimation()
    {
        _model?.ClearBaseAnimation();
    }

    public bool TryGetNodeState(string nodeName, out PmxNodeState state)
    {
        state = default;
        return _model?.TryGetNodeState(nodeName, out state) == true;
    }

    public PmxNodeState GetNodeState(string nodeName)
    {
        return _model?.GetNodeState(nodeName) ?? throw new InvalidOperationException("Entity is not a PMX model.");
    }

    public bool TrySetNodeTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        return _model?.TrySetNodeTranslate(nodeName, translate, overrideAnimation) == true;
    }

    public void SetNodeTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetNodeTranslate(nodeName, translate, overrideAnimation);
    }

    public void SetNodeTranslate(string nodeName, float x, float y, float z, bool overrideAnimation = true)
    {
        SetNodeTranslate(nodeName, new Vector3(x, y, z), overrideAnimation);
    }

    public bool TrySetNodeRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        return _model?.TrySetNodeRotate(nodeName, rotate, overrideAnimation) == true;
    }

    public void SetNodeRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetNodeRotate(nodeName, rotate, overrideAnimation);
    }

    public void SetNodeRotateEuler(string nodeName, float xDegrees, float yDegrees, float zDegrees, bool overrideAnimation = true)
    {
        SetNodeRotate(nodeName, ToQuaternion(new Vector3(xDegrees, yDegrees, zDegrees)), overrideAnimation);
    }

    public bool TrySetNodeScale(string nodeName, Vector3 scale, bool overrideAnimation = true)
    {
        return _model?.TrySetNodeScale(nodeName, scale, overrideAnimation) == true;
    }

    public void SetNodeScale(string nodeName, Vector3 scale, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetNodeScale(nodeName, scale, overrideAnimation);
    }

    public void SetNodeScale(string nodeName, float x, float y, float z, bool overrideAnimation = true)
    {
        SetNodeScale(nodeName, new Vector3(x, y, z), overrideAnimation);
    }

    public bool TrySetNodeAnimTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        return _model?.TrySetNodeAnimTranslate(nodeName, translate, overrideAnimation) == true;
    }

    public void SetNodeAnimTranslate(string nodeName, Vector3 translate, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetNodeAnimTranslate(nodeName, translate, overrideAnimation);
    }

    public void SetNodeAnimTranslate(string nodeName, float x, float y, float z, bool overrideAnimation = true)
    {
        SetNodeAnimTranslate(nodeName, new Vector3(x, y, z), overrideAnimation);
    }

    public bool TrySetNodeAnimRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        return _model?.TrySetNodeAnimRotate(nodeName, rotate, overrideAnimation) == true;
    }

    public void SetNodeAnimRotate(string nodeName, Quaternion rotate, bool overrideAnimation = true)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Entity is not a PMX model.");
        }

        _model.SetNodeAnimRotate(nodeName, rotate, overrideAnimation);
    }

    public void SetNodeAnimRotateEuler(string nodeName, float xDegrees, float yDegrees, float zDegrees, bool overrideAnimation = true)
    {
        SetNodeAnimRotate(nodeName, ToQuaternion(new Vector3(xDegrees, yDegrees, zDegrees)), overrideAnimation);
    }

    public bool SaveNodeBaseAnimation(string nodeName)
    {
        return _model?.SaveNodeBaseAnimation(nodeName) == true;
    }

    public bool LoadNodeBaseAnimation(string nodeName)
    {
        return _model?.LoadNodeBaseAnimation(nodeName) == true;
    }

    public bool ClearNodeBaseAnimation(string nodeName)
    {
        return _model?.ClearNodeBaseAnimation(nodeName) == true;
    }

    public bool ClearNodeOverrides(string nodeName)
    {
        return _model?.ClearNodeOverrides(nodeName) == true;
    }

    public void ClearAllNodeOverrides()
    {
        _model?.ClearAllNodeOverrides();
    }

    public bool SetMaterialTexture(int materialIndex, string textureReference)
    {
        return _model?.SetMaterialTexture(materialIndex, ResolveRuntimeTextureReference(textureReference)) == true;
    }

    public bool SetMaterialTexture(string materialName, string textureReference)
    {
        return _model?.SetMaterialTexture(materialName, ResolveRuntimeTextureReference(textureReference)) == true;
    }

    public bool SetMaterialRenderTexture(int materialIndex, string renderTextureName)
    {
        return SetMaterialTexture(materialIndex, ToRenderTextureReference(renderTextureName));
    }

    public bool SetMaterialRenderTexture(string materialName, string renderTextureName)
    {
        return SetMaterialTexture(materialName, ToRenderTextureReference(renderTextureName));
    }

    public void ClearMaterialTextureOverride(int materialIndex)
    {
        _model?.ClearMaterialTextureOverride(materialIndex);
    }

    public void ClearMaterialTextureOverrides()
    {
        _model?.ClearMaterialTextureOverrides();
    }

    public void Speak(string text)
    {
        _voice?.Speak(this, text, (RuntimeVoiceOptions?)null);
    }

    public void Speak(string text, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId)
    {
        _voice?.Speak(this, text, speakerId);
    }

    public void Speak(string text, int speakerId, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId, float speed)
    {
        _voice?.Speak(this, text, speakerId, speed);
    }

    public void Speak(string text, int speakerId, float speed, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, int speakerId, float speed, float volume)
    {
        _voice?.Speak(this, text, speakerId, speed, volume);
    }

    public void Speak(string text, int speakerId, float speed, float volume, Action onCompleted)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            Volume = volume,
            OnCompleted = onCompleted
        });
    }

    public void Speak(string text, RuntimeVoiceOptions options)
    {
        _voice?.Speak(this, text, options);
    }

    public void SpeakWithCallback(string text, string callbackName)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            OnCompleted = () => DispatchSpeechCallback(callbackName)
        });
    }

    public void SpeakWithCallback(string text, int speakerId, float speed, float volume, string callbackName)
    {
        _voice?.Speak(this, text, new RuntimeVoiceOptions
        {
            SpeakerId = speakerId,
            Speed = speed,
            Volume = volume,
            OnCompleted = () => DispatchSpeechCallback(callbackName)
        });
    }

    public void StopSpeaking()
    {
        _voice?.Stop(this);
    }

    public void BindRelation(string targetEntityIdOrName, bool bindComponentTransform = true, bool bindLighting = false)
    {
        _ = TryBindRelation(targetEntityIdOrName, bindComponentTransform, bindLighting);
    }

    public bool TryBindRelation(string targetEntityIdOrName, bool bindComponentTransform = true, bool bindLighting = false)
    {
        if (_model is null || _scene is null || string.IsNullOrWhiteSpace(targetEntityIdOrName))
        {
            return false;
        }

        RuntimeEntity? relation = _scene.GetEntity(targetEntityIdOrName);
        if (relation?._model is null || ReferenceEquals(relation, this))
        {
            return false;
        }

        ClearRelationBinding();
        _relationUpdater = _model.CreateRelationTransformUpdater(relation._model, bindComponentTransform);
        _relationUpdater.BindLighting = bindLighting;

        _definition.Relation.Enabled = true;
        _definition.Relation.RelationEntity = targetEntityIdOrName;
        _definition.Relation.BindComponentTransform = bindComponentTransform;
        _definition.Relation.BindLighting = bindLighting;
        return true;
    }

    public void ClearRelationBinding()
    {
        if (_model is not null && _relationUpdater is not null)
        {
            _ = _model.RemoveTransformUpdater(_relationUpdater);
        }

        _relationUpdater = null;
        _definition.Relation.Enabled = false;
        _definition.Relation.RelationEntity = string.Empty;
    }

    internal void AttachVoice(RuntimeVoice voice)
    {
        _voice = voice;
        _voice.AttachEntity(this);
    }

    internal void AttachScene(RuntimeScene scene)
    {
        _scene = scene;
    }

    internal void DispatchSpeechCallback(string callbackName)
    {
        if (_scene is null || string.IsNullOrWhiteSpace(callbackName))
        {
            return;
        }

        _scene.DispatchSpeechEvent(this, callbackName);
    }

    internal SpeechTransformUpdater CreateSpeechUpdater(
        SpeechDictionarySet dictionaries,
        IReadOnlyDictionary<string, string>? vowelMorphMap)
    {
        if (_model is null)
        {
            throw new InvalidOperationException($"Entity '{Name}' is not a PMX model.");
        }

        return _model.CreateSpeechTransformUpdater(dictionaries.Kana, dictionaries.Vowel, vowelMorphMap);
    }

    internal void SyncFromModel()
    {
        if (_model is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_model.Position);
            _definition.Transform.Scale = Vector3Dto.FromVector3(_model.Scale);
            _definition.IsPlaying = _model.IsPlaying;
            _definition.PlaybackSpeed = _model.PlaybackSpeed;
            _definition.LoopMotion = _model.LoopMotion;
            _definition.ResetPhysicsOnMotionLoop = _model.ResetPhysicsOnMotionLoop;
            _definition.EnableEdge = _model.EnableEdge;
            _definition.EnableShadow = _model.EnableShadow;
            _definition.DrawShadowInMainPass = _model.DrawShadowInMainPass;
        }
        else if (_particle is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_particle.Position);
            _definition.IsPlaying = _particle.Enabled;
            _definition.Particle.SimulationSpeed = _particle.SimulationSpeed;
            _definition.Particle.Opacity = _particle.Opacity;
        }
        else if (_water is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_water.Position);
            _definition.Transform.Scale = Vector3Dto.FromVector3(_water.Scale);
            _definition.IsPlaying = _water.Enabled;
            _definition.Water.Alpha = _water.Alpha;
            _definition.Water.AnimationSpeed = _water.AnimationSpeed;
            _definition.Water.NormalTiling = _water.NormalTiling;
            _definition.Water.DeepColor = Vector3Dto.FromVector3(_water.DeepColor);
            _definition.Water.ReflectionTint = Vector3Dto.FromVector3(_water.ReflectionTint);
            _definition.Water.SkyReflectionStrength = _water.SkyReflectionStrength;
        }
        else if (_plane is not null)
        {
            _definition.Transform.Position = Vector3Dto.FromVector3(_plane.Position);
            _definition.Transform.Scale = Vector3Dto.FromVector3(_plane.Scale);
            _definition.Plane.Width = _plane.Width;
            _definition.Plane.Height = _plane.Height;
            _definition.Plane.Billboard = _plane.Billboard;
            _definition.Plane.Opacity = _plane.Opacity;
            _definition.Plane.Tint = Vector4Dto.FromVector4(_plane.Tint);
        }
    }

    private static float ToRadians(float degrees) => degrees * MathF.PI / 180.0f;

    private ColliderSettings GetPrimaryCollider()
    {
        if (_definition.Colliders.Count == 0)
        {
            if (_definition.Collision.Enabled)
            {
                _definition.Colliders.Add(CollisionGeometry.FromLegacy(_definition.Collision));
                _definition.Collision.Enabled = false;
            }
            else
            {
                _definition.Colliders.Add(CreateCapsuleCollider("Capsule Collider", 0.5f, 2.0f, 0.0f, 1.0f, 0.0f, "y"));
            }
        }

        return _definition.Colliders[0];
    }

    private static ColliderSettings CreateCapsuleCollider(
        string name,
        float radius,
        float height,
        float centerX,
        float centerY,
        float centerZ,
        string axis)
    {
        return new ColliderSettings
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Capsule Collider" : name,
            Enabled = true,
            Shape = "capsule",
            Position = new Vector3Dto(centerX, centerY, centerZ),
            Radius = Math.Max(0.0001f, radius),
            Height = Math.Max(0.0f, height),
            Axis = NormalizeCollisionAxis(axis)
        };
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private static Vector3 ToEulerDegrees(Quaternion rotation)
    {
        Quaternion q = Quaternion.Normalize(rotation);
        float sinrCosp = 2.0f * ((q.W * q.X) + (q.Y * q.Z));
        float cosrCosp = 1.0f - (2.0f * ((q.X * q.X) + (q.Y * q.Y)));
        float x = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2.0f * ((q.W * q.Y) - (q.Z * q.X));
        float y = MathF.Abs(sinp) >= 1.0f
            ? MathF.CopySign(MathF.PI * 0.5f, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2.0f * ((q.W * q.Z) + (q.X * q.Y));
        float cosyCosp = 1.0f - (2.0f * ((q.Y * q.Y) + (q.Z * q.Z)));
        float z = MathF.Atan2(sinyCosp, cosyCosp);
        return new Vector3(x, y, z) * (180.0f / MathF.PI);
    }

    private static string NormalizeCollisionAxis(string axis)
    {
        return (axis ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "x" => "x",
            "z" => "z",
            _ => "y"
        };
    }

    private static string ToRenderTextureReference(string renderTextureName)
    {
        string trimmed = (renderTextureName ?? string.Empty).Trim();
        return trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"rt:{trimmed}";
    }

    private string ResolveRuntimeTextureReference(string textureReference)
    {
        string trimmed = (textureReference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return _resolvePath(trimmed);
    }
}
