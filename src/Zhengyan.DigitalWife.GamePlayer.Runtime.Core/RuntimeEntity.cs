using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeEntity
{
    private readonly Func<string, bool>? _resetPmxPhysics;
    private readonly Func<ColliderSettings, RuntimeMeshCollider?>? _meshColliderResolver;
    private readonly Func<string, Matrix4x4?>? _nodeWorldResolver;

    public RuntimeEntity(GameEntity definition, Func<string, bool>? resetPmxPhysics = null, Func<ColliderSettings, RuntimeMeshCollider?>? meshColliderResolver = null, Func<string, Matrix4x4?>? nodeWorldResolver = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _resetPmxPhysics = resetPmxPhysics;
        _meshColliderResolver = meshColliderResolver;
        _nodeWorldResolver = nodeWorldResolver;
    }

    public GameEntity Definition { get; }

    public string Id => Definition.Id;

    public string Name
    {
        get => Definition.Name;
        set => Definition.Name = value?.Trim() ?? string.Empty;
    }

    public string Type => Definition.Type;

    public bool IsPmxModel => NormalizeType(Type) == "pmx_model";

    public bool IsPointLight => NormalizeType(Type) is "point_light" or "pointlight";

    public bool IsSpotLight => NormalizeType(Type) is "spot_light" or "spotlight";

    public bool IsTexturedPlane => NormalizeType(Type) is "textured_plane" or "plane";

    public bool IsParticleSystem => NormalizeType(Type) is "particle_system" or "particles" or "particle";

    public bool IsWaterSurface => NormalizeType(Type) is "water_surface" or "water";

    public Vector3 Position
    {
        get => Definition.Transform.Position.ToVector3();
        set => Definition.Transform.Position = Vector3Dto.FromVector3(RequireFinite(value, nameof(value)));
    }

    public Vector3 RotationDegrees
    {
        get => Definition.Transform.RotationDegrees.ToVector3();
        set => Definition.Transform.RotationDegrees = Vector3Dto.FromVector3(RequireFinite(value, nameof(value)));
    }

    public Vector3 Scale
    {
        get => Definition.Transform.Scale.ToVector3();
        set
        {
            Vector3 valid = RequireFinite(value, nameof(value));
            Definition.Transform.Scale = Vector3Dto.FromVector3(new Vector3(
                MathF.Abs(valid.X) < 1e-6f ? 1e-6f : valid.X,
                MathF.Abs(valid.Y) < 1e-6f ? 1e-6f : valid.Y,
                MathF.Abs(valid.Z) < 1e-6f ? 1e-6f : valid.Z));
        }
    }

    public Matrix4x4 TransformMatrix
    {
        get
        {
            const float radians = MathF.PI / 180.0f;
            Vector3 rotation = RotationDegrees * radians;
            return Matrix4x4.CreateScale(Scale)
                * Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z)
                * Matrix4x4.CreateTranslation(Position);
        }
    }

    public bool Enabled
    {
        get => IsPointLight ? Definition.PointLight.Enabled : IsSpotLight ? Definition.SpotLight.Enabled : true;
        set
        {
            if (IsPointLight) Definition.PointLight.Enabled = value;
            if (IsSpotLight) Definition.SpotLight.Enabled = value;
        }
    }

    public string ReceiveShadowMode
    {
        get => Definition.ReceiveShadowMode;
        set => Definition.ReceiveShadowMode = string.Equals(value, "toon", StringComparison.OrdinalIgnoreCase)
            ? "toon"
            : "smooth";
    }

    public Vector3 LightColor
    {
        get => IsPointLight ? Definition.PointLight.Color.ToVector3() : Definition.SpotLight.Color.ToVector3();
        set
        {
            Vector3 color = Vector3.Max(RequireFinite(value, nameof(value)), Vector3.Zero);
            if (IsPointLight) Definition.PointLight.Color = Vector3Dto.FromVector3(color);
            if (IsSpotLight) Definition.SpotLight.Color = Vector3Dto.FromVector3(color);
        }
    }

    public float LightIntensity
    {
        get => IsPointLight ? Definition.PointLight.Intensity : Definition.SpotLight.Intensity;
        set
        {
            float valid = RequireNonNegative(value, nameof(value));
            if (IsPointLight) Definition.PointLight.Intensity = valid;
            if (IsSpotLight) Definition.SpotLight.Intensity = valid;
        }
    }

    public float LightRange
    {
        get => IsPointLight ? Definition.PointLight.Range : Definition.SpotLight.Range;
        set
        {
            if (!float.IsFinite(value) || value <= 0.0f) throw new ArgumentOutOfRangeException(nameof(value));
            if (IsPointLight) Definition.PointLight.Range = value;
            if (IsSpotLight) Definition.SpotLight.Range = value;
        }
    }

    public Vector3 SpotDirection
    {
        get
        {
            const float radians = MathF.PI / 180.0f;
            Vector3 rotation = RotationDegrees * radians;
            Quaternion quaternion = Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z);
            return Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, quaternion));
        }
    }

    public float SpotInnerConeAngleDegrees => Definition.SpotLight.InnerConeAngleDegrees;

    public float SpotOuterConeAngleDegrees => Definition.SpotLight.OuterConeAngleDegrees;

    public bool CastsShadows => IsPointLight ? Definition.PointLight.CastShadows : IsSpotLight && Definition.SpotLight.CastShadows;

    public bool TryResetPhysics()
        => IsPmxModel && _resetPmxPhysics?.Invoke(Id) == true;

    public CollisionSettings Collision => Definition.Collision;
    public IList<ColliderSettings> Colliders => Definition.Colliders;
    internal IEnumerable<ColliderSettings> EffectiveColliders => GameEntityCollision.GetEffectiveColliders(Definition);
    public bool CollisionEnabled
    {
        get => EffectiveColliders.Any(c => c.Enabled);
        set { if (Definition.Colliders.Count == 0) Definition.Collision.Enabled = value; else foreach (ColliderSettings c in Definition.Colliders) c.Enabled = value; }
    }
    public string CollisionShape => EffectiveColliders.FirstOrDefault()?.Shape ?? Definition.Collision.Shape;
    public Vector3 CollisionPosition { get => Definition.Colliders.Count == 0 ? Definition.Collision.Center.ToVector3() : GetPrimaryCollider().Position.ToVector3(); set { if (Definition.Colliders.Count == 0) Definition.Collision.Center = Vector3Dto.FromVector3(value); else GetPrimaryCollider().Position = Vector3Dto.FromVector3(value); } }
    public Vector3 ColliderPosition { get => CollisionPosition; set => CollisionPosition = value; }
    public float CollisionRadius { get => Definition.Colliders.Count == 0 ? Definition.Collision.Radius : GetPrimaryCollider().Radius; set { if (Definition.Colliders.Count == 0) Definition.Collision.Radius = Math.Max(0.0001f, value); else GetPrimaryCollider().Radius = Math.Max(0.0001f, value); } }
    public float ColliderRadius { get => CollisionRadius; set => CollisionRadius = value; }
    public float CollisionHeight { get => Definition.Colliders.Count == 0 ? Definition.Collision.Height : GetPrimaryCollider().Height; set { if (Definition.Colliders.Count == 0) Definition.Collision.Height = Math.Max(0.0f, value); else GetPrimaryCollider().Height = Math.Max(0.0f, value); } }
    public float ColliderHeight { get => CollisionHeight; set => CollisionHeight = value; }
    public string CollisionAxis { get => Definition.Colliders.Count == 0 ? Definition.Collision.Axis : GetPrimaryCollider().Axis; set { if (Definition.Colliders.Count == 0) Definition.Collision.Axis = NormalizeAxis(value); else GetPrimaryCollider().Axis = NormalizeAxis(value); } }
    public string ColliderAxis { get => CollisionAxis; set => CollisionAxis = value; }

    public void SetCapsuleCollider(float radius, float height, float centerX = 0, float centerY = 1, float centerZ = 0, string axis = "y")
    { Definition.Collision.Enabled = false; Definition.Colliders.Clear(); AddCapsuleCollider("Capsule Collider", radius, height, centerX, centerY, centerZ, axis); }
    public string AddCapsuleCollider(string name, float radius, float height, float centerX = 0, float centerY = 1, float centerZ = 0, string axis = "y", float rotationX = 0, float rotationY = 0, float rotationZ = 0)
    { ColliderSettings c = new() { Name = string.IsNullOrWhiteSpace(name) ? "Capsule Collider" : name, Enabled = true, Shape = "capsule", Position = new Vector3Dto(centerX, centerY, centerZ), Radius = Math.Max(0.0001f, radius), Height = Math.Max(0, height), Axis = NormalizeAxis(axis), RotationDegrees = new Vector3Dto(rotationX, rotationY, rotationZ) }; Definition.Colliders.Add(c); Definition.Collision.Enabled = false; return c.Id; }
    public string AddBoxCollider(string name, float sizeX, float sizeY, float sizeZ, float centerX = 0, float centerY = .5f, float centerZ = 0, float rotationX = 0, float rotationY = 0, float rotationZ = 0)
    { ColliderSettings c = new() { Name = string.IsNullOrWhiteSpace(name) ? "Box Collider" : name, Enabled = true, Shape = "box", Position = new Vector3Dto(centerX, centerY, centerZ), Size = new Vector3Dto(Math.Max(.001f, sizeX), Math.Max(.001f, sizeY), Math.Max(.001f, sizeZ)), RotationDegrees = new Vector3Dto(rotationX, rotationY, rotationZ) }; Definition.Colliders.Add(c); Definition.Collision.Enabled = false; return c.Id; }
    public string AddMeshCollider(string name, bool walkable = true, float maxSlopeDegrees = 55, float offsetX = 0, float offsetY = 0, float offsetZ = 0, float scaleX = 1, float scaleY = 1, float scaleZ = 1, float rotationX = 0, float rotationY = 0, float rotationZ = 0)
    { ColliderSettings c = new() { Name = string.IsNullOrWhiteSpace(name) ? "Mesh Collider" : name, Enabled = true, Shape = "mesh", Position = new Vector3Dto(offsetX, offsetY, offsetZ), Size = new Vector3Dto(Math.Max(.001f, MathF.Abs(scaleX)), Math.Max(.001f, MathF.Abs(scaleY)), Math.Max(.001f, MathF.Abs(scaleZ))), RotationDegrees = new Vector3Dto(rotationX, rotationY, rotationZ), Walkable = walkable, MaxSlopeDegrees = Math.Clamp(maxSlopeDegrees, 0, 89.9f) }; Definition.Colliders.Add(c); Definition.Collision.Enabled = false; return c.Id; }
    public bool RemoveCollider(string idOrName) { int i = Definition.Colliders.FindIndex(c => string.Equals(c.Id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, idOrName, StringComparison.OrdinalIgnoreCase)); if (i < 0) return false; Definition.Colliders.RemoveAt(i); return true; }
    public void ClearColliders() { Definition.Colliders.Clear(); Definition.Collision.Enabled = false; }
    public void DisableCollider() { foreach (ColliderSettings c in Definition.Colliders) c.Enabled = false; Definition.Collision.Enabled = false; }
    public bool TryGetCapsule(out RuntimeCapsule capsule) => RuntimePhysics.TryCreateCapsule(this, out capsule);
    public bool Raycast(RuntimeRay ray, out float distance, out Vector3 point) => RuntimePhysics.TryRaycastEntity(this, ray, out _, out distance, out point);
    public bool CheckCollision(RuntimeEntity other) => RuntimePhysics.CheckCollision(this, other);
    public float DistanceToCollider(RuntimeEntity other) => RuntimePhysics.DistanceBetween(this, other);

    internal Matrix4x4 GetColliderParentWorld(ColliderSettings collider) => !string.IsNullOrWhiteSpace(collider.BoundBoneName) && _nodeWorldResolver?.Invoke(collider.BoundBoneName) is Matrix4x4 bone ? bone : TransformMatrix;
    internal bool TryCreateMeshCollider(ColliderSettings collider, out RuntimeMeshCollider mesh) { mesh = _meshColliderResolver?.Invoke(collider) ?? default; return mesh.Triangles is { Count: > 0 }; }

    private ColliderSettings GetPrimaryCollider() => EffectiveColliders.FirstOrDefault() ?? throw new InvalidOperationException("Entity has no collider.");
    private static string NormalizeAxis(string? axis) => (axis ?? string.Empty).Trim().ToLowerInvariant() switch { "x" => "x", "z" => "z", _ => "y" };

    public void ResetPhysics() => _ = TryResetPhysics();

    public void ResetMotionPhysics() => ResetPhysics();

    private static string NormalizeType(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');

    private static Vector3 RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    private static float RequireNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0.0f) throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}
