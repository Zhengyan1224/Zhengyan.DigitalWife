using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class DesktopSpriteHitTestComponent(Func<bool> isEnabled) : DrawableGameComponent
{
    private readonly Func<bool> _isEnabled = isEnabled;
    private bool _wasEnabled;

    public override void Draw(GameTime gameTime)
    {
        if (Game is null)
        {
            return;
        }

        if (Game.GraphicsDevice.Renderer is not OpenGlRenderer)
        {
            if (_wasEnabled)
            {
                DesktopSpritePlatform.ApplyClickThrough(Game.Window, false);
                _wasEnabled = false;
            }
            return;
        }

        bool enabled = _isEnabled();
        if (!enabled)
        {
            if (_wasEnabled)
            {
                DesktopSpritePlatform.ApplyClickThrough(Game.Window, false);
                _wasEnabled = false;
            }

            return;
        }

        _wasEnabled = true;
        DesktopSpritePlatform.SyncClickThroughRegionFromFramebuffer(
            Game.Window,
            Game.GraphicsDevice.Gl,
            Game.GraphicsDevice.BackBufferSize.X,
            Game.GraphicsDevice.BackBufferSize.Y,
            enabled);
    }

    public override void Dispose()
    {
        if (Game is not null && _wasEnabled)
        {
            DesktopSpritePlatform.ApplyClickThrough(Game.Window, false);
        }

        base.Dispose();
    }
}
