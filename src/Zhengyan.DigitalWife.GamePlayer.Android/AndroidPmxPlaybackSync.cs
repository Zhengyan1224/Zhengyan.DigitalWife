using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidPmxPlaybackSync
{
    internal static void Apply(GameEntity definition, PmxModelComponent model, bool initialize)
    {
        model.IsPlaying = definition.IsPlaying;
        model.PlaybackSpeed = definition.PlaybackSpeed;
        model.LoopMotion = definition.LoopMotion;
        model.EnablePhysical = definition.EnablePhysics;
        model.PhysicsGravity = definition.PhysicsGravity;
        // This setter changes every motion layer, rather than only the default.
        // Apply it before loading initial layers; subsequent script changes are
        // applied directly to the model by AndroidScriptEntity.
        if (initialize) model.ResetPhysicsOnMotionLoop = definition.ResetPhysicsOnMotionLoop;
    }
}
