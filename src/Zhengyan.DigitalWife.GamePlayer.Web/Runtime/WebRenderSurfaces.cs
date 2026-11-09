using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebLocalShadow(int PointLightIndex, int SpotLightIndex, int Face,
    float[] ViewProjection, float[] AtlasRect, float NearPlane, float FarPlane);

public sealed record WebPlanarReflection(string Id, float[] ViewProjection, float[] CameraPosition, float[] ClipPlane);

public sealed partial record WebRenderFrame
{
    // Match the desktop 4x4 atlas: two six-face point lights and four spots.
    public const int LocalShadowAtlasSize = 4096;

    private static IReadOnlyList<WebLocalShadow> CreateLocalShadows(WebPointLight[] points, WebSpotLight[] spots)
    {
        List<WebLocalShadow> shadows = [];
        int pointCount = 0;
        for (int i = 0; i < points.Length && pointCount < 2; i++)
        {
            WebPointLight light = points[i];
            if (!light.CastShadows || light.Range <= 0 || light.Intensity <= 0) continue;
            pointCount++;
            float near = LocalNearPlane(light.Range);
            Vector3 position = new(light.Position);
            for (int face = 0; face < 6; face++)
            {
                (Vector3 direction, Vector3 up) = face switch
                {
                    0 => (Vector3.UnitX, Vector3.UnitY),
                    1 => (-Vector3.UnitX, Vector3.UnitY),
                    2 => (Vector3.UnitY, -Vector3.UnitZ),
                    3 => (-Vector3.UnitY, Vector3.UnitZ),
                    4 => (Vector3.UnitZ, Vector3.UnitY),
                    _ => (-Vector3.UnitZ, Vector3.UnitY)
                };
                Add(i, -1, face, position, direction, up, MathF.PI / 2, near, light.Range);
            }
        }
        for (int i = 0; i < spots.Length; i++)
        {
            WebSpotLight light = spots[i];
            if (!light.CastShadows || light.Range <= 0 || light.Intensity <= 0) continue;
            Vector3 direction = Vector3.Normalize(new Vector3(light.Direction));
            Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .95f ? Vector3.UnitZ : Vector3.UnitY;
            float fov = Math.Clamp(2 * MathF.Acos(light.OuterConeCosine), MathF.PI / 180, 179 * MathF.PI / 180);
            Add(-1, i, 0, new Vector3(light.Position), direction, up, fov, LocalNearPlane(light.Range), light.Range);
        }
        return shadows;

        void Add(int point, int spot, int face, Vector3 position, Vector3 direction, Vector3 up, float fov, float near, float far)
        {
            int tile = shadows.Count;
            const float padding = 2.0f / LocalShadowAtlasSize;
            float[] rect = [tile % 4 / 4.0f + padding, tile / 4 / 4.0f + padding, .25f - 2 * padding, .25f - 2 * padding];
            Matrix4x4 matrix = Matrix4x4.CreateLookAt(position, position + direction, up)
                * ToGlProjection(Matrix4x4.CreatePerspectiveFieldOfView(fov, 1, near, far));
            shadows.Add(new(point, spot, face, ToGlArray(matrix), rect, near, far));
        }
    }

    private static float LocalNearPlane(float range)
        => Math.Clamp(range * .0025f, Math.Min(.02f, range * .1f), Math.Min(.25f, range * .5f));

    private static Matrix4x4 CreateRenderTransform(RuntimeEntity entity, RuntimeCamera camera)
    {
        if (!entity.IsTexturedPlane || !entity.Definition.Plane.Billboard) return entity.TransformMatrix;
        Vector3 up = camera.Settings.VmdHasUp ? camera.Settings.VmdUp.ToVector3() : Vector3.UnitY;
        return Matrix4x4.CreateScale(entity.Scale)
            * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(
                Matrix4x4.CreateBillboard(entity.Position, camera.Position, up, Vector3.Normalize(camera.Target - camera.Position))))
            * Matrix4x4.CreateTranslation(entity.Position);
    }

    private static IReadOnlyList<WebPlanarReflection> CreateReflections(
        (RuntimeEntity Entity, WebPmxMesh Mesh)[] models, RuntimeCamera camera, float aspect)
    {
        List<WebPlanarReflection> reflections = [];
        foreach ((RuntimeEntity entity, _) in models)
        {
            if (!entity.IsTexturedPlane || !entity.Definition.Plane.MirrorReflectionEnabled
                || entity.Definition.Plane.MirrorReflectionStrength <= .001f) continue;
            Matrix4x4 world = CreateRenderTransform(entity, camera);
            Vector3 normal = Vector3.Cross(Vector3.TransformNormal(Vector3.UnitX, world), Vector3.TransformNormal(Vector3.UnitY, world));
            if (normal.LengthSquared() <= 1e-8f) continue;
            normal = Vector3.Normalize(normal);
            float distance = -Vector3.Dot(normal, entity.Position);
            Vector3 ReflectPoint(Vector3 p) => p - 2 * (Vector3.Dot(normal, p) + distance) * normal;
            Vector3 position = ReflectPoint(camera.Position), target = ReflectPoint(camera.Target);
            Vector3 sourceUp = camera.Settings.VmdHasUp ? camera.Settings.VmdUp.ToVector3() : Vector3.UnitY;
            Vector3 up = sourceUp - 2 * Vector3.Dot(normal, sourceUp) * normal;
            Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(position, target, up) * ToGlProjection(camera.CreateProjection(aspect));
            if (Vector3.Dot(normal, camera.Position) + distance < 0) { normal = -normal; distance = -distance; }
            reflections.Add(new(entity.Id, ToGlArray(viewProjection), ToVector(position), [normal.X, normal.Y, normal.Z, distance - .01f]));
        }
        return reflections;
    }
}
