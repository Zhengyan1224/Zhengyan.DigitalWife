using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class LocalLightShadowRenderer : IDisposable
{
    private const int AtlasColumns = 4;
    private const int AtlasRows = 4;
    private const int TilePadding = 2;
    private readonly IShadowMapTarget _atlas;
    private int _resolution = 4096;
    private long _lastRenderedFrame = -1;
    private bool _disposed;

    public LocalLightShadowRenderer(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _atlas = game.GraphicsDevice.Renderer.Services.CreateShadowMapTarget("LocalLightShadowAtlas");
    }

    public int Resolution
    {
        get => _resolution;
        set => _resolution = Math.Clamp(value, 1024, 8192);
    }

    public LocalLightShadowBinding? CurrentBinding { get; private set; }

    public void Render(
        GameTime gameTime,
        IReadOnlyList<PmxModelComponent> pmxModels,
        IReadOnlyList<PointLightData> pointLights,
        IReadOnlyList<SpotLightData> spotLights,
        float shadowStrength,
        Action restoreRenderTarget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(restoreRenderTarget);
        if (_lastRenderedFrame == gameTime.FrameCount)
        {
            return;
        }

        List<PmxModelComponent> casters = pmxModels
            .Where(model => model.Visible && model.EnableShadow)
            .ToList();
        if (casters.Count == 0 || shadowStrength <= 0.001f)
        {
            CurrentBinding = null;
            _lastRenderedFrame = gameTime.FrameCount;
            return;
        }

        List<(int PackedIndex, PointLightData Light)> points = SelectPointLights(pointLights);
        List<(int PackedIndex, SpotLightData Light)> spots = SelectSpotLights(spotLights);
        if (points.Count == 0 && spots.Count == 0)
        {
            CurrentBinding = null;
            _lastRenderedFrame = gameTime.FrameCount;
            return;
        }

        _atlas.EnsureSize(_resolution, _resolution);
        int tileSize = Math.Min(_atlas.Width / AtlasColumns, _atlas.Height / AtlasRows);
        int tileIndex = 0;
        List<PointLightShadowBinding> pointBindings = [];
        List<SpotLightShadowBinding> spotBindings = [];
        _atlas.BeginPass();
        try
        {
            foreach ((int packedIndex, PointLightData light) in points)
            {
                float nearPlane = GetNearPlane(light.Range);
                Matrix4x4[] matrices = new Matrix4x4[LocalLightShadowLimits.PointFacesPerLight];
                Vector4[] rects = new Vector4[LocalLightShadowLimits.PointFacesPerLight];
                for (int face = 0; face < LocalLightShadowLimits.PointFacesPerLight; face++)
                {
                    GetPointFace(face, out Vector3 direction, out Vector3 up);
                    matrices[face] = CreatePointViewProjection(light, direction, up, nearPlane);
                    rects[face] = BeginTile(tileIndex++, tileSize);
                    DrawCasters(casters, matrices[face]);
                }
                pointBindings.Add(new PointLightShadowBinding(
                    packedIndex,
                    nearPlane,
                    light.Range,
                    matrices,
                    rects));
            }

            foreach ((int packedIndex, SpotLightData light) in spots)
            {
                float nearPlane = GetNearPlane(light.Range);
                Matrix4x4 matrix = CreateSpotViewProjection(light, nearPlane);
                Vector4 rect = BeginTile(tileIndex++, tileSize);
                DrawCasters(casters, matrix);
                spotBindings.Add(new SpotLightShadowBinding(
                    packedIndex,
                    nearPlane,
                    light.Range,
                    matrix,
                    rect));
            }
        }
        finally
        {
            _atlas.EndPass();
        }

        restoreRenderTarget();
        CurrentBinding = new LocalLightShadowBinding
        {
            Texture = _atlas.Texture,
            NativeSampler = _atlas.NativeSampler,
            PointLights = pointBindings,
            SpotLights = spotBindings,
            Strength = Math.Clamp(shadowStrength, 0.0f, 1.0f),
            // World-space receiver offset. The shader converts it to the
            // non-linear perspective depth range for each light and fragment.
            Bias = 0.015f,
            TexelSize = new Vector2(1.0f / Math.Max(_atlas.Width, 1), 1.0f / Math.Max(_atlas.Height, 1))
        };
        _lastRenderedFrame = gameTime.FrameCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _atlas.Dispose();
        GC.SuppressFinalize(this);
    }

    private Vector4 BeginTile(int tileIndex, int tileSize)
    {
        int column = tileIndex % AtlasColumns;
        int row = tileIndex / AtlasColumns;
        int x = column * tileSize + TilePadding;
        int y = row * tileSize + TilePadding;
        int size = Math.Max(tileSize - TilePadding * 2, 1);
        _atlas.BeginRegion(x, y, size, size);
        return new Vector4(
            (float)x / _atlas.Width,
            (float)y / _atlas.Height,
            (float)size / _atlas.Width,
            (float)size / _atlas.Height);
    }

    private static void DrawCasters(IEnumerable<PmxModelComponent> casters, Matrix4x4 lightViewProjection)
    {
        foreach (PmxModelComponent model in casters)
        {
            model.DrawShadowDepthPass(lightViewProjection);
        }
    }

    private static List<(int, PointLightData)> SelectPointLights(IReadOnlyList<PointLightData> lights)
    {
        List<(int, PointLightData)> selected = [];
        int packedIndex = 0;
        foreach (PointLightData light in lights)
        {
            if (!PointLightPacking.IsValid(light)) continue;
            if (light.CastShadows && selected.Count < LocalLightShadowLimits.MaxShadowedPointLights)
                selected.Add((packedIndex, light));
            packedIndex++;
            if (packedIndex >= PointLightPacking.MaxLights) break;
        }
        return selected;
    }

    private static List<(int, SpotLightData)> SelectSpotLights(IReadOnlyList<SpotLightData> lights)
    {
        List<(int, SpotLightData)> selected = [];
        int packedIndex = 0;
        foreach (SpotLightData light in lights)
        {
            if (!SpotLightPacking.IsValid(light)) continue;
            if (light.CastShadows && selected.Count < LocalLightShadowLimits.MaxShadowedSpotLights)
                selected.Add((packedIndex, light));
            packedIndex++;
            if (packedIndex >= SpotLightPacking.MaxLights) break;
        }
        return selected;
    }

    private static Matrix4x4 CreatePointViewProjection(
        PointLightData light,
        Vector3 direction,
        Vector3 up,
        float nearPlane)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(light.Position, light.Position + direction, up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI * 0.5f,
            1.0f,
            nearPlane,
            light.Range);
        return view * projection;
    }

    private static Matrix4x4 CreateSpotViewProjection(SpotLightData light, float nearPlane)
    {
        Vector3 direction = Vector3.Normalize(light.Direction);
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        float fieldOfView = Math.Clamp(light.OuterConeAngleDegrees * 2.0f, 1.0f, 179.0f) * MathF.PI / 180.0f;
        return Matrix4x4.CreateLookAt(light.Position, light.Position + direction, up)
            * Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, 1.0f, nearPlane, light.Range);
    }

    private static float GetNearPlane(float range)
    {
        float minimum = Math.Min(0.02f, range * 0.1f);
        float maximum = Math.Min(0.25f, range * 0.5f);
        return Math.Clamp(range * 0.0025f, minimum, maximum);
    }

    private static void GetPointFace(int face, out Vector3 direction, out Vector3 up)
    {
        (direction, up) = face switch
        {
            0 => (Vector3.UnitX, Vector3.UnitY),
            1 => (-Vector3.UnitX, Vector3.UnitY),
            2 => (Vector3.UnitY, -Vector3.UnitZ),
            3 => (-Vector3.UnitY, Vector3.UnitZ),
            4 => (Vector3.UnitZ, Vector3.UnitY),
            _ => (-Vector3.UnitZ, Vector3.UnitY)
        };
    }
}
