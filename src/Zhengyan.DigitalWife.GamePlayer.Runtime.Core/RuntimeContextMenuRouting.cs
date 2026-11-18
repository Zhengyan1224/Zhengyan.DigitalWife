using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public static class RuntimeContextMenuRouting
{
    public static ContextMenuSettings? Resolve(RuntimeScene scene, GameWindowSettings window, float x, float y, int width, int height)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        bool overGui = GuiPointerHitTest.Contains(scene.Definition, x, y, width, height, window.Width, window.Height);
        RuntimeRaycastHit? hit = null;
        bool rayChecked = false;
        foreach (ContextMenuSettings menu in scene.Definition.ContextMenus.AsEnumerable().Reverse())
        {
            if (!menu.Enabled || menu.Items?.Any(item => item.Enabled) != true) continue;
            string type = Normalize(menu.TargetType);
            if (type == "window" || string.IsNullOrWhiteSpace(menu.TargetId)) return menu;
            if (type == "gui_control")
            {
                var control = scene.Definition.GuiControls.FirstOrDefault(c => c.Visible && Matches(c.Id, c.Name, menu.TargetId));
                if (control is not null && Contains(LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                    width, height, window.Width, window.Height), x, y)) return menu;
                continue;
            }
            if (type == "sprite")
            {
                var sprite = scene.Definition.Sprites.FirstOrDefault(s => s.Visible && Matches(s.Id, s.Name, menu.TargetId));
                if (sprite is not null && SpriteLayoutResolver.ContainsPoint(sprite, x, y, width, height, window.Width, window.Height)) return menu;
                continue;
            }
            if (overGui) continue; // Entity menus must not capture a GUI or interactive sprite hold.
            if (type != "entity" || string.IsNullOrWhiteSpace(menu.TargetId)) continue;
            if (!rayChecked)
            {
                // Use the last screen camera covering the pointer, matching draw order.
                foreach (RuntimeCamera camera in scene.RenderCameras.Reverse())
                {
                    RuntimeViewport viewport = camera.ResolveViewport(width, height, window.Width, window.Height);
                    // Render viewports use a bottom-left origin and are clipped to the canvas.
                    LayoutRect rect = new(viewport.X, height - viewport.Y - viewport.Height, viewport.Width, viewport.Height);
                    if (!Contains(rect, x, y)) continue;
                    var ray = camera.ScreenPointToRay(x - rect.X, y - rect.Y, rect.Width, rect.Height);
                    if (camera.RaycastEntity(ray, out var found)) hit = found;
                    break;
                }
                rayChecked = true;
            }
            var target = scene.GetEntity(menu.TargetId);
            if (hit is { } value && target is not null && ReferenceEquals(value.Entity, target)
                && (string.IsNullOrWhiteSpace(menu.TargetCollider) || Matches(value.ColliderId, value.ColliderName, menu.TargetCollider))) return menu;
        }
        return null;
    }

    public static string TargetEntity(RuntimeScene scene, ContextMenuSettings menu)
        => Normalize(menu.TargetType) switch
        {
            "entity" => scene.GetEntity(menu.TargetId)?.Id ?? menu.TargetId,
            "gui_control" => scene.Definition.GuiControls.FirstOrDefault(c => Matches(c.Id, c.Name, menu.TargetId))?.TargetEntity ?? "",
            "sprite" => scene.Definition.Sprites.FirstOrDefault(s => Matches(s.Id, s.Name, menu.TargetId))?.TargetEntity ?? "",
            _ => ""
        };

    private static bool Contains(LayoutRect rect, float x, float y)
        => x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;
    private static bool Matches(string id, string name, string target)
        => id.Equals(target, StringComparison.OrdinalIgnoreCase) || name.Equals(target, StringComparison.OrdinalIgnoreCase);
    public static string Normalize(string? type) => (type ?? "window").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "gui" or "control" or "gui_control" or "gui_controls" => "gui_control",
        "model" or "pmx" or "entity" or "rigidbody" or "rigid_body" or "collider" or "object" => "entity",
        "sprite" or "2d_sprite" or "2d" => "sprite",
        _ => "window"
    };
}
