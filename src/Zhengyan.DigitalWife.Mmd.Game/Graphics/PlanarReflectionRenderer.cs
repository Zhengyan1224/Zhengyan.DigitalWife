using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.Mmd.Game.Components;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public sealed class PlanarReflectionRenderer : IDisposable
{
    private sealed class ReflectionSurfaceState
    {
        public required RenderTexture Texture { get; init; }

        public OrbitCamera Camera { get; } = new();
    }

    private readonly Game _game;
    private readonly Dictionary<object, ReflectionSurfaceState> _surfaces = [];
    private bool _isRendering;
    private bool _disposed;
    private int _resolutionDivisor = 2;

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
            if (plane.Visible && plane.MirrorReflectionEnabled && plane.MirrorReflectionStrength > 0.001f && plane.TryGetMirrorPlane(out Vector3 normal, out float distance))
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
            _surfaces[stale].Texture.Dispose();
            _surfaces.Remove(stale);
        }

        if (activeSurfaces.Count == 0)
        {
            return;
        }

        GL gl = _game.GraphicsDevice.Gl;
        _isRendering = true;
        try
        {
            HashSet<DrawableGameComponent> excluded = [.. excludedComponents];
            foreach (ReflectionSurface surface in activeSurfaces)
            {
                if (surface.Key is DrawableGameComponent drawableSurface)
                {
                    excluded.Add(drawableSurface);
                }
            }

            foreach (ReflectionSurface surface in activeSurfaces)
            {
                ReflectionSurfaceState state = GetOrCreateSurfaceState(surface.Key);
                RenderTexture texture = state.Texture;
                texture.EnsureSize(textureWidth, textureHeight);

                ConfigureReflectionCamera(sourceCamera, state.Camera, surface.Normal, surface.Distance, texture.Width, texture.Height);

                texture.Bind();
                gl.Disable(GLEnum.ScissorTest);
                gl.Disable(GLEnum.StencilTest);
                gl.ColorMask(true, true, true, true);
                gl.DepthMask(true);
                gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

                applyCamera(state.Camera);
                DrawReflectionScene(gameTime, excluded);
                surface.SetReflection(texture.ColorTextureId, state.Camera.View * state.Camera.Projection, texture.Width, texture.Height);
            }
        }
        finally
        {
            restoreCamera(sourceCamera);
            if (restoreRenderTarget is not null)
            {
                restoreRenderTarget();
            }
            else
            {
                gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                gl.Viewport(0, 0, (uint)targetWidth, (uint)targetHeight);
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
        foreach (ReflectionSurfaceState state in _surfaces.Values)
        {
            state.Texture.Dispose();
        }

        _surfaces.Clear();
    }

    private ReflectionSurfaceState GetOrCreateSurfaceState(object key)
    {
        if (!_surfaces.TryGetValue(key, out ReflectionSurfaceState? state))
        {
            state = new ReflectionSurfaceState
            {
                Texture = new RenderTexture(_game.GraphicsDevice.Gl, $"PlanarReflection-{_surfaces.Count + 1}")
            };
            _surfaces[key] = state;
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
        int height)
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
    }

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

        public void SetReflection(uint textureId, Matrix4x4 reflectionViewProjection, int width, int height)
        {
            if (_water is not null)
            {
                _water.SetPlanarReflection(textureId, reflectionViewProjection, width, height);
            }
            else
            {
                _plane?.SetPlanarReflection(textureId, reflectionViewProjection, width, height);
            }
        }
    }
}
