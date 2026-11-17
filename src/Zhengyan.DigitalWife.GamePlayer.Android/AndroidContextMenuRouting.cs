using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidContextMenuRouting
{
    public static ContextMenuSettings? Resolve(RuntimeScene scene, GameWindowSettings window, float x, float y, int width, int height,
        Func<RuntimeRay, RuntimeRaycastHit?>? modelHitTest = null)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        bool overGui = GuiPointerHitTest.Contains(scene.Definition, x, y, width, height, window.Width, window.Height);
        RuntimeRaycastHit? hit = null;
        bool rayChecked = false;
        foreach (ContextMenuSettings menu in scene.Definition.ContextMenus.AsEnumerable().Reverse())
        {
            if (!menu.Enabled || menu.Items?.Any(item => item.Enabled) != true) continue;
            string type = Normalize(menu.TargetType);
            if (type == "gui_control")
            {
                var control = scene.Definition.GuiControls.FirstOrDefault(c => c.Visible && Matches(c.Id, c.Name, menu.TargetId));
                if (control is not null && Contains(LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                    width, height, window.Width, window.Height), x, y)) return menu;
                continue;
            }
            if (overGui) continue; // Entity/window menus must not capture a GUI hold.
            if (type == "window") return menu;
            if (type == "sprite")
            {
                var sprite = scene.Definition.Sprites.FirstOrDefault(s => s.Visible && Matches(s.Id, s.Name, menu.TargetId));
                if (sprite is not null && SpriteLayoutResolver.ContainsPoint(sprite, x, y, width, height, window.Width, window.Height)) return menu;
                continue;
            }
            if (type != "entity" || string.IsNullOrWhiteSpace(menu.TargetId)) continue;
            if (!rayChecked)
            {
                // Use the last screen camera covering the pointer, matching draw order.
                foreach (RuntimeCamera camera in scene.RenderCameras.Reverse())
                {
                    var viewport = camera.Definition.Viewport;
                    LayoutRect rect = viewport.Enabled
                        ? LayoutResolver.Resolve(viewport.LayoutMode, viewport.X, viewport.Y, viewport.Width, viewport.Height,
                            width, height, window.Width, window.Height)
                        : new(0, 0, width, height);
                    if (!Contains(rect, x, y)) continue;
                    var ray = camera.ScreenPointToRay(x - rect.X, y - rect.Y, rect.Width, rect.Height);
                    if (camera.RaycastEntity(ray, out var found)) hit = found;
                    if (modelHitTest?.Invoke(ray) is { } modelHit && (hit is null || modelHit.Distance < hit.Value.Distance)) hit = modelHit;
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

    public static bool RaycastBounds(RuntimeRay ray, Matrix4x4 world, Vector3 min, Vector3 max, out float distance)
    {
        distance = 0;
        if (!Matrix4x4.Invert(world, out var inverse)) return false;
        Vector3 origin = Vector3.Transform(ray.Origin, inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, inverse);
        float far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (Math.Abs(d) < .000001f) { if (o < lo || o > hi) return false; continue; }
            float a = (lo - o) / d, b = (hi - o) / d;
            distance = Math.Max(distance, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
            if (far < distance) return false;
        }
        return true;
    }

    private static bool Contains(LayoutRect rect, float x, float y)
        => x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;
    private static bool Matches(string id, string name, string target)
        => id.Equals(target, StringComparison.OrdinalIgnoreCase) || name.Equals(target, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string? type) => (type ?? "window").Trim().ToLowerInvariant() switch
    {
        "gui" or "control" or "gui_control" => "gui_control",
        "model" or "pmx" or "entity" => "entity",
        "sprite" => "sprite",
        _ => "window"
    };
}
