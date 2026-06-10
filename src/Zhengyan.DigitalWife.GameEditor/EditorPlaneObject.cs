using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorPlaneObject
{
    public required GameEntity Entity { get; init; }

    public required TexturedPlaneComponent Component { get; init; }
}
