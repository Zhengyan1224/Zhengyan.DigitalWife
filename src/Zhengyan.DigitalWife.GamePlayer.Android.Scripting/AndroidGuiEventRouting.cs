using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidGuiEventRouting
{
    public static RuntimeEntity? ResolveTarget(RuntimeScene scene, string configuredTarget, Func<RuntimeEntity, bool> hasScripts)
    {
        if (!string.IsNullOrWhiteSpace(configuredTarget) && scene.GetEntity(configuredTarget) is { } target)
        {
            return target;
        }

        RuntimeEntity[] scriptedTargets = scene.Entities.Where(hasScripts).ToArray();
        RuntimeEntity[] scriptedPmxTargets = scriptedTargets.Where(entity => entity.IsPmxModel).ToArray();
        if (scriptedPmxTargets.Length <= 1)
        {
            return scriptedPmxTargets.FirstOrDefault() ?? scriptedTargets.FirstOrDefault();
        }

        // Match the desktop fallback: prefer the model other PMX models bind to,
        // then an independent model, then its name for a stable selection.
        HashSet<string> relationTargets = scene.Entities
            .Where(entity => entity.IsPmxModel && entity.Definition.Relation.Enabled
                && !string.IsNullOrWhiteSpace(entity.Definition.Relation.RelationEntity))
            .Select(entity => scene.GetEntity(entity.Definition.Relation.RelationEntity))
            .Where(entity => entity is not null)
            .Select(entity => entity!.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scriptedPmxTargets
            .OrderByDescending(entity => relationTargets.Contains(entity.Id))
            .ThenBy(entity => entity.Definition.Relation.Enabled)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }
}
