using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidVulkanSpriteComponent(
    GameProjectScene scene,
    GameWindowSettings window,
    Func<string, string> resolvePath) : DrawableGameComponent
{
    private readonly Dictionary<string, ITexture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private IScreenSpriteRenderer? _renderer;

    protected override void Initialize()
    {
        _renderer = Game?.GraphicsDevice.CreateScreenSpriteRenderer()
            ?? throw new InvalidOperationException("The Vulkan sprite renderer requires an attached game.");
    }

    public override void Draw(GameTime gameTime)
        => DrawSprites(gameTime, foreground: true);

    public void DrawBackground(GameTime gameTime)
        => DrawSprites(gameTime, foreground: false);

    private void DrawSprites(GameTime gameTime, bool foreground)
    {
        _ = gameTime;
        if (_renderer is null || Game is null || scene.Sprites.Count == 0) return;

        int width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        int height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);
        List<ScreenSpriteDrawCommand> commands = [];
        foreach (SpriteSettings sprite in scene.Sprites
            .Where(sprite => sprite.Visible && !string.IsNullOrWhiteSpace(sprite.Path)
                && (foreground ? sprite.DrawOrder >= 0 : sprite.DrawOrder < 0))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            ITexture2D? texture = GetTexture(sprite.Path);
            if (texture is null) continue;
            LayoutRect rect = SpriteLayoutResolver.Resolve(sprite, width, height, window.Width, window.Height);
            commands.Add(new ScreenSpriteDrawCommand(
                new RuntimeTextureHandle(texture.Backend, texture.LegacyTextureId, texture.NativeResource),
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X + Math.Max(rect.Width, 1.0f), rect.Y + Math.Max(rect.Height, 1.0f)),
                sprite.RotationDegrees,
                sprite.Opacity,
                false) { SourceUv = sprite.GetSourceUv(texture.Width, texture.Height) });
        }

        _renderer.Draw(commands, width, height);
    }

    public override void Dispose()
    {
        _renderer?.Dispose();
        _renderer = null;
        foreach (ITexture2D texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        base.Dispose();
    }

    private ITexture2D? GetTexture(string path)
    {
        string resolved = resolvePath(path);
        if (_textures.TryGetValue(resolved, out ITexture2D? texture)) return texture;
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved)) return null;

        texture = Game!.GraphicsDevice.CreateTexture2D();
        try
        {
            texture.LoadFromFile(resolved);
            _textures[resolved] = texture;
            return texture;
        }
        catch
        {
            texture.Dispose();
            return null;
        }
    }
}
