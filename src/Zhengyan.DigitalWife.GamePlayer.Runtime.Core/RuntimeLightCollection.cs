using System.Collections;
using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Script-facing collection for dynamically created point or spot lights.</summary>
public sealed class RuntimeLightCollection : IReadOnlyCollection<RuntimeEntity>
{
    private readonly RuntimeScene _scene;
    private readonly bool _spot;

    internal RuntimeLightCollection(RuntimeScene scene, bool spot)
    {
        _scene = scene;
        _spot = spot;
    }

    public int Count => _scene.Entities.Count(entity => (_spot ? entity.IsSpotLight : entity.IsPointLight) && entity.Enabled);

    public IEnumerable<RuntimeEntity> All => this;

    public RuntimeEntity? Get(string idOrName)
        => _scene.GetEntity(idOrName) is { } entity && (_spot ? entity.IsSpotLight : entity.IsPointLight)
            ? entity
            : null;

    public RuntimeEntity Add(
        string name,
        Vector3 position,
        Vector3 color,
        float intensity = 1.0f,
        float range = 8.0f)
        => _spot
            ? _scene.AddSpotLight(name, position, Vector3.Zero, color, intensity, range)
            : _scene.AddPointLight(name, position, color, intensity, range);

    public RuntimeEntity Add(
        string name,
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity = 1.0f,
        float range = 12.0f,
        float innerConeAngleDegrees = 18.0f,
        float outerConeAngleDegrees = 28.0f)
        => _spot
            ? _scene.AddSpotLight(name, position, DirectionToEuler(direction), color, intensity, range, innerConeAngleDegrees, outerConeAngleDegrees)
            : _scene.AddPointLight(name, position, color, intensity, range);

    public bool Remove(string idOrName)
    {
        RuntimeEntity? entity = Get(idOrName);
        return entity is not null && _scene.RemoveEntity(entity.Id);
    }

    public void Clear()
    {
        foreach (RuntimeEntity entity in this.ToArray()) _scene.RemoveEntity(entity.Id);
    }

    public IEnumerator<RuntimeEntity> GetEnumerator()
        => _scene.Entities
            .Where(entity => (_spot ? entity.IsSpotLight : entity.IsPointLight) && entity.Enabled)
            .GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static Vector3 DirectionToEuler(Vector3 direction)
    {
        if (direction.LengthSquared() <= 1e-8f) return Vector3.Zero;
        direction = Vector3.Normalize(direction);
        return new Vector3(
            MathF.Asin(-direction.Y) * 180.0f / MathF.PI,
            MathF.Atan2(-direction.X, -direction.Z) * 180.0f / MathF.PI,
            0.0f);
    }
}
