using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidDialogueBubbleLayout
{
    // Match PC's model-top anchor, including the rendered component's scale,
    // rotation and translation. WorldOffset is applied afterwards in world space.
    internal static Vector3 ModelAnchor(Vector3 minimum, Vector3 maximum, Matrix4x4 world, bool useTop)
        => Vector3.Transform(useTop ? new Vector3((minimum.X + maximum.X) * 0.5f,
            maximum.Y, (minimum.Z + maximum.Z) * 0.5f) : Vector3.Zero, world);

    internal static bool TryResolveAnchor(RuntimeDialogueBubble bubble, Vector3? entityAnchor,
        Matrix4x4 view, Matrix4x4 projection, LayoutRect viewport, Vector2 canvasSize,
        Vector2 referenceSize, out Vector2 position)
    {
        position = default;
        LayoutRect offset = LayoutResolver.Resolve(bubble.LayoutMode, bubble.ScreenOffset.X, bubble.ScreenOffset.Y,
            1, 1, canvasSize.X, canvasSize.Y, referenceSize.X, referenceSize.Y);
        Vector3 world;
        if (string.Equals(bubble.AnchorMode, "entity", StringComparison.OrdinalIgnoreCase))
        {
            // Missing/removed targets must not turn into fixed screen-space UI.
            if (entityAnchor is not Vector3 anchor) return false;
            world = anchor + bubble.WorldOffset;
        }
        else if (string.Equals(bubble.AnchorMode, "world", StringComparison.OrdinalIgnoreCase))
            world = bubble.WorldPosition + bubble.WorldOffset;
        else
        {
            LayoutRect screen = LayoutResolver.Resolve(bubble.LayoutMode, bubble.ScreenPosition.X, bubble.ScreenPosition.Y,
                1, 1, canvasSize.X, canvasSize.Y, referenceSize.X, referenceSize.Y);
            position = new(screen.X + offset.X, screen.Y + offset.Y);
            return float.IsFinite(position.X) && float.IsFinite(position.Y);
        }

        // OrbitCamera exposes the same canonical 0..1-depth projection on both
        // backends. Canvas and this viewport use a top-left origin, regardless
        // of the GPU framebuffer's Y convention.
        Vector4 clip = Vector4.Transform(Vector4.Transform(new Vector4(world, 1), view), projection);
        if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) || !float.IsFinite(clip.Z) ||
            !float.IsFinite(clip.W) || clip.W <= 0.0001f || clip.Z < 0 || clip.Z > clip.W) return false;
        position = new(viewport.X + (clip.X / clip.W * 0.5f + 0.5f) * viewport.Width + offset.X,
            viewport.Y + (0.5f - clip.Y / clip.W * 0.5f) * viewport.Height + offset.Y);
        return true;
    }

    internal static Vector2 TopLeft(RuntimeDialogueBubble bubble, Vector2 anchor, Vector2 size)
        => anchor - Vector2.Clamp(bubble.Pivot, Vector2.Zero, Vector2.One) * size;
}
