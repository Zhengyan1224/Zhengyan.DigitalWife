using Android.Content;
using Android.Graphics;
using Android.Views;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>
/// Android native text layer. GLES remains responsible for backgrounds and sprites;
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
            Vector4Dto textColor = control.Style.TextColor;
            _paint.Color = Color.Rgb(
                (int)(Math.Clamp(textColor.X, 0, 1) * 255),
                (int)(Math.Clamp(textColor.Y, 0, 1) * 255),
                (int)(Math.Clamp(textColor.Z, 0, 1) * 255));
            _paint.TextSize = LayoutResolver.ResolveFontSize(control.LayoutMode, control.Style.FontSize,
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

    public void Refresh() => PostInvalidateOnAnimation();
}
