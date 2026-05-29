using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public static class VisibilityCulling
{
    public static bool IsBoundingSphereVisible(OrbitCamera camera, Vector3 center, float radius)
    {
        ArgumentNullException.ThrowIfNull(camera);

        float safeRadius = Math.Max(radius, 0.0f);
        Vector3 viewCenter = Vector3.Transform(center, camera.View);
        float depth = -viewCenter.Z;

        if (depth + safeRadius < camera.NearClipPlane || depth - safeRadius > camera.FarClipPlane)
        {
            return false;
        }

        float aspect = Math.Max(camera.Width, 1) / (float)Math.Max(camera.Height, 1);
        if (camera.ProjectionMode == CameraProjectionMode.Orthographic)
        {
            float halfHeight = camera.OrthographicSize;
            float halfWidth = halfHeight * aspect;
            return MathF.Abs(viewCenter.X) - safeRadius <= halfWidth
                && MathF.Abs(viewCenter.Y) - safeRadius <= halfHeight;
        }

        float clampedDepth = Math.Max(depth, camera.NearClipPlane);
        float halfHeightPerspective = MathF.Tan(camera.Fov * 0.5f) * clampedDepth;
        float halfWidthPerspective = halfHeightPerspective * aspect;
        return MathF.Abs(viewCenter.X) - safeRadius <= halfWidthPerspective
            && MathF.Abs(viewCenter.Y) - safeRadius <= halfHeightPerspective;
    }
}
