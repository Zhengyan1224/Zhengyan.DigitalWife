using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeSpotLightCollection
{
    private readonly Func<IEnumerable<RuntimeEntity>> _entities;
    private readonly Func<string?, string, Vector3, Vector3, Vector3, float, float, float, float, bool, RuntimeEntity> _add;
    private readonly Func<string, bool> _remove;

    internal RuntimeSpotLightCollection(
        Func<IEnumerable<RuntimeEntity>> entities,
        Func<string?, string, Vector3, Vector3, Vector3, float, float, float, float, bool, RuntimeEntity> add,
        Func<string, bool> remove)
    {
        _entities = entities;
        _add = add;
        _remove = remove;
    }

    public IEnumerable<RuntimeEntity> All => _entities().Where(entity => entity.IsSpotLight);

    public int Count => All.Count();

    public RuntimeEntity? Get(string idOrName)
    {
        return All.FirstOrDefault(entity =>
            string.Equals(entity.Id, idOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public RuntimeEntity Add(
        string name,
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity = 1.0f,
        float range = 12.0f,
        float innerConeAngleDegrees = 18.0f,
        float outerConeAngleDegrees = 28.0f,
        bool enabled = true)
    {
        return _add(null, name, position, direction, color, intensity, range,
            innerConeAngleDegrees, outerConeAngleDegrees, enabled);
    }

    internal RuntimeEntity AddWithId(
        string? id,
        string name,
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity,
        float range,
        float innerConeAngleDegrees,
        float outerConeAngleDegrees,
        bool enabled)
    {
        return _add(id, name, position, direction, color, intensity, range,
            innerConeAngleDegrees, outerConeAngleDegrees, enabled);
    }

    public bool Remove(string idOrName) => _remove(idOrName);

    public void Clear()
    {
        foreach (string id in All.Select(entity => entity.Id).ToArray())
        {
            _remove(id);
        }
    }
}
