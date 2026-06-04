using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

public sealed class RuntimeDialogueBubbleManager
{
    private readonly Dictionary<string, RuntimeDialogueBubble> _bubbles = new(StringComparer.OrdinalIgnoreCase);
    private long _nextCreationOrder = 1;

    public int Count => _bubbles.Count;

    public IReadOnlyList<string> Names => [.. _bubbles.Values
        .OrderBy(item => item.CreationOrder)
        .Select(item => item.Name)];

    public IReadOnlyList<string> VisibleNames => [.. _bubbles.Values
        .Where(item => item.ShouldRender)
        .OrderBy(item => item.CreationOrder)
        .Select(item => item.Name)];

    public bool Contains(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && _bubbles.ContainsKey(name.Trim());
    }

    public RuntimeDialogueBubble Create(string name) => GetOrCreate(name);

    public RuntimeDialogueBubble GetOrCreate(string name)
    {
        string normalized = NormalizeName(name);
        if (_bubbles.TryGetValue(normalized, out RuntimeDialogueBubble? existing))
        {
            return existing;
        }

        RuntimeDialogueBubble bubble = new(normalized)
        {
            CreationOrder = _nextCreationOrder++
        };
        _bubbles.Add(normalized, bubble);
        return bubble;
    }

    public RuntimeDialogueBubble? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _bubbles.TryGetValue(name.Trim(), out RuntimeDialogueBubble? bubble) ? bubble : null;
    }

    public RuntimeDialogueBubble ShowText(string name, string text, string? headerText = null, string? footerText = null)
    {
        RuntimeDialogueBubble bubble = GetOrCreate(name);
        bubble.SetContent(text, headerText ?? string.Empty, footerText ?? string.Empty);
        bubble.Show();
        return bubble;
    }

    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _bubbles.Remove(name.Trim());
    }

    public void HideAll()
    {
        foreach (RuntimeDialogueBubble bubble in _bubbles.Values)
        {
            bubble.Hide();
        }
    }

    public void Clear()
    {
        _bubbles.Clear();
    }

    internal IReadOnlyList<RuntimeDialogueBubble> GetOrderedVisibleBubbles()
    {
        return [.. _bubbles.Values
            .Where(static item => item.ShouldRender)
            .OrderBy(item => item.DrawOrder)
            .ThenBy(item => item.CreationOrder)];
    }

    private static string NormalizeName(string name)
    {
        string normalized = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Bubble name is required.", nameof(name));
        }

        return normalized;
    }
}

public sealed class RuntimeDialogueBubble
{
    private string _layoutMode = "absolute";
    private string _anchorMode = "screen";
    private Vector2 _pivot = new(0.5f, 1.0f);
    private float _width = 360.0f;
    private float _paddingX = 14.0f;
    private float _paddingY = 10.0f;
    private float _rounding = 14.0f;
    private float _borderThickness = 1.0f;
    private float _fontSize = 18.0f;
    private float _headerFontSize = 16.0f;
    private float _footerFontSize = 15.0f;
    private string _textAlignment = "left";

    internal RuntimeDialogueBubble(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool Visible { get; set; } = true;

    public string LayoutMode
    {
        get => _layoutMode;
        set => _layoutMode = LayoutResolver.NormalizeLayoutMode(value);
    }

    public string AnchorMode
    {
        get => _anchorMode;
        set => _anchorMode = NormalizeAnchorMode(value);
    }

    public string AnchorEntity { get; set; } = string.Empty;

    public bool UseEntityTopAnchor { get; set; } = true;

    public Vector2 ScreenPosition { get; set; } = new(220.0f, 140.0f);

    public Vector2 ScreenOffset { get; set; } = new(0.0f, -12.0f);

    public Vector3 WorldPosition { get; set; } = Vector3.Zero;

    public Vector3 WorldOffset { get; set; } = new(0.0f, 0.35f, 0.0f);

    public Vector2 Pivot
    {
        get => _pivot;
        set => _pivot = new(
            Math.Clamp(value.X, 0.0f, 1.0f),
            Math.Clamp(value.Y, 0.0f, 1.0f));
    }

    public float Width
    {
        get => _width;
        set => _width = Math.Clamp(value, 80.0f, 2400.0f);
    }

    public float PaddingX
    {
        get => _paddingX;
        set => _paddingX = Math.Clamp(value, 0.0f, 200.0f);
    }

    public float PaddingY
    {
        get => _paddingY;
        set => _paddingY = Math.Clamp(value, 0.0f, 200.0f);
    }

    public float Rounding
    {
        get => _rounding;
        set => _rounding = Math.Clamp(value, 0.0f, 80.0f);
    }

    public float BorderThickness
    {
        get => _borderThickness;
        set => _borderThickness = Math.Clamp(value, 0.0f, 24.0f);
    }

    public float FontSize
    {
        get => _fontSize;
        set => _fontSize = Math.Clamp(value, 8.0f, 192.0f);
    }

    public float HeaderFontSize
    {
        get => _headerFontSize;
        set => _headerFontSize = Math.Clamp(value, 8.0f, 192.0f);
    }

    public float FooterFontSize
    {
        get => _footerFontSize;
        set => _footerFontSize = Math.Clamp(value, 8.0f, 192.0f);
    }

    public string TextAlignment
    {
        get => _textAlignment;
        set => _textAlignment = NormalizeTextAlignment(value);
    }

    public bool WordWrap { get; set; } = true;

    public int DrawOrder { get; set; }

    public string HeaderText { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string FooterText { get; set; } = string.Empty;

    public Vector4 BackgroundColor { get; set; } = new(0.07f, 0.08f, 0.11f, 0.92f);

    public Vector4 BorderColor { get; set; } = new(0.42f, 0.64f, 0.95f, 0.85f);

    public Vector4 TextColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

    public Vector4 HeaderTextColor { get; set; } = new(0.78f, 0.84f, 0.95f, 1.0f);

    public Vector4 FooterTextColor { get; set; } = new(0.72f, 0.76f, 0.82f, 1.0f);

    internal long CreationOrder { get; set; }

    internal bool ShouldRender =>
        Visible
        && (!string.IsNullOrWhiteSpace(HeaderText)
            || !string.IsNullOrWhiteSpace(Text)
            || !string.IsNullOrWhiteSpace(FooterText));

    public void Show()
    {
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
    }

    public void ClearText()
    {
        HeaderText = string.Empty;
        Text = string.Empty;
        FooterText = string.Empty;
    }

    public void SetContent(string text, string headerText = "", string footerText = "")
    {
        HeaderText = headerText ?? string.Empty;
        Text = text ?? string.Empty;
        FooterText = footerText ?? string.Empty;
    }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void SetHeaderText(string text)
    {
        HeaderText = text ?? string.Empty;
    }

    public void SetFooterText(string text)
    {
        FooterText = text ?? string.Empty;
    }

    public void UseScreenSpace(float x, float y, string? layoutMode = null)
    {
        AnchorMode = "screen";
        ScreenPosition = new Vector2(x, y);
        if (!string.IsNullOrWhiteSpace(layoutMode))
        {
            LayoutMode = layoutMode!;
        }
    }

    public void SetScreenPosition(float x, float y)
    {
        ScreenPosition = new Vector2(x, y);
    }

    public void SetScreenOffset(float x, float y)
    {
        ScreenOffset = new Vector2(x, y);
    }

    public void UseWorldSpace(float x, float y, float z)
    {
        AnchorMode = "world";
        WorldPosition = new Vector3(x, y, z);
    }

    public void SetWorldPosition(float x, float y, float z)
    {
        WorldPosition = new Vector3(x, y, z);
    }

    public void SetWorldOffset(float x, float y, float z)
    {
        WorldOffset = new Vector3(x, y, z);
    }

    public void AttachToEntity(string entityIdOrName, bool useModelTopAnchor = true)
    {
        AnchorMode = "entity";
        AnchorEntity = (entityIdOrName ?? string.Empty).Trim();
        UseEntityTopAnchor = useModelTopAnchor;
    }

    public void SetPadding(float x, float y)
    {
        PaddingX = x;
        PaddingY = y;
    }

    public void SetPivot(float x, float y)
    {
        Pivot = new Vector2(x, y);
    }

    private static string NormalizeAnchorMode(string anchorMode)
    {
        string normalized = (anchorMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "entity" or "actor" or "model" => "entity",
            "world" or "world_space" => "world",
            _ => "screen"
        };
    }

    private static string NormalizeTextAlignment(string alignment)
    {
        string normalized = (alignment ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "center" or "middle" => "center",
            "right" => "right",
            _ => "left"
        };
    }
}
