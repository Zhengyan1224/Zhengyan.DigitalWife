using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public sealed class RuntimeDialogueBubble
{
    public RuntimeDialogueBubble(string name) => Name = name ?? string.Empty;
    public string Name { get; }
    public string Text { get; private set; } = string.Empty;
    public string HeaderText { get; private set; } = string.Empty;
    public string FooterText { get; private set; } = string.Empty;
    public float Width { get; set; } = 360.0f;
    public float FontSize { get; set; } = 18.0f;
    public float HeaderFontSize { get; set; } = 16.0f;
    public float FooterFontSize { get; set; } = 15.0f;
    public Vector4 BackgroundColor { get; set; } = new(0.07f, 0.08f, 0.11f, 0.92f);
    public Vector4 BorderColor { get; set; } = new(0.42f, 0.64f, 0.95f, 0.85f);
    public Vector4 HeaderTextColor { get; set; } = new(0.78f, 0.84f, 0.95f, 1.0f);
    public Vector4 TextColor { get; set; } = Vector4.One;
    public Vector4 FooterTextColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1.0f);
    public string TextAlignment { get; set; } = "left";
    public bool Visible { get; private set; }
    public string LayoutMode { get; set; } = "absolute";
    public string AnchorMode { get; set; } = "screen";
    public Vector2 ScreenPosition { get; set; } = new(220.0f, 140.0f);
    public Vector3 WorldPosition { get; set; }
    public Vector2 Pivot { get; set; } = new(0.5f, 1.0f);
    public string AnchorEntity { get; private set; } = string.Empty;
    public bool UseEntityTopAnchor { get; private set; } = true;
    public Vector3 WorldOffset { get; private set; } = new(0.0f, 0.35f, 0.0f);
    public Vector2 ScreenOffset { get; private set; } = new(0.0f, -12.0f);
    public void AttachToEntity(string entityId, bool useModelTopAnchor = true) { AnchorMode = "entity"; AnchorEntity = (entityId ?? string.Empty).Trim(); UseEntityTopAnchor = useModelTopAnchor; }
    public void UseScreenSpace(float x, float y, string? layoutMode = null) { AnchorMode = "screen"; ScreenPosition = new(x, y); if (layoutMode is not null) LayoutMode = LayoutResolver.NormalizeLayoutMode(layoutMode); }
    public void UseWorldSpace(float x, float y, float z) { AnchorMode = "world"; WorldPosition = new(x, y, z); }
    public void SetScreenPosition(float x, float y) => ScreenPosition = new(x, y);
    public void SetWorldPosition(float x, float y, float z) => WorldPosition = new(x, y, z);
    public void SetPivot(float x, float y) => Pivot = Vector2.Clamp(new(x, y), Vector2.Zero, Vector2.One);
    public void SetWorldOffset(float x, float y, float z) => WorldOffset = new Vector3(x, y, z);
    public void SetScreenOffset(float x, float y) => ScreenOffset = new Vector2(x, y);
    public void SetContent(string text, string headerText = "", string footerText = "") { Text = text ?? string.Empty; HeaderText = headerText ?? string.Empty; FooterText = footerText ?? string.Empty; }
    public void Show() => Visible = true;
    public void Hide() => Visible = false;
}
