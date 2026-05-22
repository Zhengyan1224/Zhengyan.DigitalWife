using Zhengyan.DigitalWife.Mmd.Game;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed class GroundShadowPassComponent(GameEditorGame editorGame) : DrawableGameComponent
{
    public override void Draw(GameTime gameTime)
    {
        _ = gameTime;

        foreach (EditorPmxObject item in editorGame.PmxObjects)
        {
            if (!item.Model.Visible || item.Model.DrawShadowInMainPass)
            {
                continue;
            }

            item.Model.DrawGroundShadowPass();
        }
    }
}
