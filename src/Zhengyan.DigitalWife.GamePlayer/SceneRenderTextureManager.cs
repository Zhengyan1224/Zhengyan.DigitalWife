using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class SceneRenderTextureManager : IRuntimeTextureProvider, IDisposable
{
    private sealed class RenderTextureRuntimeState
    {
        public required IRenderTarget Texture { get; init; }

        public double LastRenderTimeSeconds { get; set; } = double.NegativeInfinity;
    }

    private readonly Zhengyan.DigitalWife.Mmd.Game.Game _game;
    private readonly Func<GameProjectScene> _getScene;
    private readonly Func<IReadOnlyList<DrawableGameComponent>> _getExcludedComponents;
    private readonly Dictionary<string, OrbitCamera> _cameras = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderTextureRuntimeState> _renderTextures = new(StringComparer.OrdinalIgnoreCase);
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

        if (!_renderTextures.TryGetValue(name, out RenderTextureRuntimeState? state))
        {
            return false;
        }

        textureId = state.Texture.LegacyColorTextureId;
        return textureId != 0;
    }

    public bool TryGetTextureHandle(string textureReference, out RuntimeTextureHandle handle)
    {
        string name = NormalizeRuntimeTextureName(textureReference);
        if (!string.IsNullOrWhiteSpace(name)
            && _renderTextures.TryGetValue(name, out RenderTextureRuntimeState? state)
            && (state.Texture.LegacyColorTextureId != 0 || state.Texture.Backend == GraphicsBackend.Vulkan))
        {
            handle = new RuntimeTextureHandle(
                state.Texture.Backend,
                state.Texture.LegacyColorTextureId,
                state.Texture.NativeColorResource);
            return true;
        }

        handle = default;
        return false;
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
            HashSet<string> validRenderTextures = new(StringComparer.OrdinalIgnoreCase);

            foreach (RenderTextureSettings settings in scene.RenderTextures.Where(item => item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(settings.Name))
                {
                    continue;
                }

                validRenderTextures.Add(settings.Name);
                RenderTextureRuntimeState state = GetOrCreateRenderTexture(settings.Name);
                IRenderTarget renderTexture = state.Texture;
                renderTexture.EnsureSize(settings.Width, settings.Height);
                OrbitCamera camera = ResolveCamera(settings.Camera, mainCamera);
                camera.Width = renderTexture.Width;
                camera.Height = renderTexture.Height;

                if (!ShouldRender(settings, state, gameTime.TotalSeconds))
                {
                    continue;
                }

                Vector4 clearColor = settings.ClearColor.ToVector4();
                renderTexture.BeginPass(clearColor);

                applyCamera(camera);
                DrawSceneComponents(gameTime);
                renderTexture.EndPass();
                state.LastRenderTimeSeconds = gameTime.TotalSeconds;
            }

            foreach (string staleName in _renderTextures.Keys.Where(name => !validRenderTextures.Contains(name)).ToArray())
            {
                _renderTextures[staleName].Texture.Dispose();
                _renderTextures.Remove(staleName);
            }
        }
            finally
            {
                restoreCamera(mainCamera);
                _game.GraphicsDevice.RestoreBackBuffer();
                _isRendering = false;
            }
    }

    public void Dispose()
    {
        foreach (RenderTextureRuntimeState state in _renderTextures.Values)
        {
            state.Texture.Dispose();
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

    private RenderTextureRuntimeState GetOrCreateRenderTexture(string name)
    {
        if (!_renderTextures.TryGetValue(name, out RenderTextureRuntimeState? state))
        {
            state = new RenderTextureRuntimeState
            {
                Texture = _game.GraphicsDevice.CreateRenderTarget(name)
            };
            _renderTextures[name] = state;
        }

        return state;
    }

    private void DrawSceneComponents(GameTime gameTime)
    {
        IReadOnlyList<DrawableGameComponent> excludedComponents = _getExcludedComponents();
        HashSet<DrawableGameComponent> excluded = [.. excludedComponents];
        foreach (GameComponent component in _game.Components)
        {
            if (component is DrawableGameComponent drawable
                && drawable.Visible
                && !excluded.Contains(drawable))
            {
                drawable.Draw(gameTime);
            }
        }
    }

    private static bool ShouldRender(RenderTextureSettings settings, RenderTextureRuntimeState state, double nowSeconds)
    {
        string mode = NormalizeRefreshMode(settings.RefreshMode);
        return mode switch
        {
            "fixed_rate" => nowSeconds - state.LastRenderTimeSeconds >= Math.Max(0.01, settings.RefreshIntervalSeconds),
            "on_demand" => double.IsNegativeInfinity(state.LastRenderTimeSeconds),
            _ => true
        };
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

    private static string NormalizeRefreshMode(string refreshMode)
    {
        string normalized = (refreshMode ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "fixed_rate" or "fixed" => "fixed_rate",
            "on_demand" or "ondemand" => "on_demand",
            _ => "every_frame"
        };
    }
}
