using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.MmdDemo;

internal sealed class GroundShadowPassComponent(DemoGame demoGame) : DrawableGameComponent
{
    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        foreach (Zhengyan.DigitalWife.Mmd.Game.Pmx.PmxModelComponent model in demoGame.Models)
        {
            if (!model.Visible || model.DrawShadowInMainPass)
            {
                continue;
            }

            model.DrawGroundShadowPass();
        }
    }
}
