using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeSpriteControl
{
    private readonly SpriteSettings _sprite;

    internal RuntimeSpriteControl(SpriteSettings sprite)
    {
        _sprite = sprite;
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

    public void Show()
    {
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
    }
}
