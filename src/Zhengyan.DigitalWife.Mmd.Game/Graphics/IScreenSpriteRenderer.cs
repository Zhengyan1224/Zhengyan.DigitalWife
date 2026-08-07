namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public interface IScreenSpriteRenderer : IDisposable
{
    void Draw(IReadOnlyList<ScreenSpriteDrawCommand> commands, int targetWidth, int targetHeight);
}
