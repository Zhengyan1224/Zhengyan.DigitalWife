using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class GroundShadowPassComponent(GamePlayerGame playerGame) : DrawableGameComponent
{
    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        foreach (PlayerPmxObject item in playerGame.PmxObjects)
        {
            if (!item.Model.Visible || item.Model.DrawShadowInMainPass)
            {
                continue;
            }

            item.Model.DrawGroundShadowPass();
        }
    }
}
