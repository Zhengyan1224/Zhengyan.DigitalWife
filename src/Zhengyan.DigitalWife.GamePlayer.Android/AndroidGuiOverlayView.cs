using Android.Content;
using Android.Graphics;
using Android.Views;
using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>
/// Android native text layer. The selected graphics backend remains responsible for backgrounds and sprites;
/// Canvas supplies device-independent font rasterization and text input visuals.
/// </summary>
internal sealed class AndroidGuiOverlayView : View
{
    private readonly Paint _paint = new() { AntiAlias = true, SubpixelText = true };
    private readonly AndroidGameSurfaceView _gameView;
    private readonly Dictionary<string, (BubbleLayoutKey Key, AndroidBubbleTextLayout Layout)> _bubbleLayouts = new();
    private sealed record BubbleLayoutKey(string Text, string Header, string Footer, string Mode, string Alignment,
        float Width, float FontSize, float HeaderSize, float FooterSize, int CanvasWidth, int CanvasHeight,
        float ReferenceWidth, float ReferenceHeight);

    public GuiControlSettings? EditingControl { get; set; }

    public AndroidGuiOverlayView(Context context, AndroidGameSurfaceView gameView)
        : base(context)
    {
        _gameView = gameView;
        SetWillNotDraw(false);
        Clickable = false;
        Focusable = false;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        GameProject? project = _gameView.Project;
        if (project is null)
        {
            return;
        }

        float referenceWidth = Math.Max(project.Window.Width, 1);
        float referenceHeight = Math.Max(project.Window.Height, 1);
        foreach (GuiControlSettings control in project.Scene.GuiControls.Where(control => control.Visible))
        {
            LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                Width, Height, referenceWidth, referenceHeight);
            GuiControlStyleSettings style = control.Style;
            float layoutScale = string.Equals(control.LayoutMode, "relative", StringComparison.OrdinalIgnoreCase)
                ? MathF.Min(Width / referenceWidth, Height / referenceHeight)
                : 1.0f;
            float radius = Math.Clamp(Math.Max(0.0f, style.Rounding * layoutScale), 0.0f, MathF.Min(rect.Width, rect.Height) * 0.5f);
            float stroke = Math.Clamp(Math.Max(0.0f, style.BorderThickness * layoutScale), 0.0f, MathF.Min(rect.Width, rect.Height) * 0.5f);
            using Paint panel = new() { AntiAlias = true };
            panel.Color = ToColor(style.BackgroundColor);
            panel.SetStyle(Paint.Style.Fill);
            canvas.DrawRoundRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height, radius, radius, panel);
            if (stroke > 0.0f)
            {
                panel.Color = ToColor(style.BorderColor);
                panel.SetStyle(Paint.Style.Stroke);
                panel.StrokeWidth = stroke;
                canvas.DrawRoundRect(rect.X + stroke * 0.5f, rect.Y + stroke * 0.5f,
                    rect.X + rect.Width - stroke * 0.5f, rect.Y + rect.Height - stroke * 0.5f, radius, radius, panel);
            }
            if (string.Equals(control.Type, "progress", StringComparison.OrdinalIgnoreCase))
            {
                panel.Color = ToColor(style.HoverColor);
                panel.SetStyle(Paint.Style.Fill);
                float progressWidth = rect.Width * Math.Clamp(control.Progress, 0.0f, 1.0f);
                canvas.DrawRoundRect(rect.X, rect.Y, rect.X + progressWidth, rect.Y + rect.Height, radius, radius, panel);
            }
            // The native editor draws the active text, while this layer keeps
            // drawing its original panel. Never blend two copies of the text.
            if (ReferenceEquals(control, EditingControl)) continue;
            _paint.Color = ToColor(style.TextColor);
            _paint.TextSize = LayoutResolver.ResolveFontSize(control.LayoutMode, style.FontSize,
                Width, Height, referenceWidth, referenceHeight);
            _paint.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Normal));
            string text = control.Text ?? string.Empty;
            float textWidth = _paint.MeasureText(text);
            float x = style.HorizontalAlignment.ToLowerInvariant() switch
            {
                "left" => rect.X + 4.0f,
                "right" => rect.X + Math.Max(rect.Width - textWidth - 4.0f, 4.0f),
                _ => rect.X + Math.Max((rect.Width - textWidth) * 0.5f, 4.0f)
            };
            Paint.FontMetrics metrics = _paint.GetFontMetrics()!;
            float baseline = style.VerticalAlignment.ToLowerInvariant() switch
            {
                "top" => rect.Y + 4.0f - metrics.Top,
                "bottom" => rect.Y + rect.Height - 4.0f - metrics.Bottom,
                _ => rect.Y + (rect.Height - metrics.Bottom - metrics.Top) * 0.5f
            };
            canvas.DrawText(text, x, baseline, _paint);
        }

        RuntimeDialogueBubble[] visibleBubbles = AndroidScriptBubbleManager.Shared.VisibleBubbles.ToArray();
        HashSet<string> visibleNames = visibleBubbles.Select(bubble => bubble.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in _bubbleLayouts.Keys.Where(name => !visibleNames.Contains(name)).ToArray()) _bubbleLayouts.Remove(name);
        foreach (RuntimeDialogueBubble bubble in visibleBubbles)
        {
            if (!_gameView.TryGetBubblePosition(bubble, Width, Height, out var anchor)) continue;
            BubbleLayoutKey key = new(bubble.Text, bubble.HeaderText, bubble.FooterText, bubble.LayoutMode,
                bubble.TextAlignment, bubble.Width, bubble.FontSize, bubble.HeaderFontSize, bubble.FooterFontSize,
                Width, Height, referenceWidth, referenceHeight);
            if (!_bubbleLayouts.TryGetValue(bubble.Name, out var cached) || cached.Key != key)
            {
                AndroidBubbleTextLayout measured = AndroidDialogueBubbleTextLayout.Build(bubble,
                    new(Width, Height), new(referenceWidth, referenceHeight),
                    (text, size) => { _paint.TextSize = size; return _paint.MeasureText(text); },
                    size =>
                    {
                        _paint.TextSize = size;
                        Paint.FontMetrics metrics = _paint.GetFontMetrics()!;
                        return new(metrics.Top, metrics.Bottom, _paint.FontSpacing);
                    });
                cached = (key, measured);
                _bubbleLayouts[bubble.Name] = cached;
            }
            AndroidBubbleTextLayout layout = cached.Layout;
            float width = layout.Size.X;
            float height = layout.Size.Y;
            var topLeft = AndroidDialogueBubbleLayout.TopLeft(bubble, anchor, new(width, height));
            float left = topLeft.X;
            float top = topLeft.Y;
            using Paint panel = new() { AntiAlias = true, Color = ToColor(bubble.BackgroundColor) };
            panel.SetStyle(Paint.Style.Fill);
            canvas.DrawRoundRect(left, top, left + width, top + height, 12, 12, panel);
            panel.Color = ToColor(bubble.BorderColor); panel.SetStyle(Paint.Style.Stroke); panel.StrokeWidth = 2;
            canvas.DrawRoundRect(left, top, left + width, top + height, 12, 12, panel);
            int saved = canvas.Save();
            canvas.ClipRect(left + layout.Padding, top + layout.Padding,
                left + width - layout.Padding, top + height - layout.Padding);
            foreach (AndroidBubbleTextLine line in layout.Lines)
            {
                _paint.TextSize = line.FontSize;
                _paint.Color = ToColor(line.Part switch
                {
                    0 => bubble.HeaderTextColor,
                    2 => bubble.FooterTextColor,
                    _ => bubble.TextColor
                });
                canvas.DrawText(line.Text, left + line.X, top + line.Baseline, _paint);
            }
            canvas.RestoreToCount(saved);
        }
    }

    private static Color ToColor(Vector4Dto value) => Color.Argb(
        (int)(Math.Clamp(value.W, 0, 1) * 255),
        (int)(Math.Clamp(value.X, 0, 1) * 255),
        (int)(Math.Clamp(value.Y, 0, 1) * 255),
        (int)(Math.Clamp(value.Z, 0, 1) * 255));

    private static Color ToColor(System.Numerics.Vector4 value) => Color.Argb(
        (int)(Math.Clamp(value.W, 0, 1) * 255),
        (int)(Math.Clamp(value.X, 0, 1) * 255),
        (int)(Math.Clamp(value.Y, 0, 1) * 255),
        (int)(Math.Clamp(value.Z, 0, 1) * 255));

    public void Refresh() => PostInvalidateOnAnimation();
}
