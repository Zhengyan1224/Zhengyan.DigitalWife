using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidSpriteComponent(
    GameProjectScene scene,
    GameWindowSettings window,
    Func<string, string> resolvePath,
    IRuntimeTextureProvider? runtimeTextures = null) : DrawableGameComponent
{
    private readonly Dictionary<string, ITexture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private IScreenSpriteRenderer? _renderer;

    protected override void Initialize()
    {
        _renderer = Game?.GraphicsDevice.CreateScreenSpriteRenderer()
            ?? throw new InvalidOperationException("The sprite renderer requires an attached game.");
    }

    public override void Draw(GameTime gameTime)
        => DrawSprites(gameTime, foreground: true);

    public void DrawBackground(GameTime gameTime)
        => DrawSprites(gameTime, foreground: false);

    public void DrawBackground(
        GameTime gameTime,
        int outputWidth,
        int outputHeight,
        int originX,
        int originY,
        int layoutWidth,
        int layoutHeight)
        => DrawSprites(
            gameTime,
            foreground: false,
            outputWidth,
            outputHeight,
            originX,
            originY,
            layoutWidth,
            layoutHeight);

    private void DrawSprites(
        GameTime gameTime,
        bool foreground,
        int? outputWidth = null,
        int? outputHeight = null,
        int originX = 0,
        int originY = 0,
        int? layoutWidth = null,
        int? layoutHeight = null)
    {
        _ = gameTime;
        if (_renderer is null || Game is null || scene.Sprites.Count == 0) return;

        int width = Math.Max(outputWidth ?? Game.GraphicsDevice.BackBufferSize.X, 1);
        int height = Math.Max(outputHeight ?? Game.GraphicsDevice.BackBufferSize.Y, 1);
        int resolvedLayoutWidth = Math.Max(layoutWidth ?? width, 1);
        int resolvedLayoutHeight = Math.Max(layoutHeight ?? height, 1);
        List<ScreenSpriteDrawCommand> commands = [];
        foreach (SpriteSettings sprite in scene.Sprites
            .Where(sprite => sprite.Visible && !string.IsNullOrWhiteSpace(sprite.Path)
                && (foreground ? sprite.DrawOrder >= 0 : sprite.DrawOrder < 0))
            .OrderBy(sprite => sprite.DrawOrder))
        {
            RuntimeTextureHandle handle;
            Vector4 sourceUv;
            bool runtimeTexture = sprite.Path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase);
            if (runtimeTexture)
            {
                if (runtimeTextures?.TryGetTextureHandle(sprite.Path, out handle) != true) continue;
                sourceUv = new Vector4(0, 0, 1, 1);
            }
            else
            {
                ITexture2D? texture = GetTexture(sprite.Path);
                if (texture is null) continue;
                handle = new RuntimeTextureHandle(texture.Backend, texture.LegacyTextureId, texture.NativeResource);
                sourceUv = sprite.GetSourceUv(texture.Width, texture.Height);
            }
            LayoutRect localRect = SpriteLayoutResolver.Resolve(
                sprite,
                resolvedLayoutWidth,
                resolvedLayoutHeight,
                window.Width,
                window.Height);
            LayoutRect rect = new(
                localRect.X + originX,
                localRect.Y + originY,
                localRect.Width,
                localRect.Height);
            commands.Add(new ScreenSpriteDrawCommand(
                handle,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X + Math.Max(rect.Width, 1.0f), rect.Y + Math.Max(rect.Height, 1.0f)),
                sprite.RotationDegrees,
                sprite.Opacity,
                runtimeTexture && handle.Backend == GraphicsBackend.OpenGL) { SourceUv = sourceUv });
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
