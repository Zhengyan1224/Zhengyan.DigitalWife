using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class PlayerPmxObject
{
    public required GameEntity Definition { get; init; }

    public required PmxModelComponent Model { get; init; }

    public required RuntimeEntity RuntimeEntity { get; init; }

    public List<IScriptInstance> Scripts { get; } = [];
}
