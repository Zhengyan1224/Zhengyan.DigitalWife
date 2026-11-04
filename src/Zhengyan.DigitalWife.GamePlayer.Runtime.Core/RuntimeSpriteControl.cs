using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Shared script-facing wrapper for a scene sprite.</summary>
public sealed class RuntimeSpriteControl
{
    private readonly SpriteSettings _sprite;

    internal RuntimeSpriteControl(SpriteSettings sprite) => _sprite = sprite;

    public string Id => _sprite.Id;
    public string Name { get => _sprite.Name; set => _sprite.Name = value ?? string.Empty; }
    public string Path { get => _sprite.Path; set => _sprite.Path = value ?? string.Empty; }
    public bool Visible { get => _sprite.Visible; set => _sprite.Visible = value; }
    public float X { get => _sprite.X; set => _sprite.X = value; }
    public float Y { get => _sprite.Y; set => _sprite.Y = value; }
    public float Width { get => _sprite.Width; set => _sprite.Width = Math.Max(value, 1.0f); }
    public float Height { get => _sprite.Height; set => _sprite.Height = Math.Max(value, 1.0f); }
    public float Opacity { get => _sprite.Opacity; set => _sprite.Opacity = Math.Clamp(value, 0.0f, 1.0f); }
    public float RotationDegrees { get => _sprite.RotationDegrees; set => _sprite.RotationDegrees = value; }
}
