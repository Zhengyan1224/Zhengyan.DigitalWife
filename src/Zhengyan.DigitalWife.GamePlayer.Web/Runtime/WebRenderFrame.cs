using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebRenderMesh(
    string Id,
    float[] Model,
    bool ShadowEnabled,
    bool ReceiveShadow,
    bool EdgeEnabled);

public sealed record WebParticle(float[] Position, float[] Color, float Size);

public sealed record WebRenderFeatures(
    bool Shadows = true,
    bool Edges = true,
    bool Skybox = false,
    bool Particles = false,
    bool Water = false,
    bool PostProcess = false);

public sealed record WebRenderFrame(
    float[] ViewProjection,
    float[] Model,
    float[] LightViewProjection,
    float[] LightDirection,
    float[] LightColor,
    float[] AmbientColor,
    float AmbientStrength,
    float[] ClearColor,
    bool ShadowEnabled,
    IReadOnlyList<WebRenderMesh> Meshes,
    WebRenderFeatures Features,
    IReadOnlyList<WebParticle> Particles)
{
    public static WebRenderFrame FromScene(
        RuntimeScene scene,
        IEnumerable<(RuntimeEntity Entity, WebPmxAsset Asset)> models,
        int width,
        int height,
        double elapsedSeconds = 0.0)
    {
        WebRenderMesh[] meshes = models
            .Where(item => item.Entity.IsPmxModel)
            .Select(item => new WebRenderMesh(
                item.Entity.Id,
                ToColumnMajor(item.Entity.TransformMatrix),
                item.Entity.Definition.EnableShadow,
                item.Entity.Definition.ReceiveShadow,
                item.Entity.Definition.EnableEdge))
            .ToArray();
        WebRenderMesh first = meshes.FirstOrDefault()
            ?? new WebRenderMesh("default", ToColumnMajor(Matrix4x4.Identity), true, true, true);
        float aspect = Math.Max(width, 1) / (float)Math.Max(height, 1);
        Vector3 lightDirection = scene.Lighting.DirectionalDirection;
        if (lightDirection.LengthSquared() < 1e-8f) lightDirection = new(0.5f, -1.0f, 0.5f);
        lightDirection = Vector3.Normalize(lightDirection);
        Vector3 center = ComputeLightCenter(models);
        Vector3 lightPosition = center - lightDirection * 25.0f;
        Vector3 up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPosition, center, up);
        Matrix4x4 lightProjection = Matrix4x4.CreateOrthographic(30.0f, 30.0f, 0.1f, 80.0f);
        return new WebRenderFrame(
            ToColumnMajor(scene.MainCamera.CreateView() * scene.MainCamera.CreateProjection(aspect)),
            first.Model,
            ToColumnMajor(lightView * lightProjection),
            ToVector(lightDirection),
            ToVector(scene.Lighting.DirectionalColor),
            ToVector(scene.Lighting.AmbientColor),
            scene.Lighting.AmbientStrength,
            ToVector4(scene.Lighting.Settings.ClearColor),
            meshes.Any(mesh => mesh.ShadowEnabled),
            meshes,
            new WebRenderFeatures(
                Shadows: meshes.Any(mesh => mesh.ShadowEnabled),
                Edges: meshes.Any(mesh => mesh.EdgeEnabled),
                Skybox: scene.Definition.Skybox.Enabled,
                Particles: scene.ParticleSystems.Any(),
                Water: scene.WaterSurfaces.Any()),
            CreateParticles(scene, elapsedSeconds));
    }

    public static WebRenderFrame FromScene(RuntimeScene scene, RuntimeEntity entity, int width, int height)
    {
        return FromScene(scene, [(entity, null!)], width, height);
    }

    private static Vector3 ComputeLightCenter(IEnumerable<(RuntimeEntity Entity, WebPmxAsset Asset)> models)
    {
        Vector3[] positions = models.Select(item => item.Entity.Position).ToArray();
        return positions.Length == 0
            ? Vector3.Zero
            : positions.Aggregate(Vector3.Zero, static (sum, position) => sum + position) / positions.Length;
    }

    private static IReadOnlyList<WebParticle> CreateParticles(RuntimeScene scene, double elapsedSeconds)
    {
        List<WebParticle> particles = [];
        foreach (RuntimeEntity entity in scene.ParticleSystems.Take(8))
        {
            ParticleEntitySettings settings = entity.Definition.Particle;
            int count = Math.Clamp(settings.ParticleCount, 0, 128);
            int seed = entity.Id.Aggregate(17, static (value, character) => unchecked(value * 31 + character));
            Vector3 origin = entity.Position;
            for (int i = 0; i < count; i++)
            {
                float phase = (float)((elapsedSeconds * Math.Max(settings.SimulationSpeed, 0.0f) + i * 0.6180339 + seed * 0.001) % Math.Max(settings.MaxLifetime, 0.1f));
                float normalized = (i + 1) / (float)(count + 1);
                Vector3 jitter = new(
                    MathF.Sin(i * 12.9898f + seed) * settings.SpawnBoxHalfExtents.X,
                    MathF.Cos(i * 7.123f + seed) * settings.SpawnBoxHalfExtents.Y,
                    MathF.Sin(i * 3.77f + seed) * settings.SpawnBoxHalfExtents.Z);
                Vector3 position = origin + jitter + settings.BaseVelocity.ToVector3() * phase + settings.Acceleration.ToVector3() * (phase * phase * 0.5f);
                Vector4 color = Vector4.Lerp(settings.StartColor.ToVector4(), settings.EndColor.ToVector4(), normalized);
                particles.Add(new WebParticle(ToVector(position), ToVector4(color), Math.Max(settings.MinSize, 0.01f)));
            }
        }
        return particles;
    }

    private static float[] ToColumnMajor(Matrix4x4 value) =>
    [
        value.M11, value.M21, value.M31, value.M41,
        value.M12, value.M22, value.M32, value.M42,
        value.M13, value.M23, value.M33, value.M43,
        value.M14, value.M24, value.M34, value.M44
    ];

    private static float[] ToVector(Vector3 value) => [value.X, value.Y, value.Z];
    private static float[] ToVector(Vector3Dto value) => [value.X, value.Y, value.Z];
    private static float[] ToVector4(Vector4 value) => [value.X, value.Y, value.Z, value.W];
    private static float[] ToVector4(Vector4Dto value) => [value.X, value.Y, value.Z, value.W];
}
