using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class ShadowMapRenderer : IDisposable
{
    private const int DefaultResolution = 2048;
    private const float MinimumExtent = 6.0f;
    private const float DepthPadding = 18.0f;
    private const float BoundsPadding = 4.0f;
    private readonly IShadowMapTarget _shadowTexture;
    private bool _disposed;
    private int _resolution = DefaultResolution;

    public ShadowMapRenderer(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _shadowTexture = game.GraphicsDevice.Renderer.Services.CreateShadowMapTarget("DirectionalShadowMap");
    }

    public int Resolution
    {
        get => _resolution;
        set => _resolution = Math.Clamp(value, 256, 8192);
    }

    public ShadowMapBinding? CurrentBinding { get; private set; }

    public void Render(
        GameTime gameTime,
        IReadOnlyList<PmxModelComponent> pmxModels,
        IReadOnlyList<ParticleSystemComponent> particleSystems,
        IReadOnlyList<TexturedPlaneComponent> planeReceivers,
        Vector3 lightDirection,
        Vector4 shadowColor,
        int targetWidth,
        int targetHeight,
        Action restoreRenderTarget)
    {
        _ = gameTime;
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pmxModels);
        ArgumentNullException.ThrowIfNull(particleSystems);
        ArgumentNullException.ThrowIfNull(planeReceivers);
        ArgumentNullException.ThrowIfNull(restoreRenderTarget);

        List<PmxModelComponent> casters = pmxModels
            .Where(model => model.Visible && model.EnableShadow)
            .ToList();
        List<ParticleSystemComponent> particleCasters = particleSystems
            .Where(particle => particle.Visible && particle.CastShadows)
            .ToList();
        if ((casters.Count == 0 && particleCasters.Count == 0) || shadowColor.W <= 0.001f)
        {
            CurrentBinding = null;
            return;
        }

        Bounds3 bounds = ComputeSceneBounds(casters, particleCasters, planeReceivers);
        Matrix4x4 lightViewProjection = CreateLightViewProjection(
            bounds,
            lightDirection,
            _resolution,
            out float nearDistance,
            out float farDistance,
            out float worldUnitsPerTexel);

        _shadowTexture.EnsureSize(_resolution, _resolution);
        _shadowTexture.BeginPass();
        try
        {
            foreach (PmxModelComponent model in casters)
            {
                model.DrawShadowDepthPass(lightViewProjection, 2.0f / 16777216.0f);
            }
            foreach (ParticleSystemComponent particle in particleCasters)
            {
                particle.DrawShadowDepthPass(lightViewProjection, 2.0f / 16777216.0f);
            }
        }
        finally
        {
            _shadowTexture.EndPass();
        }

        restoreRenderTarget();

        CurrentBinding = new ShadowMapBinding(
            _shadowTexture.Texture,
            lightViewProjection,
            nearDistance,
            farDistance,
            Math.Clamp(shadowColor.W, 0.0f, 1.0f),
            0.0018f)
        {
            NativeSampler = _shadowTexture.NativeSampler,
            TexelSize = new Vector2(
                1.0f / Math.Max(_shadowTexture.Width, 1),
                1.0f / Math.Max(_shadowTexture.Height, 1)),
            NormalOffset = worldUnitsPerTexel * 0.75f
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shadowTexture.Dispose();
    }

    private static Bounds3 ComputeSceneBounds(
        IReadOnlyList<PmxModelComponent> casters,
        IReadOnlyList<ParticleSystemComponent> particleCasters,
        IReadOnlyList<TexturedPlaneComponent> planeReceivers)
    {
        Bounds3 bounds = new();
        foreach (PmxModelComponent model in casters)
        {
            bounds.Encapsulate(model.Position);
            Vector3 localMin = model.BoundsMin;
            Vector3 localMax = model.BoundsMax;
            if (localMin == localMax)
            {
                bounds.Encapsulate(model.Position + new Vector3(-1.0f, 0.0f, -1.0f));
                bounds.Encapsulate(model.Position + new Vector3(1.0f, 2.0f, 1.0f));
                continue;
            }

            Matrix4x4 world = model.World;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 corner = new(
                    x == 0 ? localMin.X : localMax.X,
                    y == 0 ? localMin.Y : localMax.Y,
                    z == 0 ? localMin.Z : localMax.Z);
                bounds.Encapsulate(Vector3.Transform(corner, world));
            }
        }

        foreach (ParticleSystemComponent particle in particleCasters)
        {
            if (particle.TryGetShadowBounds(out Vector3 minimum, out Vector3 maximum))
            {
                bounds.Encapsulate(minimum);
                bounds.Encapsulate(maximum);
            }
        }

        foreach (TexturedPlaneComponent plane in planeReceivers.Where(plane => plane.Visible && plane.ReceiveShadow))
        {
            foreach (Vector3 corner in plane.GetWorldCorners())
            {
                bounds.Encapsulate(corner);
            }
        }

        if (!bounds.HasValue)
        {
            bounds.Encapsulate(Vector3.Zero);
            bounds.Encapsulate(Vector3.One);
        }

        bounds.Expand(BoundsPadding);
        return bounds;
    }

    private static Matrix4x4 CreateLightViewProjection(
        Bounds3 bounds,
        Vector3 lightDirection,
        int resolution,
        out float nearDistance,
        out float farDistance,
        out float worldUnitsPerTexel)
    {
        Vector3 direction = lightDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(lightDirection)
            : Vector3.Normalize(new Vector3(-0.5f, -1.0f, -0.5f));
        Vector3 center = bounds.Center;
        float radius = MathF.Max(bounds.Radius, MinimumExtent);
        Vector3 lightPosition = center - (direction * (radius + DepthPadding));
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 view = Matrix4x4.CreateLookAt(lightPosition, center, up);

        Vector3 lightMin = new(float.PositiveInfinity);
        Vector3 lightMax = new(float.NegativeInfinity);
        foreach (Vector3 corner in bounds.Corners)
        {
            Vector3 lightSpace = Vector3.Transform(corner, view);
            lightMin = Vector3.Min(lightMin, lightSpace);
            lightMax = Vector3.Max(lightMax, lightSpace);
        }

        float halfWidth = MathF.Max(MathF.Max(MathF.Abs(lightMin.X), MathF.Abs(lightMax.X)) + 1.0f, MinimumExtent * 0.5f);
        float halfHeight = MathF.Max(MathF.Max(MathF.Abs(lightMin.Y), MathF.Abs(lightMax.Y)) + 1.0f, MinimumExtent * 0.5f);
        float depth = (radius * 2.0f) + (DepthPadding * 2.0f);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(
            halfWidth * 2.0f,
            halfHeight * 2.0f,
            0.1f,
            depth);

        nearDistance = 0.0f;
        farDistance = depth;
        worldUnitsPerTexel = MathF.Max(halfWidth * 2.0f, halfHeight * 2.0f)
            / Math.Max(resolution, 1);
        return view * projection;
    }

    private struct Bounds3
    {
        public Vector3 Min;
        public Vector3 Max;
        public bool HasValue;

        public readonly Vector3 Center => (Min + Max) * 0.5f;

        public readonly float Radius => HasValue ? Vector3.Distance(Min, Max) * 0.5f : MinimumExtent;

        public readonly IEnumerable<Vector3> Corners
        {
            get
            {
                if (!HasValue)
                {
                    yield break;
                }

                for (int x = 0; x <= 1; x++)
                for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    yield return new Vector3(
                        x == 0 ? Min.X : Max.X,
                        y == 0 ? Min.Y : Max.Y,
                        z == 0 ? Min.Z : Max.Z);
                }
            }
        }

        public void Encapsulate(Vector3 point)
        {
            if (!HasValue)
            {
                Min = point;
                Max = point;
                HasValue = true;
                return;
            }

            Min = Vector3.Min(Min, point);
            Max = Vector3.Max(Max, point);
        }

        public void Expand(float amount)
        {
            if (!HasValue)
            {
                return;
            }

            Vector3 padding = new(MathF.Max(0.0f, amount));
            Min -= padding;
            Max += padding;
        }
    }
}
