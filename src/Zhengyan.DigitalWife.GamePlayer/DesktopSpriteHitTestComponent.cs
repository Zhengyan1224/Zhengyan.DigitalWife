using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class DesktopSpriteHitTestComponent(Func<bool> isEnabled) : DrawableGameComponent
{
    private readonly Func<bool> _isEnabled = isEnabled;
    private byte[] _framebufferBytes = [];
    private bool _wasEnabled;

    public override void Draw(GameTime gameTime)
    {
        if (Game is null)
        {
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
        int width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        int height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);
        int required = checked(width * height * 4);
        if (_framebufferBytes.Length != required)
        {
            _framebufferBytes = new byte[required];
        }

        if (!Game.GraphicsDevice.TryReadBackBufferRgba(_framebufferBytes))
        {
            DesktopSpritePlatform.ApplyClickThrough(Game.Window, false);
            _wasEnabled = false;
            return;
        }

        DesktopSpritePlatform.SyncClickThroughRegion(Game.Window, _framebufferBytes, width, height, enabled);
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
