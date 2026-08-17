using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed class RuntimeEntity
{
    public RuntimeEntity(GameEntity definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
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
