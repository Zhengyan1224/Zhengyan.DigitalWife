using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class RuntimeParticleObject
{
    public required GameEntity Definition { get; init; }

    public required ParticleSystemComponent Component { get; init; }

    public required RuntimeEntity Entity { get; init; }
}
