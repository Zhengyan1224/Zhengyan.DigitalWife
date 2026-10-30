namespace Zhengyan.DigitalWife.GameProjects;

public static class GuiPointerHitTest
{
    public static bool Contains(GameProjectScene scene, float x, float y,
        float actualWidth, float actualHeight, float referenceWidth, float referenceHeight)
    {
        foreach (GuiControlSettings control in scene.GuiControls.Where(control => control.Visible))
        {
            LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y,
                control.Width, control.Height, actualWidth, actualHeight, referenceWidth, referenceHeight);
            if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height)
                return true;
        }
        return scene.Sprites.Any(sprite => sprite.Visible && SpriteLayoutResolver.ContainsPoint(sprite,
            x, y, actualWidth, actualHeight, referenceWidth, referenceHeight));
    }
}
