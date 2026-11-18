using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidContextMenuPopup(FrameLayout root, AndroidGameSurfaceView game) : IDisposable
{
    private PopupWindow? _popup;
    private GameProjectScene? _scene;
    private ContextMenuSettings? _menu;
    private readonly int[] _location = new int[2];
    private int _originX, _originY, _width, _height;

    public void Show(ContextMenuSelection selection, float x, float y)
    {
        var menu = selection.Menu;
        Dismiss();
        if (!root.IsAttachedToWindow || game.Project is not { } project) { game.EndContextMenuInteraction(); return; }
        _scene = project.Scene;
        _menu = menu;
        ContextMenuLayout layout = ContextMenuLayout.Resolve(menu, game.Width, game.Height, project.Window);
        using Rect frame = new();
        root.GetWindowVisibleDisplayFrame(frame);
        if (frame.Width() <= 0 || frame.Height() <= 0)
            frame.Set(0, 0, root.Resources?.DisplayMetrics?.WidthPixels ?? root.Width, root.Resources?.DisplayMetrics?.HeightPixels ?? root.Height);
        int width = Math.Clamp((int)Math.Ceiling(layout.Width), 1, Math.Max(1, frame.Width()));
        int height = Math.Clamp((int)Math.Ceiling(layout.Height(menu.Items.Count)), 1, Math.Max(1, frame.Height()));
        LinearLayout panel = new(root.Context) { Orientation = Orientation.Vertical };
        panel.SetPadding((int)Math.Ceiling(layout.PaddingX + layout.BorderThickness), (int)Math.Ceiling(layout.PaddingY + layout.BorderThickness),
            (int)Math.Ceiling(layout.PaddingX + layout.BorderThickness), (int)Math.Ceiling(layout.PaddingY + layout.BorderThickness));
        ScrollView scroll = new(root.Context) { FillViewport = false };
        LinearLayout items = new(root.Context) { Orientation = Orientation.Vertical };
        scroll.AddView(items, new ScrollView.LayoutParams(-1, -2));
        panel.AddView(scroll, new LinearLayout.LayoutParams(-1, -1));
        PopupWindow popup = new(panel, width, height, true) { OutsideTouchable = true, ClippingEnabled = true,
            InputMethodMode = InputMethod.NotNeeded, Elevation = 0 };
        popup.SetBackgroundDrawable(Background(menu.Style.BackgroundColor, layout.Rounding, layout.BorderThickness, menu.Style.BorderColor));
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) { popup.IsClippedToScreen = true; popup.IsLaidOutInScreen = true; }
        // A separate overlay window keeps the menu usable beyond the sprite's alpha touch region.
        if (root.Context is not global::Android.App.Activity)
            popup.WindowLayoutType = OperatingSystem.IsAndroidVersionAtLeast(26) ? WindowManagerTypes.ApplicationOverlay : WindowManagerTypes.Phone;
        _popup = popup;
        GameProjectScene scene = _scene;
        foreach (ContextMenuItemSettings item in menu.Items)
        {
            TextView row = new(root.Context) { Text = string.IsNullOrWhiteSpace(item.Text) ? "Menu Item" : item.Text,
                Enabled = item.Enabled, Clickable = item.Enabled, Focusable = item.Enabled, Gravity = GravityFlags.CenterVertical | GravityFlags.Left,
                Alpha = item.Enabled ? 1 : .6f, ContentDescription = item.Text };
            row.SetSingleLine(true);
            row.SetTextSize(ComplexUnitType.Px, layout.FontSize);
            row.SetTextColor(ToColor(menu.Style.TextColor));
            StateListDrawable states = new();
            states.AddState([global::Android.Resource.Attribute.StatePressed], Background(menu.Style.ActiveColor));
            states.AddState([global::Android.Resource.Attribute.StateHovered], Background(menu.Style.HoverColor));
            states.AddState([global::Android.Resource.Attribute.StateFocused], Background(menu.Style.HoverColor));
            states.AddState([], Background(new(0, 0, 0, 0)));
            row.Background = states;
            row.Click += (_, _) =>
            {
                if (!ReferenceEquals(scene, game.Project?.Scene) || !scene.ContextMenus.Contains(menu)
                    || !menu.Items.Contains(item) || !menu.Enabled || !item.Enabled) { Dismiss(); return; }
                Dismiss();
                game.DispatchContextMenuItem(selection, item, x, y);
            };
            items.AddView(row, new LinearLayout.LayoutParams(-1, Math.Max(1, (int)Math.Ceiling(layout.ItemHeight))));
        }
        popup.DismissEvent += (_, _) => Cleanup(popup);
        try
        {
            game.GetLocationOnScreen(_location);
            _originX = _location[0]; _originY = _location[1]; _width = game.Width; _height = game.Height;
            int left = Math.Clamp(_originX + (int)x, frame.Left, Math.Max(frame.Left, frame.Right - width));
            int top = Math.Clamp(_originY + (int)y, frame.Top, Math.Max(frame.Top, frame.Bottom - height));
            game.BeginContextMenuInteraction();
            popup.ShowAtLocation(root, GravityFlags.Top | GravityFlags.Left, left, top);
            game.NotifyContextMenuOpened(selection, x, y);
            global::Android.Util.Log.Info("ZhengyanContextMenu", $"Opened '{menu.Name}'; target={selection.Target.TargetType}:{selection.Target.TargetId}; point={x:F0},{y:F0}");
        }
        catch (Exception ex) { Cleanup(popup); global::Android.Util.Log.Warn("ZhengyanContextMenu", ex.ToString()); }
    }

    public void Validate()
    {
        if (_popup is null) return;
        game.GetLocationOnScreen(_location);
        if (_popup is not null && (!ReferenceEquals(_scene, game.Project?.Scene) || !root.IsAttachedToWindow
            || _width != game.Width || _height != game.Height || _originX != _location[0] || _originY != _location[1]
            || _menu is null || !_menu.Enabled || _scene?.ContextMenus.Contains(_menu) != true || !_menu.Items.Any(i => i.Enabled))) Dismiss();
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
        _popup = null; _scene = null; _menu = null;
        game.EndContextMenuInteraction();
        popup.Dispose();
    }
    public void Dispose() => Dismiss();

    private static Color ToColor(Vector4Dto color) => Color.Argb((int)(Math.Clamp(color.W, 0, 1) * 255),
        (int)(Math.Clamp(color.X, 0, 1) * 255), (int)(Math.Clamp(color.Y, 0, 1) * 255), (int)(Math.Clamp(color.Z, 0, 1) * 255));
    private static GradientDrawable Background(Vector4Dto color, float rounding = 0, float border = 0, Vector4Dto borderColor = default)
    {
        GradientDrawable drawable = new(); drawable.SetColor(ToColor(color)); drawable.SetCornerRadius(rounding);
        if (border > 0) drawable.SetStroke((int)Math.Ceiling(border), ToColor(borderColor));
        return drawable;
    }
}
