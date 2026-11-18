using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public static class RuntimeContextMenuRouting
{
    public static ContextMenuSettings? Resolve(RuntimeScene scene, GameWindowSettings window, float x, float y, int width, int height)
        => ResolveSelection(scene, window, x, y, width, height)?.Menu;

    public static ContextMenuSelection? ResolveSelection(RuntimeScene scene, GameWindowSettings window, float x, float y, int width, int height)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        bool overGui = GuiPointerHitTest.Contains(scene.Definition, x, y, width, height, window.Width, window.Height);
        RuntimeRaycastHit? hit = null;
        bool rayChecked = false;
        foreach (ContextMenuSettings menu in scene.Definition.ContextMenus.AsEnumerable().Reverse())
        {
            if (!menu.Enabled || menu.Items?.Any(item => item.Enabled) != true) continue;
            string type = Normalize(menu.TargetType);
            var targets = menu.GetTargets();
            if (type == "window" || targets.Count == 0)
                return new(menu, new() { PointerX = x, PointerY = y });
            if (type == "gui_control")
            {
                var control = scene.Definition.GuiControls.AsEnumerable().Reverse().FirstOrDefault(c => c.Visible
                    && targets.Any(t => Matches(c.Id, c.Name, t.TargetId))
                    && Contains(LayoutResolver.Resolve(c.LayoutMode, c.X, c.Y, c.Width, c.Height,
                        width, height, window.Width, window.Height), x, y));
                if (control is not null)
                    return new(menu, Target(scene, type, control.Id, control.Name, control.TargetEntity, x, y));
                continue;
            }
            if (type == "sprite")
            {
                var sprite = scene.Definition.Sprites.AsEnumerable().Reverse().OrderByDescending(s => s.DrawOrder)
                    .FirstOrDefault(s => s.Visible && targets.Any(t => Matches(s.Id, s.Name, t.TargetId))
                        && SpriteLayoutResolver.ContainsPoint(s, x, y, width, height, window.Width, window.Height));
                if (sprite is not null) return new(menu, Target(scene, type, sprite.Id, sprite.Name, sprite.TargetEntity, x, y));
                continue;
            }
            if (overGui) continue; // Entity menus must not capture a GUI or interactive sprite hold.
            if (type != "entity") continue;
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
            if (hit is { } value && targets.Any(t => ReferenceEquals(scene.GetEntity(t.TargetId), value.Entity)
                && t.MatchesCollider(value.ColliderId, value.ColliderName)))
                return new(menu, Target(scene, type, value.Entity.Id, value.Entity.Name, value.Entity.Id, x, y) with
                { ColliderId = value.ColliderId, ColliderName = value.ColliderName, ColliderType = value.ColliderShape });
        }
        return null;
    }

    public static string TargetEntity(RuntimeScene scene, ContextMenuSettings menu)
    {
        // Kept for callers resolving a single binding. Opening-specific routing uses selection.Target.EntityId.
        string targetId = menu.GetTargets().FirstOrDefault()?.TargetId ?? "";
        return Normalize(menu.TargetType) switch
        {
            "entity" => scene.GetEntity(targetId)?.Id ?? targetId,
            "gui_control" => scene.Definition.GuiControls.FirstOrDefault(c => Matches(c.Id, c.Name, targetId))?.TargetEntity ?? "",
            "sprite" => scene.Definition.Sprites.FirstOrDefault(s => Matches(s.Id, s.Name, targetId))?.TargetEntity ?? "",
            _ => ""
        };
    }

    private static ContextMenuTargetInfo Target(RuntimeScene scene, string type, string id, string name, string entityId, float x, float y)
    {
        var entity = scene.GetEntity(entityId);
        return new() { TargetType = type, TargetId = id, TargetName = name, EntityId = entity?.Id ?? "",
            EntityName = entity?.Name ?? "", PointerX = x, PointerY = y };
    }

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
