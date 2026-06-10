using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorWaterObject
{
    public required GameEntity Entity { get; init; }

    public required WaterSurfaceComponent Component { get; init; }
}
