using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

internal sealed class GroundShadowPassComponent(DigitalHumanGame digitalHumanGame) : DrawableGameComponent
{
    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        if (!digitalHumanGame.HasGroundShadowReceiver)
        {
            return;
        }

        foreach (Zhengyan.DigitalWife.Mmd.Game.Pmx.PmxModelComponent model in digitalHumanGame.ShadowCasterModels)
        {
            if (!model.Visible || model.DrawShadowInMainPass)
            {
                continue;
            }

            model.DrawGroundShadowPass();
        }
    }
}
