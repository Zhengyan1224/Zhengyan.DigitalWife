using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed class EditorPmxObject
{
    public required GameEntity Entity { get; init; }

    public required PmxModelComponent Model { get; init; }

    public RelationTransformUpdater? RelationUpdater { get; set; }
}
