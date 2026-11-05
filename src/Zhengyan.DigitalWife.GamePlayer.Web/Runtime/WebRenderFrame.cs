using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebRenderMesh(
    string Id,
    float[] Model,
    bool ShadowEnabled,
    bool ReceiveShadow,
    bool EdgeEnabled)
{
    public bool ShadowToon { get; init; }
}

public sealed record WebParticle(float[] Position, float[] Color, float Size);

public sealed record WebPointLight(
    float[] Position,
    float[] Color,
    float Intensity,
    float Range,
    bool CastShadows);

public sealed record WebSpotLight(
    float[] Position,
    float[] Direction,
    float[] Color,
    float Intensity,
    float Range,
    float InnerConeCosine,
    float OuterConeCosine,
    bool CastShadows);

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
    IReadOnlyList<WebParticle> Particles,
    IReadOnlyList<WebPointLight> PointLights,
    IReadOnlyList<WebSpotLight> SpotLights)
{
    public float ShadowStrength { get; init; }
    public float ShadowBias { get; init; } = 0.0018f;
    public float ShadowNormalOffset { get; init; }
    public float[] CameraPosition { get; init; } = [0, 0, 0];

    public static WebRenderFrame FromScene(
        RuntimeScene scene,
        IEnumerable<(RuntimeEntity Entity, WebPmxMesh Mesh)> models,
        int width,
        int height,
        double elapsedSeconds = 0.0)
    {
        (RuntimeEntity Entity, WebPmxMesh Mesh)[] modelArray = models.ToArray();
        WebRenderMesh[] meshes = modelArray
            .Select(item => new WebRenderMesh(
                item.Entity.Id,
                ToGlArray(item.Entity.TransformMatrix),
                item.Entity.IsPmxModel && item.Entity.Definition.EnableShadow,
                item.Entity.IsPmxModel ? item.Entity.Definition.ReceiveShadow : item.Mesh.Materials.Any(material => material.ReceiveShadow),
                item.Entity.IsPmxModel && item.Entity.Definition.EnableEdge)
            {
                ShadowToon = string.Equals(item.Entity.Definition.ReceiveShadowMode, "toon", StringComparison.OrdinalIgnoreCase)
            })
            .ToArray();
        WebPointLight[] pointLights = scene.PointLights
            .Take(8)
            .Select(entity => new WebPointLight(
                ToVector(entity.Position),
                ToVector(entity.LightColor),
                entity.LightIntensity,
                entity.LightRange,
                entity.CastsShadows))
            .ToArray();
        WebSpotLight[] spotLights = scene.SpotLights
            .Take(4)
            .Select(entity =>
            {
                float inner = MathF.Cos(Math.Clamp(entity.SpotInnerConeAngleDegrees, 0.0f, 89.0f) * MathF.PI / 180.0f);
                float outer = MathF.Cos(Math.Clamp(entity.SpotOuterConeAngleDegrees, 0.01f, 89.5f) * MathF.PI / 180.0f);
                return new WebSpotLight(
                    ToVector(entity.Position),
                    ToVector(entity.SpotDirection),
                    ToVector(entity.LightColor),
                    entity.LightIntensity,
                    entity.LightRange,
                    inner,
                    outer,
                    entity.CastsShadows);
            })
            .ToArray();
        WebRenderMesh first = meshes.FirstOrDefault()
            ?? new WebRenderMesh("default", ToGlArray(Matrix4x4.Identity), true, true, true);
        float aspect = Math.Max(width, 1) / (float)Math.Max(height, 1);
        Vector3 lightDirection = scene.Lighting.DirectionalDirection;
        if (lightDirection.LengthSquared() < 1e-8f) lightDirection = new(0.5f, -1.0f, 0.5f);
        lightDirection = Vector3.Normalize(lightDirection);
        Matrix4x4 lightViewProjection = CreateLightViewProjection(modelArray, lightDirection, out float normalOffset);
        return new WebRenderFrame(
            ToGlArray(scene.MainCamera.CreateView() * ToGlProjection(scene.MainCamera.CreateProjection(aspect))),
            first.Model,
            ToGlArray(lightViewProjection),
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
            CreateParticles(scene, elapsedSeconds),
            pointLights,
            spotLights)
        {
            ShadowStrength = Math.Clamp(scene.Lighting.Settings.ShadowColor.W, 0.0f, 1.0f),
            ShadowNormalOffset = normalOffset,
            CameraPosition = ToVector(scene.MainCamera.Position)
        };
    }

    public static WebRenderFrame FromScene(RuntimeScene scene, RuntimeEntity entity, int width, int height)
    {
        return FromScene(scene, [], width, height);
    }

    private static Matrix4x4 CreateLightViewProjection(
        IEnumerable<(RuntimeEntity Entity, WebPmxMesh Mesh)> models, Vector3 direction, out float normalOffset)
    {
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach ((RuntimeEntity entity, WebPmxMesh mesh) in models)
        {
            foreach (Vector3 corner in BoundsCorners(mesh.BoundsMin, mesh.BoundsMax))
            {
                Vector3 world = Vector3.Transform(corner, entity.TransformMatrix);
                minimum = Vector3.Min(minimum, world);
                maximum = Vector3.Max(maximum, world);
            }
        }
        if (!float.IsFinite(minimum.X)) { minimum = new(-3); maximum = new(3); }
        minimum -= new Vector3(4);
        maximum += new Vector3(4);
        Vector3 center = (minimum + maximum) * 0.5f;
        float radius = MathF.Max(Vector3.Distance(minimum, maximum) * 0.5f, 6.0f);
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 view = Matrix4x4.CreateLookAt(center - direction * (radius + 18.0f), center, up);
        float halfWidth = 3, halfHeight = 3;
        foreach (Vector3 corner in BoundsCorners(minimum, maximum))
        {
            Vector3 lightSpace = Vector3.Transform(corner, view);
            halfWidth = MathF.Max(halfWidth, MathF.Abs(lightSpace.X) + 1);
            halfHeight = MathF.Max(halfHeight, MathF.Abs(lightSpace.Y) + 1);
        }
        normalOffset = MathF.Max(halfWidth, halfHeight) * 2.0f / 1024.0f * 0.75f;
        return view * ToGlProjection(Matrix4x4.CreateOrthographic(halfWidth * 2, halfHeight * 2, 0.1f, radius * 2 + 36));
    }

    private static IEnumerable<Vector3> BoundsCorners(Vector3 min, Vector3 max)
    {
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            yield return new Vector3(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
    }

    // Numerics projections use [0, 1] depth; WebGL requires [-1, 1].
    private static Matrix4x4 ToGlProjection(Matrix4x4 projection)
        => projection * Matrix4x4.CreateScale(1, 1, 2) * Matrix4x4.CreateTranslation(0, 0, -1);

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

    // System.Numerics uses row-vector matrix composition (world * view * projection),
    // while the WebGL shaders consume column vectors. Passing the row-major
    // fields as a column-major uniform supplies the required transposed matrix,
    // matching the Android renderer's ToGlArray contract.
    private static float[] ToGlArray(Matrix4x4 value) =>
    [
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
    ];

    private static float[] ToVector(Vector3 value) => [value.X, value.Y, value.Z];
    private static float[] ToVector(Vector3Dto value) => [value.X, value.Y, value.Z];
    private static float[] ToVector4(Vector4 value) => [value.X, value.Y, value.Z, value.W];
    private static float[] ToVector4(Vector4Dto value) => [value.X, value.Y, value.Z, value.W];
}
