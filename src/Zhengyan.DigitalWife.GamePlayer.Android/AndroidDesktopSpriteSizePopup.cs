using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidDesktopSpriteSizePopup(FrameLayout root, AndroidGameSurfaceView game, Action<int> changeSize) : IDisposable
{
    private PopupWindow? _popup;

    public void Show(int percent, int maximumPercent)
    {
        Dismiss();
        if (!root.IsAttachedToWindow) return;
        int Dp(int value) => Math.Max(1, (int)(value * (root.Resources?.DisplayMetrics?.Density ?? 1)));
        int minimumPercent = AndroidDesktopSpritePolicy.MinimumScalePercent;
        maximumPercent = Math.Max(minimumPercent, maximumPercent);
        percent = Math.Clamp(percent, minimumPercent, maximumPercent);
        using Rect frame = new();
        root.GetWindowVisibleDisplayFrame(frame);
        if (frame.Width() <= 0 || frame.Height() <= 0)
            frame.Set(0, 0, root.Resources?.DisplayMetrics?.WidthPixels ?? root.Width,
                root.Resources?.DisplayMetrics?.HeightPixels ?? root.Height);
        int width = Math.Clamp(Dp(280), 1, Math.Max(1, frame.Width()));
        int height = Math.Clamp(Dp(144), 1, Math.Max(1, frame.Height()));
        LinearLayout panel = new(root.Context) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        TextView label = new(root.Context) { Gravity = GravityFlags.CenterVertical };
        label.SetTextColor(Color.White);
        label.SetTextSize(ComplexUnitType.Sp, 14);
        string Description(int value) => value == 100 ? "大小：100%（默认）"
            : value == maximumPercent ? $"大小：最大（约 {value}%）" : $"大小：{value}%";
        label.Text = Description(percent);
        panel.AddView(label, new LinearLayout.LayoutParams(-1, Dp(28)));
        SeekBar slider = new(root.Context)
        {
            Max = maximumPercent - minimumPercent,
            Progress = percent - minimumPercent,
            ContentDescription = "桌面精灵大小",
            Enabled = maximumPercent > minimumPercent
        };
        slider.ProgressChanged += (_, args) =>
        {
            int value = minimumPercent + args.Progress;
            label.Text = Description(value);
            if (args.FromUser) changeSize(value);
        };
        panel.AddView(slider, new LinearLayout.LayoutParams(-1, Dp(44)));
        LinearLayout actions = new(root.Context) { Orientation = Orientation.Horizontal };
        Button reset = new(root.Context) { Text = "恢复默认", ContentDescription = "恢复默认大小" };
        reset.SetPadding(0, 0, 0, 0);
        reset.SetTextColor(Color.White);
        reset.SetBackgroundColor(Color.Transparent);
        reset.Click += (_, _) =>
        {
            int value = Math.Clamp(100, minimumPercent, maximumPercent);
            slider.Progress = value - minimumPercent;
            changeSize(value);
        };
        actions.AddView(reset, new LinearLayout.LayoutParams(0, Dp(40), 1));
        Button done = new(root.Context) { Text = "完成", ContentDescription = "关闭大小调节" };
        done.SetPadding(0, 0, 0, 0);
        done.SetTextColor(Color.White);
        done.SetBackgroundColor(Color.Transparent);
        done.Click += (_, _) => Dismiss();
        actions.AddView(done, new LinearLayout.LayoutParams(0, Dp(40), 1));
        panel.AddView(actions, new LinearLayout.LayoutParams(-1, Dp(40)));
        PopupWindow popup = new(panel, width, height, true)
        {
            OutsideTouchable = true, ClippingEnabled = true, InputMethodMode = InputMethod.NotNeeded, Elevation = 0
        };
        GradientDrawable background = new();
        background.SetColor(Color.Argb(245, 20, 30, 45));
        background.SetCornerRadius(Dp(8));
        popup.SetBackgroundDrawable(background);
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) { popup.IsClippedToScreen = true; popup.IsLaidOutInScreen = true; }
        if (root.Context is not global::Android.App.Activity)
            popup.WindowLayoutType = OperatingSystem.IsAndroidVersionAtLeast(26) ? WindowManagerTypes.ApplicationOverlay : WindowManagerTypes.Phone;
        _popup = popup;
        popup.DismissEvent += (_, _) => Cleanup(popup);
        try
        {
            int[] location = new int[2];
            root.GetLocationOnScreen(location);
            int left = Math.Clamp(location[0] + root.Width - width, frame.Left, Math.Max(frame.Left, frame.Right - width));
            int top = Math.Clamp(location[1] + Dp(32), frame.Top, Math.Max(frame.Top, frame.Bottom - height));
            // A separate window stays fixed under the finger while the sprite changes size and position.
            game.CancelInput();
            game.BeginContextMenuInteraction();
            popup.ShowAtLocation(root, GravityFlags.Top | GravityFlags.Left, left, top);
        }
        catch (Exception ex) { Cleanup(popup); Log.Warn("ZhengyanDesktopSprite", "Size panel: " + ex); }
    }

    public void Dismiss()
    {
        if (_popup is not { } popup) return;
        popup.Dismiss();
        Cleanup(popup);
    }

    private void Cleanup(PopupWindow popup)
    {
        if (!ReferenceEquals(_popup, popup)) return;
        _popup = null;
        game.EndContextMenuInteraction();
        popup.Dispose();
    }

    public void Dispose() => Dismiss();
}
