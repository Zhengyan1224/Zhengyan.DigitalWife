using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public class RuntimeSpriteControl
{
    private readonly SpriteSettings _sprite;
    private readonly RuntimeWindowControl _window;
    private readonly Func<(float Width,float Height,float ReferenceWidth,float ReferenceHeight)>? _viewport;
    protected virtual (float Width,float Height,float ReferenceWidth,float ReferenceHeight) ScriptViewport => _viewport?.Invoke() ?? (_window.ActualWidth,_window.ActualHeight,_window.Width,_window.Height);
    public RuntimeSpriteControl(SpriteSettings sprite):this(sprite,null,null) { }

    public RuntimeSpriteControl(SpriteSettings sprite, RuntimeWindowControl? window = null, Func<(float Width,float Height,float ReferenceWidth,float ReferenceHeight)>? viewport = null)
    {
        _sprite = sprite;
        _window = window ?? new RuntimeWindowControl();
        _viewport = viewport;
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

    public float SourceX
    {
        get => _sprite.SourceX;
        set => _sprite.SourceX = Math.Max(0.0f, value);
    }

    public float SourceY
    {
        get => _sprite.SourceY;
        set => _sprite.SourceY = Math.Max(0.0f, value);
    }

    public float SourceWidth
    {
        get => _sprite.SourceWidth;
        set => _sprite.SourceWidth = Math.Max(0.0f, value);
    }

    public float SourceHeight
    {
        get => _sprite.SourceHeight;
        set => _sprite.SourceHeight = Math.Max(0.0f, value);
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

    public void SetSourceRect(float x, float y, float width, float height)
    {
        SourceX = x;
        SourceY = y;
        SourceWidth = width;
        SourceHeight = height;
    }

    public void ResetSourceRect()
    {
        SetSourceRect(0.0f, 0.0f, 0.0f, 0.0f);
    }

    public void SetLayoutMode(string layoutMode)
    {
        LayoutMode = layoutMode;
    }

    public LayoutRect GetScreenRect()
    {
        return SpriteLayoutResolver.Resolve(
            _sprite,
            Math.Max(ScriptViewport.Width, 1),
            Math.Max(ScriptViewport.Height, 1),
            Math.Max(ScriptViewport.ReferenceWidth, 1),
            Math.Max(ScriptViewport.ReferenceHeight, 1));
    }

    public bool ContainsPoint(float x, float y)
    {
        return SpriteLayoutResolver.ContainsPoint(
            _sprite,
            x,
            y,
            Math.Max(ScriptViewport.Width, 1),
            Math.Max(ScriptViewport.Height, 1),
            Math.Max(ScriptViewport.ReferenceWidth, 1),
            Math.Max(ScriptViewport.ReferenceHeight, 1));
    }

    public bool ContainsMouse(RuntimeCameraInput input)
    {
        return ContainsPoint(input.MouseX, input.MouseY);
    }

    public bool ContainsTouch(RuntimeCameraInput input)
    {
        foreach (var touch in input.Touches ?? [])
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
