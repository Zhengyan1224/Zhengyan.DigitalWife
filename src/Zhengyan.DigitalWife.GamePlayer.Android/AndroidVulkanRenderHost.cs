using Android.Runtime;
using Android.Util;
using Android.Views;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Numerics;
using Veldrid;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidVulkanRenderHost : IAndroidRenderHost
{
    private const string LogTag = "ZhengyanGamePlayer";

    private AndroidVulkanGame? _game;
    private RuntimeSceneManager? _sceneManager;
    private AndroidCSharpScriptHost? _scriptHost;
    private AndroidAudioHost? _audioHost;
    private GameProject? _project;
    private string _projectDirectory = string.Empty;
    private int _width = 1;
    private int _height = 1;
    private long _lastFrameTimeNanos;
    private bool _surfaceAvailable;
    private bool _disposed;
    private Surface? _surface;
    private readonly object _lifecycleLock = new();

    public GameProject? Project => _project;

    public void SetProject(GameProject? project, string? projectDirectory)
    {
        lock (_lifecycleLock)
        {
            SetProjectCore(project, projectDirectory);
        }
    }

    private void SetProjectCore(GameProject? project, string? projectDirectory)
    {
        DisposeRuntime();
        _project = project;
        _projectDirectory = projectDirectory ?? string.Empty;
        if (project is not null && !string.IsNullOrWhiteSpace(_projectDirectory))
        {
            _sceneManager = new RuntimeSceneManager(
                project,
                _projectDirectory,
                idOrName => _game?.TryResetPhysics(idOrName) == true,
                (id, collider) => _game?.TryCreateMeshCollider(id, collider),
                (id, node) => _game?.TryGetNodeWorld(id, node));
            _audioHost = new AndroidAudioHost(_projectDirectory);
            _scriptHost = new AndroidCSharpScriptHost(
                _projectDirectory,
                RequestSceneChange,
                (scene, name) => _audioHost?.Play(scene, name) == true,
                name => _audioHost?.Pause(name) == true,
                name => _audioHost?.Stop(name) == true,
                name => _game?.RequestRenderTextureRefresh(name) == true,
                (name, mode, interval) => _game?.ConfigureRenderTexture(name, mode, interval) == true,
                name => _game?.GetRenderTexture(name),
                () => _game?.GetRenderTextures() ?? [],
                ApplyMotion,
                (entity, frame, playing) =>
                {
                    _ = _game?.TrySetMotionState(entity.Id, frame, playing);
                },
                entity => _game?.GetPmxModel(entity.Id),
                (name, volume) => _audioHost?.SetVolume(name, volume) == true,
                (name, loop) => _audioHost?.SetLoop(name, loop) == true,
                name => _audioHost?.IsPlaying(name) == true);
            _sceneManager.SceneChanged += OnSceneChanged;
            _sceneManager.SceneLoadFailed += failure =>
                Log.Warn(LogTag, $"Runtime scene load failed '{failure.ScenePath}': {failure.Error.Message}");
            _sceneManager.LoadInitial();
        }

        if (_surfaceAvailable)
        {
            ReloadGame();
        }
    }

    public void CreateSurface(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        lock (_lifecycleLock)
        {
            DestroySurfaceCore();
            _surface = surface;
            _surfaceAvailable = true;
            ReloadGame();
            ResetFrameClock();
        }
    }

    public void Resize(int width, int height)
    {
        lock (_lifecycleLock)
        {
            _width = Math.Max(width, 1);
            _height = Math.Max(height, 1);
            _game?.ResizeHosted(new Vector2D<int>(_width, _height));
        }
    }

    public void Render(long frameTimeNanos, AndroidInputSnapshot input)
    {
        lock (_lifecycleLock)
        {
            if (_game is null || !_surfaceAvailable || _surface is null)
            {
                return;
            }

            double deltaSeconds = 0.0;
            if (_lastFrameTimeNanos != 0 && frameTimeNanos >= _lastFrameTimeNanos)
            {
                deltaSeconds = (frameTimeNanos - _lastFrameTimeNanos) / 1_000_000_000.0;
            }
            _lastFrameTimeNanos = frameTimeNanos;

            RuntimeScene? inputScene = _sceneManager?.Current;
            if (inputScene is not null) DispatchTouchEvents(inputScene, input);
            _sceneManager?.Update((float)deltaSeconds, ToCameraInput(input));
            RuntimeScene? scene = _sceneManager?.Current;
            if (scene is not null)
            {
                _scriptHost?.Update(scene, (float)deltaSeconds, input);
                _project!.Scene = scene.Definition;
            }
            try
            {
                _game.UpdateHosted(deltaSeconds);
                _game.RenderHostedWithoutPresent(deltaSeconds);
                _game.PresentHosted();
            }
            catch (Exception ex)
            {
                // Android may invalidate the native surface between frame callbacks
                // (rotation, backgrounding, or driver-level swapchain loss). Recreate
                // the Vulkan device on the next frame instead of killing the activity.
                Log.Warn(LogTag, $"Vulkan frame submission failed; recreating device. {ex}");
                _game.Dispose();
                _game = null;
                if (_surfaceAvailable && _surface is not null)
                {
                    ReloadGame();
                }
            }
        }
    }


    public void Pause() => ResetFrameClock();

    public void DestroySurface()
    {
        lock (_lifecycleLock)
        {
            DestroySurfaceCore();
        }
    }

    public void RequestSceneChange(string scenePath)
    {
        lock (_lifecycleLock)
        {
            _sceneManager?.RequestSceneChange(scenePath);
        }
    }

    public bool RequestRenderTextureRefresh(string idOrName)
    {
        lock (_lifecycleLock)
        {
            return _game?.RequestRenderTextureRefresh(idOrName) == true;
        }
    }

    public void DispatchContextMenuItem(ContextMenuSettings menu, ContextMenuItemSettings item, float x, float y)
    {
        RuntimeScene? scene = _sceneManager?.Current;
        if (scene is null) return;
        _scriptHost?.DispatchEvent(scene, new AndroidRuntimeEvent(
            "context_menu", item.Id, item.EventName,
            new Vector2(x / Math.Max(_width, 1), y / Math.Max(_height, 1)), item.Text));
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            DestroySurfaceCore();
            DisposeRuntime();
        }
    }

    private void ReloadGame()
    {
        _game?.Dispose();
        _game = null;
        if (!_surfaceAvailable || _surface is null || _project is null || _sceneManager?.Current is not { } scene)
        {
            return;
        }

        VulkanRenderer renderer = new()
        {
            WaitForIdleAfterPresent = true
        };
        AndroidVulkanGame? game = null;
        try
        {
#pragma warning disable CS0618
            SwapchainSource source = SwapchainSource.CreateAndroidSurface(_surface.Handle, JNIEnv.Handle);
#pragma warning restore CS0618
            int requestedMsaa = AndroidVulkanGame.ResolveAntiAliasingSamples(_project);
            // Android Vulkan surfaces are required to use the driver's supported FIFO
            // present mode. Some Mali drivers terminate the process while creating an
            // immediate-mode swapchain, so keep FIFO presentation here; frame pacing
            // remains governed by the Android Choreographer callback.
            renderer.Initialize(source, new Vector2D<int>(_width, _height), requestedMsaa);
            game = new AndroidVulkanGame(
                _project, scene, _projectDirectory, renderer, new Vector2D<int>(_width, _height));
            game.InitializeHosted();
            _game = game;
            _audioHost?.StartScene(scene);
            _scriptHost?.Start(scene);
            Log.Info(LogTag,
                $"Android graphics backend: {_game.GraphicsDevice.Backend}; " +
                $"renderer: {_game.GraphicsDevice.RendererName}; " +
                $"MSAA requested={_game.GraphicsDevice.RequestedAntiAliasingSamples}x, " +
                $"actual={_game.GraphicsDevice.AntiAliasingSamples}x; " +
                $"skinning={(_project.Runtime.UseVulkanCompute ? "Vulkan Compute" : "CPU compatibility")}; " +
                $"projectMsaa={_project.Window.AntiAliasingSamples}x; " +
                $"qualityProfile={_project.AndroidQuality.Profile}; " +
                $"shadow={_project.AndroidQuality.MaxShadowMapSize}px; " +
                $"localShadow={_project.AndroidQuality.MaxLocalShadowMapSize}px; " +
                $"reflections={_project.AndroidQuality.MaxReflectionSurfaces}; " +
                $"particles={_project.AndroidQuality.MaxParticleCount}");
        }
        catch
        {
            if (game is not null) game.Dispose();
            else renderer.Dispose();
            throw;
        }
    }

    private void OnSceneChanged(RuntimeSceneChange change)
    {
        if (change.Current is not null) _project!.Scene = change.Current.Definition;
        if (_surfaceAvailable) ReloadGame();
    }

    private void ApplyMotion(RuntimeScene scene, RuntimeEntity entity, string motionPath)
    {
        string absolutePath = GameProjectPath.ToAbsolute(_projectDirectory, motionPath);
        if (!File.Exists(absolutePath))
        {
            Log.Warn(LogTag, $"Android script VMD asset was not found: {absolutePath}");
            return;
        }

        entity.Definition.MotionLayers =
        [
            new MotionLayerSettings { Path = motionPath, Weight = 1.0f }
        ];
        entity.Definition.IsPlaying = true;
        entity.Definition.PlaybackSpeed = Math.Max(entity.Definition.PlaybackSpeed, 0.0001f);
        _game?.ApplyMotion(entity, motionPath);
        _ = scene;
    }

    private void DisposeRuntime()
    {
        _game?.Dispose();
        _game = null;
        _sceneManager?.Dispose();
        _sceneManager = null;
        _scriptHost?.Dispose();
        _scriptHost = null;
        _audioHost?.Dispose();
        _audioHost = null;
    }

    private void DestroySurfaceCore()
    {
        _surfaceAvailable = false;
        _surface = null;
        _game?.Dispose();
        _game = null;
        ResetFrameClock();
    }

    private static RuntimeCameraInput ToCameraInput(AndroidInputSnapshot input)
    {
        if (input.ActiveTouchCount == 0) return RuntimeCameraInput.None;
        AndroidTouchPoint[] active = input.Touches.Where(point => point.IsActive).ToArray();
        if (active.Length == 1) return new RuntimeCameraInput(active[0].Delta, Vector2.Zero, 0.0f);

        Vector2 currentA = active[0].PixelPosition;
        Vector2 currentB = active[1].PixelPosition;
        Vector2 previousA = currentA - active[0].Delta;
        Vector2 previousB = currentB - active[1].Delta;
        float currentDistance = Vector2.Distance(currentA, currentB);
        float previousDistance = Vector2.Distance(previousA, previousB);
        Vector2 pan = (active[0].Delta + active[1].Delta) * 0.5f;
        return new RuntimeCameraInput(Vector2.Zero, pan, (previousDistance - currentDistance) * 0.01f);
    }

    private void DispatchTouchEvents(RuntimeScene scene, AndroidInputSnapshot input)
    {
        if (_scriptHost is null || input.Touches.Count == 0) return;
        foreach (AndroidTouchPoint touch in input.Touches.Where(point => point.Phase is AndroidTouchPhase.Began or AndroidTouchPhase.Ended))
        {
            float x = touch.PixelPosition.X;
            float y = touch.PixelPosition.Y;
            string eventType = touch.Phase == AndroidTouchPhase.Began ? "pointer_down" : "pointer_up";
            bool handled = false;
            foreach (GuiControlSettings control in scene.Definition.GuiControls.Where(control => control.Visible).Reverse())
            {
                LayoutRect rect = LayoutResolver.Resolve(control.LayoutMode, control.X, control.Y, control.Width, control.Height,
                    _width, _height, _project?.Window.Width ?? _width, _project?.Window.Height ?? _height);
                if (x < rect.X || x > rect.X + rect.Width || y < rect.Y || y > rect.Y + rect.Height) continue;
                _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                    "gui", control.Id,
                    touch.Phase == AndroidTouchPhase.Ended ? control.EventName : eventType,
                    touch.Position, control.Name, control.TargetEntity));
                handled = true;
                break;
            }

            if (!handled)
            {
                foreach (SpriteSettings sprite in scene.Definition.Sprites.Where(sprite => sprite.Visible).Reverse())
                {
                    if (!SpriteLayoutResolver.ContainsPoint(sprite, x, y, _width, _height,
                        _project?.Window.Width ?? _width, _project?.Window.Height ?? _height))
                    {
                        continue;
                    }

                    _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                        "sprite", sprite.Id,
                        touch.Phase == AndroidTouchPhase.Ended ? "clicked" : eventType,
                        touch.Position, sprite.Name, sprite.TargetEntity));
                    handled = true;
                    break;
                }
            }

            if (!handled && touch.Phase == AndroidTouchPhase.Ended)
            {
                foreach (ContextMenuSettings menu in scene.Definition.ContextMenus.Where(menu => menu.Enabled))
                {
                    _scriptHost.DispatchEvent(scene, new AndroidRuntimeEvent(
                        "context_menu", menu.Id, "opened", touch.Position));
                }
            }
        }
    }

    private void ResetFrameClock()
    {
        _lastFrameTimeNanos = 0;
    }
}
