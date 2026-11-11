using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public static class WebPmxRelations
{
    // Resolve dependencies before applying poses: exported entity order need
    // not put the body before its clothes, and a relation may itself be bound.
    public static IReadOnlySet<string> Apply(RuntimeScene scene, IReadOnlyDictionary<string, WebPmxAsset> assets)
    {
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase), done = new(StringComparer.OrdinalIgnoreCase), changed = new(StringComparer.OrdinalIgnoreCase);
        foreach (WebPmxAsset asset in assets.Values) Visit(asset);
        return changed;

        void Visit(WebPmxAsset asset)
        {
            if (done.Contains(asset.Entity.Id) || !visiting.Add(asset.Entity.Id)) return;
            var settings = asset.Entity.Relation;
            if (!settings.Enabled) asset.ClearRelation();
            if (settings.Enabled)
            {
                RuntimeEntity? target = scene.GetEntity(settings.RelationEntity);
                if (target is null && string.IsNullOrWhiteSpace(settings.RelationEntity))
                {
                    var candidates = assets.Values.Where(a => a != asset).ToArray();
                    if (candidates.Length == 1) target = scene.GetEntity(candidates[0].Entity.Id);
                }
                if (target is not null && target.Id != asset.Entity.Id && !visiting.Contains(target.Id) && assets.TryGetValue(target.Id, out var source))
                {
                    Visit(source);
                    if (settings.BindComponentTransform)
                    {
                        var entity = scene.GetEntity(asset.Entity.Id)!;
                        entity.Position = target.Position;
                        entity.RotationDegrees = target.RotationDegrees;
                        entity.Scale = target.Scale;
                    }
                    if (asset.ApplyRelation(source)) changed.Add(asset.Entity.Id);
                }
            }
            visiting.Remove(asset.Entity.Id);
            done.Add(asset.Entity.Id);
        }
    }
}
