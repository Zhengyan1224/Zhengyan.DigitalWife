using System.Numerics;
using Silk.NET.OpenGLES;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.Samples.GameEditor;

internal sealed class SceneRenderTextureManager : IRuntimeTextureProvider, IDisposable
{
    private readonly Zhengyan.DigitalWife.Mmd.Game.Game _game;
    private readonly Func<GameProjectScene> _getScene;
    private readonly Func<IReadOnlyList<DrawableGameComponent>> _getExcludedComponents;
    private readonly Dictionary<string, OrbitCamera> _cameras = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderTexture> _renderTextures = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRendering;

    public SceneRenderTextureManager(
        Zhengyan.DigitalWife.Mmd.Game.Game game,
        Func<GameProjectScene> getScene,
        Func<IReadOnlyList<DrawableGameComponent>> getExcludedComponents)
    {
        _game = game;
        _getScene = getScene;
        _getExcludedComponents = getExcludedComponents;
    }

    public IReadOnlyDictionary<string, OrbitCamera> Cameras => _cameras;

    public void SyncCameras(OrbitCamera mainCamera)
    {
        GameProjectScene scene = _getScene();
        EnsureLegacyCamera(scene);

        HashSet<string> validNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (SceneCameraSettings settings in scene.Cameras.Where(camera => camera.Enabled))
        {
            if (string.IsNullOrWhiteSpace(settings.Name))
            {
                settings.Name = string.IsNullOrWhiteSpace(settings.Id) ? "Camera" : settings.Id;
            }

            validNames.Add(settings.Name);
            OrbitCamera camera = GetOrCreateCamera(settings.Name);
            if (IsMainCamera(scene, settings))
            {
                CopyCamera(mainCamera, camera);
            }
            else
            {
                ApplyCameraSettings(camera, settings.Camera);
            }
        }

        foreach (string staleName in _cameras.Keys.Where(name => !validNames.Contains(name)).ToArray())
        {
            _cameras.Remove(staleName);
        }
    }

    public OrbitCamera ResolveCamera(string name, OrbitCamera fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        return _cameras.TryGetValue(name, out OrbitCamera? camera) ? camera : fallback;
    }

    public bool TryGetTexture(string textureReference, out uint textureId)
    {
        textureId = 0;
        string name = NormalizeRuntimeTextureName(textureReference);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!_renderTextures.TryGetValue(name, out RenderTexture? renderTexture))
        {
            return false;
        }

        textureId = renderTexture.ColorTextureId;
        return textureId != 0;
    }

    public void RenderAll(
        GameTime gameTime,
        OrbitCamera mainCamera,
        Action<OrbitCamera> applyCamera,
        Action<OrbitCamera> restoreCamera)
    {
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        try
        {
            SyncCameras(mainCamera);
            GameProjectScene scene = _getScene();
            GL gl = _game.GraphicsDevice.Gl;
            HashSet<string> validRenderTextures = new(StringComparer.OrdinalIgnoreCase);

            foreach (RenderTextureSettings settings in scene.RenderTextures.Where(item => item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(settings.Name))
                {
                    continue;
                }

                validRenderTextures.Add(settings.Name);
                RenderTexture renderTexture = GetOrCreateRenderTexture(settings.Name);
                renderTexture.EnsureSize(settings.Width, settings.Height);
                OrbitCamera camera = ResolveCamera(settings.Camera, mainCamera);
                camera.Width = renderTexture.Width;
                camera.Height = renderTexture.Height;

                renderTexture.Bind();
                gl.Disable(GLEnum.ScissorTest);
                gl.Disable(GLEnum.StencilTest);
                gl.ColorMask(true, true, true, true);
                gl.DepthMask(true);
                Vector4 clearColor = settings.ClearColor.ToVector4();
                gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

                applyCamera(camera);
                DrawSceneComponents(gameTime);
            }

            foreach (string staleName in _renderTextures.Keys.Where(name => !validRenderTextures.Contains(name)).ToArray())
            {
                _renderTextures[staleName].Dispose();
                _renderTextures.Remove(staleName);
            }
        }
        finally
        {
            restoreCamera(mainCamera);
            _game.GraphicsDevice.Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            _game.GraphicsDevice.Gl.Viewport(0, 0, (uint)Math.Max(_game.GraphicsDevice.BackBufferSize.X, 1), (uint)Math.Max(_game.GraphicsDevice.BackBufferSize.Y, 1));
            _isRendering = false;
        }
    }

    public void Dispose()
    {
        foreach (RenderTexture renderTexture in _renderTextures.Values)
        {
            renderTexture.Dispose();
        }

        _renderTextures.Clear();
    }

    private OrbitCamera GetOrCreateCamera(string name)
    {
        if (!_cameras.TryGetValue(name, out OrbitCamera? camera))
        {
            camera = new OrbitCamera();
            _cameras[name] = camera;
        }

        return camera;
    }

    private RenderTexture GetOrCreateRenderTexture(string name)
    {
        if (!_renderTextures.TryGetValue(name, out RenderTexture? renderTexture))
        {
            renderTexture = new RenderTexture(_game.GraphicsDevice.Gl, name);
            _renderTextures[name] = renderTexture;
        }

        return renderTexture;
    }

    private void DrawSceneComponents(GameTime gameTime)
    {
        IReadOnlyList<DrawableGameComponent> excludedComponents = _getExcludedComponents();
        foreach (DrawableGameComponent component in _game.Components
            .OfType<DrawableGameComponent>()
            .Where(component => component.Visible && !excludedComponents.Contains(component))
            .OrderBy(component => component.DrawOrder))
        {
            component.Draw(gameTime);
        }
    }

    private static void EnsureLegacyCamera(GameProjectScene scene)
    {
        if (scene.Cameras.Count == 0)
        {
            scene.Cameras.Add(new SceneCameraSettings
            {
                Name = string.IsNullOrWhiteSpace(scene.MainCamera) ? "Main Camera" : scene.MainCamera,
                IsMain = true,
                Camera = scene.Camera
            });
        }

        SceneCameraSettings? main = scene.Cameras.FirstOrDefault(camera => camera.IsMain)
            ?? scene.Cameras.FirstOrDefault(camera => string.Equals(camera.Name, scene.MainCamera, StringComparison.OrdinalIgnoreCase));
        if (main is null)
        {
            main = scene.Cameras[0];
        }

        main.IsMain = true;
        scene.MainCamera = main.Name;
        scene.Camera = main.Camera;
    }

    private static bool IsMainCamera(GameProjectScene scene, SceneCameraSettings settings)
    {
        return settings.IsMain || string.Equals(settings.Name, scene.MainCamera, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCameraSettings(OrbitCamera target, CameraSettings settings)
    {
        target.SetLookAt(settings.Position.ToVector3(), settings.Target.ToVector3());
        target.ProjectionMode = NormalizeProjectionMode(settings.ProjectionMode) == "orthographic"
            ? CameraProjectionMode.Orthographic
            : CameraProjectionMode.Perspective;
        target.Fov = settings.Fov;
        target.OrthographicSize = settings.OrthographicSize;
        target.NearClipPlane = settings.NearClipPlane;
        target.FarClipPlane = settings.FarClipPlane;
    }

    private static void CopyCamera(OrbitCamera source, OrbitCamera target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.SetLookAt(source.Position, source.Target);
        target.ProjectionMode = source.ProjectionMode;
        target.Fov = source.Fov;
        target.OrthographicSize = source.OrthographicSize;
        target.NearClipPlane = source.NearClipPlane;
        target.FarClipPlane = source.FarClipPlane;
    }

    private static string NormalizeProjectionMode(string projectionMode)
    {
        string normalized = (projectionMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "orthographic" or "ortho" ? "orthographic" : "perspective";
    }

    private static string NormalizeRuntimeTextureName(string textureReference)
    {
        string trimmed = (textureReference ?? string.Empty).Trim();
        return trimmed.StartsWith("rt:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["rt:".Length..].Trim()
            : string.Empty;
    }
}
