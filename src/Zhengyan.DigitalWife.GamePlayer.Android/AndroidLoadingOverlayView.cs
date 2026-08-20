using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Small native overlay shown while a package is extracted and its first scene is loaded.</summary>
internal sealed class AndroidLoadingOverlayView : View
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private string _message = "正在加载场景...";

    public AndroidLoadingOverlayView(Context context) : base(context)
    {
        SetBackgroundColor(Color.Rgb(10, 14, 22));
        Clickable = true;
    }

    public void SetError(string message)
    {
        _message = string.IsNullOrWhiteSpace(message) ? "场景加载失败" : message;
        Invalidate();
    }

    public void ShowLoading()
    {
        _message = "正在加载场景...";
        Visibility = ViewStates.Visible;
        BringToFront();
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float cx = Width * 0.5f;
        float cy = Height * 0.5f;
        _paint.Color = Color.White;
        float density = Resources?.DisplayMetrics?.Density ?? 1f;
        _paint.TextSize = Math.Max(28f, density * 18f);
        _paint.TextAlign = Paint.Align.Center;
        canvas.DrawText(_message, cx, cy, _paint);
        _paint.Color = Color.Rgb(65, 125, 210);
        canvas.DrawRoundRect(cx - 120, cy + 28, cx + 120, cy + 36, 4, 4, _paint);
    }
}
