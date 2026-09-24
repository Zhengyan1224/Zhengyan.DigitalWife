using Android.Content;
using Android.Graphics;
using Android.Views;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidLoadingOverlayView : View
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private string _message = "Loading scene...";
    private float _progress;
    private LoadingScreenSettings _settings = new();
    private Bitmap? _background;

    public AndroidLoadingOverlayView(Context context) : base(context)
    {
        Clickable = true;
    }

    public void Configure(LoadingScreenSettings settings, string projectDirectory)
    {
        _settings = settings ?? new LoadingScreenSettings();
        _background?.Recycle();
        _background = null;
        if (!string.IsNullOrWhiteSpace(_settings.BackgroundImagePath))
        {
            string path = GameProjectPath.ToAbsolute(projectDirectory, _settings.BackgroundImagePath);
            if (File.Exists(path)) _background = BitmapFactory.DecodeFile(path);
        }
        Invalidate();
    }

    public void SetError(string message)
    {
        _message = string.IsNullOrWhiteSpace(message) ? "Scene loading failed" : message;
        _progress = 0.0f;
        Visibility = ViewStates.Visible;
        Invalidate();
    }

    public void SetProgress(float progress, string message)
    {
        _progress = Math.Clamp(progress, 0.0f, 1.0f);
        if (!string.IsNullOrWhiteSpace(message)) _message = message;
        Visibility = ViewStates.Visible;
        Invalidate();
    }

    public void ShowLoading()
    {
        _message = "Loading scene...";
        _progress = 0.0f;
        Visibility = ViewStates.Visible;
        BringToFront();
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        Vector4Dto background = _settings.BackgroundColor;
        canvas.DrawColor(ToColor(background));
        if (_background is not null && !_background.IsRecycled)
        {
            _paint.Alpha = (int)(Math.Clamp(_settings.BackgroundImageOpacity, 0.0f, 1.0f) * 255.0f);
            canvas.DrawBitmap(_background, null, new Rect(0, 0, Width, Height), _paint);
            _paint.Alpha = 255;
        }

        float centerX = Width * 0.5f;
        float centerY = Height * 0.5f;
        float density = Resources?.DisplayMetrics?.Density ?? 1.0f;
        _paint.Color = Color.White;
        _paint.TextSize = Math.Max(28.0f, density * 18.0f);
        _paint.TextAlign = Paint.Align.Center;
        canvas.DrawText(_message, centerX, centerY, _paint);

        LoadingProgressBarSettings progress = _settings.ProgressBar;
        if (!progress.Visible) return;
        LayoutRect rect = LayoutResolver.Resolve(progress.LayoutMode, progress.X, progress.Y,
            progress.Width, progress.Height, Width, Height, 1280.0f, 720.0f);
        _paint.Color = ToColor(progress.TrackColor);
        canvas.DrawRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height, _paint);
        _paint.Color = ToColor(progress.FillColor);
        canvas.DrawRect(rect.X, rect.Y, rect.X + rect.Width * _progress, rect.Y + rect.Height, _paint);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _background?.Recycle();
            _background = null;
        }
        base.Dispose(disposing);
    }

    private static Color ToColor(Vector4Dto value) => Color.Argb(
        (int)(Math.Clamp(value.W, 0.0f, 1.0f) * 255.0f),
        (int)(Math.Clamp(value.X, 0.0f, 1.0f) * 255.0f),
        (int)(Math.Clamp(value.Y, 0.0f, 1.0f) * 255.0f),
        (int)(Math.Clamp(value.Z, 0.0f, 1.0f) * 255.0f));
}
