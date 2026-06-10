using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.GameEditor;

internal sealed class EditorParticleObject
{
    public required GameEntity Entity { get; init; }

    public required ParticleSystemComponent Component { get; init; }
}
