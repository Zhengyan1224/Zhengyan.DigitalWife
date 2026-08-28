using Android.Content;
using Android.Graphics;
using Android.Views;
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

    public AndroidGuiOverlayView(Context context, AndroidGameSurfaceView gameView)
        : base(context)
    {
        _gameView = gameView;
        SetWillNotDraw(false);
        Clickable = false;
        Focusable = false;
        SetLayerType(LayerType.Software, null);
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
            Vector4Dto textColor = style.TextColor;
            _paint.Color = Color.Rgb(
                (int)(Math.Clamp(textColor.X, 0, 1) * 255),
                (int)(Math.Clamp(textColor.Y, 0, 1) * 255),
                (int)(Math.Clamp(textColor.Z, 0, 1) * 255));
            _paint.TextSize = LayoutResolver.ResolveFontSize(control.LayoutMode, style.FontSize,
                Width, Height, referenceWidth, referenceHeight);
            _paint.SetTypeface(Typeface.Create(Typeface.Default, TypefaceStyle.Normal));
            string text = control.Text ?? string.Empty;
            float textWidth = _paint.MeasureText(text);
            float x = rect.X + Math.Max((rect.Width - textWidth) * 0.5f, 4.0f);
            Paint.FontMetrics metrics = _paint.GetFontMetrics()!;
            float baseline = rect.Y + (rect.Height - metrics.Bottom - metrics.Top) * 0.5f;
            canvas.DrawText(text, x, baseline, _paint);
        }
    }

    private static Color ToColor(Vector4Dto value) => Color.Argb(
        (int)(Math.Clamp(value.W, 0, 1) * 255),
        (int)(Math.Clamp(value.X, 0, 1) * 255),
        (int)(Math.Clamp(value.Y, 0, 1) * 255),
        (int)(Math.Clamp(value.Z, 0, 1) * 255));

    public void Refresh() => PostInvalidateOnAnimation();
}
