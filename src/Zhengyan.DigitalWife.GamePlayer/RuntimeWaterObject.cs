using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class RuntimeWaterObject
{
    public required GameEntity Definition { get; init; }

    public required WaterSurfaceComponent Component { get; init; }
}
