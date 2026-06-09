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
    private readonly Dictionary<WaterSurfaceComponent, ReflectionSurfaceState> _surfaces = [];
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

        List<WaterSurfaceComponent> activeSurfaces = [];
        foreach (WaterSurfaceComponent water in waterSurfaces)
        {
            if (water.Visible && water.MirrorReflectionEnabled && water.Alpha > 0.001f)
            {
                activeSurfaces.Add(water);
            }
            else
            {
                water.ClearPlanarReflection();
            }
        }

        HashSet<WaterSurfaceComponent> validSurfaces = [.. waterSurfaces];
        foreach (WaterSurfaceComponent stale in _surfaces.Keys.Where(surface => !validSurfaces.Contains(surface)).ToArray())
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

            foreach (WaterSurfaceComponent water in activeSurfaces)
            {
                ReflectionSurfaceState state = GetOrCreateSurfaceState(water);
                RenderTexture texture = state.Texture;
                texture.EnsureSize(textureWidth, textureHeight);

                ConfigureReflectionCamera(sourceCamera, state.Camera, water.Position.Y, texture.Width, texture.Height);

                texture.Bind();
                gl.Disable(GLEnum.ScissorTest);
                gl.Disable(GLEnum.StencilTest);
                gl.ColorMask(true, true, true, true);
                gl.DepthMask(true);
                gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

                applyCamera(state.Camera);
                DrawReflectionScene(gameTime, excluded);
                water.SetPlanarReflection(texture.ColorTextureId, state.Camera.View * state.Camera.Projection, texture.Width, texture.Height);
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

    private ReflectionSurfaceState GetOrCreateSurfaceState(WaterSurfaceComponent water)
    {
        if (!_surfaces.TryGetValue(water, out ReflectionSurfaceState? state))
        {
            state = new ReflectionSurfaceState
            {
                Texture = new RenderTexture(_game.GraphicsDevice.Gl, $"WaterReflection-{_surfaces.Count + 1}")
            };
            _surfaces[water] = state;
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
            if (drawable is WaterSurfaceComponent || excluded.Contains(drawable))
            {
                continue;
            }

            drawable.Draw(gameTime);
        }
    }

    private static void ConfigureReflectionCamera(
        OrbitCamera source,
        OrbitCamera target,
        float planeY,
        int width,
        int height)
    {
        Vector3 reflectedPosition = ReflectPoint(source.Position, planeY);
        Vector3 reflectedTarget = ReflectPoint(source.Target, planeY);
        if (Vector3.DistanceSquared(reflectedPosition, reflectedTarget) < 0.0001f)
        {
            reflectedTarget = reflectedPosition + ReflectVector(source.Front);
        }

        target.Width = Math.Max(width, 1);
        target.Height = Math.Max(height, 1);
        target.SetLookAt(reflectedPosition, reflectedTarget, ReflectVector(source.Up));
        target.ProjectionMode = source.ProjectionMode;
        target.Fov = source.Fov;
        target.OrthographicSize = source.OrthographicSize;
        target.NearClipPlane = source.NearClipPlane;
        target.FarClipPlane = source.FarClipPlane;
    }

    private static Vector3 ReflectPoint(Vector3 point, float planeY)
    {
        point.Y = (planeY * 2.0f) - point.Y;
        return point;
    }

    private static Vector3 ReflectVector(Vector3 direction)
    {
        direction.Y = -direction.Y;
        return Vector3.Normalize(direction);
    }
}
