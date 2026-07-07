using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeSpriteControl
{
    private readonly SpriteSettings _sprite;
    private readonly RuntimeWindowControl _window;

    internal RuntimeSpriteControl(SpriteSettings sprite, RuntimeWindowControl window)
    {
        _sprite = sprite;
        _window = window;
    }

    public string Id => _sprite.Id;

    public string Name
    {
        get => _sprite.Name;
        set => _sprite.Name = value ?? string.Empty;
    }

    public bool Visible
    {
        get => _sprite.Visible;
        set => _sprite.Visible = value;
    }

    public float X
    {
        get => _sprite.X;
        set => _sprite.X = value;
    }

    public float Y
    {
        get => _sprite.Y;
        set => _sprite.Y = value;
    }

    public float Width
    {
        get => _sprite.Width;
        set => _sprite.Width = Math.Max(1.0f, value);
    }

    public float Height
    {
        get => _sprite.Height;
        set => _sprite.Height = Math.Max(1.0f, value);
    }

    public float RotationDegrees
    {
        get => _sprite.RotationDegrees;
        set => _sprite.RotationDegrees = value;
    }

    public float Opacity
    {
        get => _sprite.Opacity;
        set => _sprite.Opacity = Math.Clamp(value, 0.0f, 1.0f);
    }

    public int DrawOrder
    {
        get => _sprite.DrawOrder;
        set => _sprite.DrawOrder = value;
    }

    public string Texture
    {
        get => _sprite.Path;
        set => _sprite.Path = value ?? string.Empty;
    }

    public string Path
    {
        get => _sprite.Path;
        set => _sprite.Path = value ?? string.Empty;
    }

    public string LayoutMode
    {
        get => _sprite.LayoutMode;
        set => _sprite.LayoutMode = LayoutResolver.NormalizeLayoutMode(value);
    }

    public string TargetEntity
    {
        get => _sprite.TargetEntity;
        set => _sprite.TargetEntity = value ?? string.Empty;
    }

    public void SetPosition(float x, float y)
    {
        X = x;
        Y = y;
    }

    public void SetSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public void SetLayoutMode(string layoutMode)
    {
        LayoutMode = layoutMode;
    }

    public LayoutRect GetScreenRect()
    {
        return SpriteLayoutResolver.Resolve(
            _sprite,
            Math.Max(_window.ActualWidth, 1),
            Math.Max(_window.ActualHeight, 1),
            Math.Max(_window.Width, 1),
            Math.Max(_window.Height, 1));
    }

    public bool ContainsPoint(float x, float y)
    {
        return SpriteLayoutResolver.ContainsPoint(
            _sprite,
            x,
            y,
            Math.Max(_window.ActualWidth, 1),
            Math.Max(_window.ActualHeight, 1),
            Math.Max(_window.Width, 1),
            Math.Max(_window.Height, 1));
    }

    public bool ContainsMouse(RuntimeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ContainsPoint(input.MouseX, input.MouseY);
    }

    public bool ContainsTouch(RuntimeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (var touch in input.Touches)
        {
            if (touch.IsActive && ContainsPoint(touch.X, touch.Y))
            {
                return true;
            }
        }

        return false;
    }

    public void Show()
    {
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
    }

    public void SetRenderTexture(string renderTextureName)
    {
        _sprite.Path = ToRenderTextureReference(renderTextureName);
    }

    private static string ToRenderTextureReference(string renderTextureName)
    {
        string trimmed = (renderTextureName ?? string.Empty).Trim();
        return trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"rt:{trimmed}";
    }
}
