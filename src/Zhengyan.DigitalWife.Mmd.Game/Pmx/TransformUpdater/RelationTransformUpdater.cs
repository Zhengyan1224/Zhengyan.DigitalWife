using Zhengyan.DigitalWife.Mmd;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

public sealed class RelationTransformUpdater : ITransformUpdater
{
    private readonly PmxModelComponent _relationComponent;
    private readonly StringComparer _boneNameComparer;
    private readonly List<BoneBinding> _bindings = [];

    private MMDModel? _cachedTargetModel;
    private MMDModel? _cachedRelationModel;

    public RelationTransformUpdater(
        PmxModelComponent relationComponent,
        bool bindComponentTransform = true,
        StringComparer? boneNameComparer = null)
    {
        _relationComponent = relationComponent ?? throw new ArgumentNullException(nameof(relationComponent));
        _boneNameComparer = boneNameComparer ?? StringComparer.Ordinal;
        BindComponentTransform = bindComponentTransform;
    }

    public TransformUpdaterStage Stage => TransformUpdaterStage.PostAnimation;

    public bool Enabled { get; set; } = true;

    public bool BindComponentTransform { get; set; }

    public bool BindLighting { get; set; }

    public PmxModelComponent RelationComponent => _relationComponent;

    public bool UpdateTransform(PmxModelComponent component, float elapsedSeconds)
    {
        _ = elapsedSeconds;

        MMDModel? targetModel = component.Model;
        MMDModel? relationModel = _relationComponent.Model;
        if (targetModel is null || relationModel is null)
        {
            return false;
        }

        RebuildBindingsIfNeeded(targetModel, relationModel);

        foreach (BoneBinding binding in _bindings)
        {
            binding.Target.Global = binding.Relation.Global;
            binding.Target.Local = binding.Relation.Local;
        }

        if (BindComponentTransform)
        {
            component.Position = _relationComponent.Position;
            component.Scale = _relationComponent.Scale;
            component.Rotation = _relationComponent.Rotation;
        }

        if (BindLighting)
        {
            component.LightColor = _relationComponent.LightColor;
            component.AmbientLightColor = _relationComponent.AmbientLightColor;
            component.AmbientLightStrength = _relationComponent.AmbientLightStrength;
            component.LightDirection = _relationComponent.LightDirection;
            component.ShadowColor = _relationComponent.ShadowColor;
        }

        return false;
    }

    private void RebuildBindingsIfNeeded(MMDModel targetModel, MMDModel relationModel)
    {
        if (ReferenceEquals(_cachedTargetModel, targetModel) && ReferenceEquals(_cachedRelationModel, relationModel))
        {
            return;
        }

        _cachedTargetModel = targetModel;
        _cachedRelationModel = relationModel;
        _bindings.Clear();

        Dictionary<string, MMDNode> relationNodes = new(_boneNameComparer);
        foreach (MMDNode node in relationModel.GetNodes())
        {
            if (!relationNodes.ContainsKey(node.Name))
            {
                relationNodes.Add(node.Name, node);
            }
        }

        foreach (MMDNode node in targetModel.GetNodes())
        {
            if (relationNodes.TryGetValue(node.Name, out MMDNode? relationNode))
            {
                _bindings.Add(new BoneBinding(node, relationNode));
            }
        }
    }

    private readonly record struct BoneBinding(MMDNode Target, MMDNode Relation);
}

