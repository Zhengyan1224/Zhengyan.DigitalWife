using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class PlanarReflectionRenderer : IDisposable
{
    private sealed class ReflectionSurfaceState
    {
        public required IRenderTarget Target { get; init; }

        public OrbitCamera Camera { get; } = new();
    }

    private readonly Game _game;
    // A scene can contain multiple camera viewports. Keep one target per
    // surface/camera pair so a main viewport and a thumbnail never resize the
    // same Vulkan texture while commands from the previous pass are recorded.
    private readonly Dictionary<object, Dictionary<OrbitCamera, ReflectionSurfaceState>> _surfaces = [];
    private bool _isRendering;
    private bool _disposed;
    // Reflections are inspected as a full surface (for example a wall
    // mirror), so rendering them at half resolution makes facial textures and
    // thin geometry visibly blocky. Keep the default at native target size;
    // callers can still lower it through ResolutionDivisor when performance
    // is more important than reflection detail.
    private int _resolutionDivisor = 1;

    public PlanarReflectionRenderer(Game game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    public int ResolutionDivisor
    {
        get => _resolutionDivisor;
        set => _resolutionDivisor = Math.Clamp(value, 1, 8);
    }

    public void RenderAll(
        GameTime gameTime,
        OrbitCamera sourceCamera,
        IReadOnlyList<WaterSurfaceComponent> waterSurfaces,
        IReadOnlyList<TexturedPlaneComponent>? mirrorPlanes,
        IReadOnlyList<DrawableGameComponent> excludedComponents,
        Action<OrbitCamera> applyCamera,
        Action<OrbitCamera> restoreCamera,
        Vector4 clearColor,
        int targetWidth,
        int targetHeight,
        Action? restoreRenderTarget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sourceCamera);
        ArgumentNullException.ThrowIfNull(waterSurfaces);
        ArgumentNullException.ThrowIfNull(excludedComponents);
        ArgumentNullException.ThrowIfNull(applyCamera);
        ArgumentNullException.ThrowIfNull(restoreCamera);

        if (_isRendering)
        {
            return;
        }

        targetWidth = Math.Max(targetWidth, 1);
        targetHeight = Math.Max(targetHeight, 1);
        int textureWidth = Math.Max(targetWidth / _resolutionDivisor, 1);
        int textureHeight = Math.Max(targetHeight / _resolutionDivisor, 1);

        List<ReflectionSurface> activeSurfaces = [];
        foreach (WaterSurfaceComponent water in waterSurfaces)
        {
            if (water.Visible && water.MirrorReflectionEnabled && water.Alpha > 0.001f)
            {
                activeSurfaces.Add(ReflectionSurface.ForWater(water));
            }
            else
            {
                water.ClearPlanarReflection();
            }
        }

        foreach (TexturedPlaneComponent plane in mirrorPlanes ?? [])
        {
            if (plane.Visible
                && plane.MirrorReflectionEnabled
                && plane.MirrorReflectionStrength > 0.001f
                && plane.TryGetMirrorPlane(out Vector3 normal, out float distance))
            {
                activeSurfaces.Add(ReflectionSurface.ForPlane(plane, normal, distance));
            }
            else
            {
                plane.ClearPlanarReflection();
            }
        }

        HashSet<object> validSurfaces = [.. waterSurfaces.Cast<object>().Concat((mirrorPlanes ?? []).Cast<object>())];
        foreach (object stale in _surfaces.Keys.Where(surface => !validSurfaces.Contains(surface)).ToArray())
        {
            foreach (ReflectionSurfaceState state in _surfaces[stale].Values)
            {
                state.Target.Dispose();
            }

            _surfaces.Remove(stale);
        }

        if (activeSurfaces.Count == 0)
        {
            return;
        }

        _isRendering = true;
        try
        {
            _game.IsPlanarReflectionPass = true;
            HashSet<DrawableGameComponent> baseExcluded = [.. excludedComponents];
            foreach (ReflectionSurface surface in activeSurfaces.Where(candidate => candidate.Key is TexturedPlaneComponent))
            {
                if (surface.Key is DrawableGameComponent drawableSurface)
                {
                    baseExcluded.Add(drawableSurface);
                }
            }

            foreach (ReflectionSurface surface in activeSurfaces)
            {
                HashSet<DrawableGameComponent> excluded = [.. baseExcluded];
                if (surface.Key is WaterSurfaceComponent)
                {
                    foreach (ReflectionSurface waterSurface in activeSurfaces.Where(candidate => candidate.Key is WaterSurfaceComponent))
                    {
                        excluded.Add((DrawableGameComponent)waterSurface.Key);
                    }
                }

                ReflectionSurfaceState state = GetOrCreateSurfaceState(surface.Key, sourceCamera);
                IRenderTarget target = state.Target;
                target.EnsureSize(textureWidth, textureHeight);

                ConfigureReflectionCamera(
                    sourceCamera,
                    state.Camera,
                    surface.Normal,
                    surface.Distance,
                    target.Width,
                    target.Height,
                    clipAtSurface: surface.Key is TexturedPlaneComponent,
                    backend: _game.GraphicsDevice.Backend,
                    retainedPoint: sourceCamera.Position);

                target.BeginPass(clearColor);

                applyCamera(state.Camera);
                DrawReflectionScene(gameTime, excluded);
                surface.SetReflection(
                    new RuntimeTextureHandle(target.Backend, target.LegacyColorTextureId, target.NativeColorResource),
                    state.Camera.View * state.Camera.Projection,
                    target.Width,
                    target.Height);
            }
        }
        finally
        {
            _game.IsPlanarReflectionPass = false;
            restoreCamera(sourceCamera);
            if (restoreRenderTarget is not null)
            {
                restoreRenderTarget();
            }
            else
            {
                _game.GraphicsDevice.RestoreBackBuffer();
            }

            _isRendering = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Dictionary<OrbitCamera, ReflectionSurfaceState> states in _surfaces.Values)
        {
            foreach (ReflectionSurfaceState state in states.Values)
            {
                state.Target.Dispose();
            }
        }

        _surfaces.Clear();
    }

    private ReflectionSurfaceState GetOrCreateSurfaceState(object key, OrbitCamera sourceCamera)
    {
        if (!_surfaces.TryGetValue(key, out Dictionary<OrbitCamera, ReflectionSurfaceState>? states))
        {
            states = [];
            _surfaces[key] = states;
        }

        if (!states.TryGetValue(sourceCamera, out ReflectionSurfaceState? state))
        {
            state = new ReflectionSurfaceState
            {
                Target = _game.GraphicsDevice.CreateRenderTarget($"PlanarReflection-{_surfaces.Count + 1}-{states.Count + 1}")
            };
            states[sourceCamera] = state;
        }

        return state;
    }

    private void DrawReflectionScene(GameTime gameTime, HashSet<DrawableGameComponent> excluded)
    {
        foreach (DrawableGameComponent drawable in _game.Components
            .OfType<DrawableGameComponent>()
            .Where(component => component.Visible)
            .OrderBy(component => component.DrawOrder))
        {
            if (excluded.Contains(drawable))
            {
                continue;
            }

            drawable.Draw(gameTime);
        }
    }

    private static void ConfigureReflectionCamera(
        OrbitCamera source,
        OrbitCamera target,
        Vector3 planeNormal,
        float planeDistance,
        int width,
        int height,
        bool clipAtSurface,
        GraphicsBackend backend,
        Vector3 retainedPoint)
    {
        Vector3 normal = planeNormal.LengthSquared() > 0.0001f ? Vector3.Normalize(planeNormal) : Vector3.UnitY;
        Vector3 reflectedPosition = ReflectPoint(source.Position, normal, planeDistance);
        Vector3 reflectedTarget = ReflectPoint(source.Target, normal, planeDistance);
        if (Vector3.DistanceSquared(reflectedPosition, reflectedTarget) < 0.0001f)
        {
            reflectedTarget = reflectedPosition + ReflectVector(source.Front, normal);
        }

        target.Width = Math.Max(width, 1);
        target.Height = Math.Max(height, 1);
        target.SetLookAt(reflectedPosition, reflectedTarget, ReflectVector(source.Up, normal));
        target.ProjectionMode = source.ProjectionMode;
        target.Fov = source.Fov;
        target.OrthographicSize = source.OrthographicSize;
        target.NearClipPlane = source.NearClipPlane;
        target.FarClipPlane = source.FarClipPlane;
        target.ProjectionOverride = null;

        if (clipAtSurface && TryCreateObliqueProjection(
            target.View,
            target.Projection,
            normal,
            planeDistance,
            retainedPoint,
            backend,
            out Matrix4x4 obliqueProjection))
        {
            target.ProjectionOverride = obliqueProjection;
        }
    }

    private static bool TryCreateObliqueProjection(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector3 planeNormal,
        float planeDistance,
        Vector3 retainedPoint,
        GraphicsBackend backend,
        out Matrix4x4 result)
    {
        result = projection;
        float retainedSide = Vector3.Dot(planeNormal, retainedPoint) + planeDistance;
        if (retainedSide < 0.0f)
        {
            planeNormal = -planeNormal;
            planeDistance = -planeDistance;
        }

        // Keep the boundary just behind the visible mirror surface to avoid
        // precision noise from geometry that is exactly coplanar with it.
        const float clipBias = 0.01f;
        Vector4 worldPlane = new(planeNormal, planeDistance - clipBias);
        if (!Matrix4x4.Invert(view, out Matrix4x4 inverseView)
            || !Matrix4x4.Invert(projection, out Matrix4x4 inverseProjection))
        {
            return false;
        }

        Vector4 eyePlane = MultiplyMatrixByColumn(inverseView, worldPlane);
        Vector4 clipCorner = new(SignNotZero(eyePlane.X), SignNotZero(eyePlane.Y), 1.0f, 1.0f);
        Vector4 eyeCorner = Vector4.Transform(clipCorner, inverseProjection);
        float denominator = Vector4.Dot(eyePlane, eyeCorner);
        if (MathF.Abs(denominator) <= 0.000001f)
        {
            return false;
        }

        float scale = (backend == GraphicsBackend.OpenGL ? 2.0f : 1.0f) / denominator;
        Vector4 scaledPlane = eyePlane * scale;
        if (backend == GraphicsBackend.OpenGL)
        {
            result.M13 = scaledPlane.X - projection.M14;
            result.M23 = scaledPlane.Y - projection.M24;
            result.M33 = scaledPlane.Z - projection.M34;
            result.M43 = scaledPlane.W - projection.M44;
        }
        else
        {
            result.M13 = scaledPlane.X;
            result.M23 = scaledPlane.Y;
            result.M33 = scaledPlane.Z;
            result.M43 = scaledPlane.W;
        }

        return true;
    }

    private static Vector4 MultiplyMatrixByColumn(Matrix4x4 matrix, Vector4 column)
    {
        return new Vector4(
            (matrix.M11 * column.X) + (matrix.M12 * column.Y) + (matrix.M13 * column.Z) + (matrix.M14 * column.W),
            (matrix.M21 * column.X) + (matrix.M22 * column.Y) + (matrix.M23 * column.Z) + (matrix.M24 * column.W),
            (matrix.M31 * column.X) + (matrix.M32 * column.Y) + (matrix.M33 * column.Z) + (matrix.M34 * column.W),
            (matrix.M41 * column.X) + (matrix.M42 * column.Y) + (matrix.M43 * column.Z) + (matrix.M44 * column.W));
    }

    private static float SignNotZero(float value) => value >= 0.0f ? 1.0f : -1.0f;

    private static Vector3 ReflectPoint(Vector3 point, Vector3 normal, float distance)
    {
        return point - (2.0f * (Vector3.Dot(normal, point) + distance) * normal);
    }

    private static Vector3 ReflectVector(Vector3 direction, Vector3 normal)
    {
        return Vector3.Normalize(direction - (2.0f * Vector3.Dot(direction, normal) * normal));
    }

    private readonly struct ReflectionSurface
    {
        private readonly WaterSurfaceComponent? _water;
        private readonly TexturedPlaneComponent? _plane;

        private ReflectionSurface(WaterSurfaceComponent water)
        {
            _water = water;
            _plane = null;
            Key = water;
            Normal = Vector3.UnitY;
            Distance = -water.Position.Y;
        }

        private ReflectionSurface(TexturedPlaneComponent plane, Vector3 normal, float distance)
        {
            _water = null;
            _plane = plane;
            Key = plane;
            Normal = normal;
            Distance = distance;
        }

        public object Key { get; }

        public Vector3 Normal { get; }

        public float Distance { get; }

        public static ReflectionSurface ForWater(WaterSurfaceComponent water) => new(water);

        public static ReflectionSurface ForPlane(TexturedPlaneComponent plane, Vector3 normal, float distance) => new(plane, normal, distance);

        public void SetReflection(RuntimeTextureHandle texture, Matrix4x4 reflectionViewProjection, int width, int height)
        {
            if (_water is not null)
            {
                _water.SetPlanarReflection(texture, reflectionViewProjection, width, height);
            }
            else
            {
                _plane?.SetPlanarReflection(texture, reflectionViewProjection, width, height);
            }
        }
    }
}
