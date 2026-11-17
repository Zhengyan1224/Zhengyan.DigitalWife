using Android.Views;
using Android.Widget;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidContextMenuPopup(FrameLayout root, AndroidGameSurfaceView game) : IDisposable
{
    private PopupMenu? _popup;
    private View? _anchor;
    private GameProjectScene? _scene;

    public void Show(ContextMenuSettings menu, float x, float y)
    {
        Dismiss();
        if (!root.IsAttachedToWindow || game.Project is not { } project) { game.EndContextMenuInteraction(); return; }
        _scene = project.Scene;
        int left = Math.Clamp((int)x, 0, Math.Max(0, root.Width - 1));
        int top = Math.Clamp((int)y, 0, Math.Max(0, root.Height - 1));
        _anchor = new View(root.Context) { ImportantForAccessibility = ImportantForAccessibility.No };
        root.AddView(_anchor, new FrameLayout.LayoutParams(1, 1) { LeftMargin = left, TopMargin = top });
        _anchor.Layout(left, top, left + 1, top + 1);
        PopupMenu popup = new(root.Context, _anchor);
        _popup = popup;
        for (int i = 0; i < menu.Items.Count; i++) popup.Menu?.Add(0, i, i, menu.Items[i].Text)?.SetEnabled(menu.Items[i].Enabled);
        GameProjectScene scene = _scene;
        popup.MenuItemClick += (_, args) =>
        {
            int index = args.Item?.ItemId ?? -1;
            if (!ReferenceEquals(scene, game.Project?.Scene) || !scene.ContextMenus.Contains(menu)
                || index < 0 || index >= menu.Items.Count || !menu.Enabled || !menu.Items[index].Enabled) return;
            args.Handled = true;
            game.DispatchContextMenuItem(menu, menu.Items[index], x, y);
        };
        popup.DismissEvent += (_, _) => Cleanup(popup);
        try
        {
            popup.Show();
            game.NotifyContextMenuOpened(menu, x, y);
            global::Android.Util.Log.Info("ZhengyanContextMenu", $"Opened '{menu.Name}'; target={menu.TargetType}:{menu.TargetId}; point={x:F0},{y:F0}");
        }
        catch (Exception ex) { Cleanup(popup); global::Android.Util.Log.Warn("ZhengyanContextMenu", ex.ToString()); }
    }

    public void Validate()
    {
        if (_popup is not null && !ReferenceEquals(_scene, game.Project?.Scene)) Dismiss();
    }
    public void Dismiss()
    {
        if (_popup is not { } popup) return;
        popup.Dismiss();
        Cleanup(popup);
    }
    private void Cleanup(PopupMenu popup)
    {
        if (!ReferenceEquals(_popup, popup)) return;
        _popup = null; _scene = null;
        if (_anchor is not null) { root.RemoveView(_anchor); _anchor.Dispose(); _anchor = null; }
        game.EndContextMenuInteraction();
        popup.Dispose();
    }
    public void Dispose() => Dismiss();
}
