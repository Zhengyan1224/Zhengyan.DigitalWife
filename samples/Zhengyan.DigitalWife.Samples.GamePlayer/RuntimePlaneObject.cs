using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class RuntimePlaneObject
{
    public required GameEntity Definition { get; init; }

    public required TexturedPlaneComponent Component { get; init; }
}
