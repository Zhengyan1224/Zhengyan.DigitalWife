using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game;

public abstract class Game : IDisposable
{
    private readonly List<GameComponent> _components = [];
    private GameComponent[] _sortedUpdateComponents = [];
    private DrawableGameComponent[] _sortedDrawableComponents = [];
    private readonly IWindow _window;
    private bool _disposed;
    private bool _initialized;
    private bool _isActive = true;
    private long _frameCount;

    protected Game(GameOptions? options = null)
    {
        Options = options ?? new GameOptions();

        WindowOptions windowOptions = WindowOptions.Default;
        windowOptions.Title = Options.Title;
        windowOptions.Size = Options.WindowSize;
        windowOptions.WindowState = Options.IsFullscreen ? WindowState.Fullscreen : WindowState.Normal;
        windowOptions.WindowBorder = Options.HideWindowBorder
            ? WindowBorder.Hidden
            : Options.IsResizable ? WindowBorder.Resizable : WindowBorder.Fixed;
        windowOptions.TopMost = Options.IsTopMost;
        windowOptions.TransparentFramebuffer = Options.TransparentFramebuffer;
        windowOptions.VSync = Options.VSync;
        windowOptions.Samples = Options.Samples;
        // The built-in shaders only require GLES 3.0, which is a much safer baseline on Windows/WGL.
        windowOptions.API = new GraphicsAPI(ContextAPI.OpenGLES, new APIVersion(3, 0));
        windowOptions.PreferredDepthBufferBits = Options.PreferredDepthBufferBits;
        windowOptions.PreferredStencilBufferBits = Options.PreferredStencilBufferBits;
        windowOptions.PreferredBitDepth = new Vector4D<int>(8);

        _window = Silk.NET.Windowing.Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.FocusChanged += isFocused => _isActive = isFocused;
    }

    public GameOptions Options { get; }

    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    public InputManager Input { get; private set; } = null!;

    public AudioEngine? Audio { get; private set; }

    public bool IsAudioAvailable => Audio is not null;

    public string? AudioStatusMessage { get; private set; }

    public IWindow Window => _window;

    public bool IsActive => _isActive;

    public IReadOnlyList<GameComponent> Components => _components;

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public AnimationTimingMode AnimationTimingMode
    {
        get => Options.AnimationTimingMode;
        set => Options.AnimationTimingMode = value;
    }

    public void SetWindowSize(int width, int height)
    {
        int clampedWidth = Math.Max(320, width);
        int clampedHeight = Math.Max(240, height);
        Options.WindowSize = new Vector2D<int>(clampedWidth, clampedHeight);
        _window.Size = Options.WindowSize;
    }

    public void SetFullscreen(bool fullscreen)
    {
        Options.IsFullscreen = fullscreen;
        _window.WindowState = fullscreen ? WindowState.Fullscreen : WindowState.Normal;
    }

    public void SetResizable(bool resizable)
    {
        Options.IsResizable = resizable;
        _window.WindowBorder = Options.HideWindowBorder
            ? WindowBorder.Hidden
            : resizable ? WindowBorder.Resizable : WindowBorder.Fixed;
    }

    public void SetTopMost(bool topMost)
    {
        Options.IsTopMost = topMost;
        _window.TopMost = topMost;
    }

    public void SetWindowBorderHidden(bool hidden)
    {
        Options.HideWindowBorder = hidden;
        _window.WindowBorder = hidden
            ? WindowBorder.Hidden
            : Options.IsResizable ? WindowBorder.Resizable : WindowBorder.Fixed;
    }

    public void Run() => _window.Run();

    public void Exit() => _window.Close();

    public T AddComponent<T>(T component) where T : GameComponent
    {
        _components.Add(component);
        SortComponents();

        if (_initialized)
        {
            component.Attach(this);
        }

        return component;
    }

    public bool RemoveComponent(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!_components.Remove(component))
        {
            return false;
        }

        SortComponents();
        component.Dispose();
        return true;
    }

    protected virtual void Initialize() { }

    protected virtual void LoadContent() { }

    protected virtual void Update(GameTime gameTime) { }

    protected virtual void LateUpdate(GameTime gameTime) { }

    protected virtual void Draw(GameTime gameTime) { }

    public virtual bool ShouldDrawComponent(DrawableGameComponent component) => true;

    protected virtual void UnloadContent() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnloadContent();

        for (int i = _components.Count - 1; i >= 0; i--)
        {
            _components[i].Dispose();
        }

        Audio?.Dispose();
        Input?.Dispose();
        GraphicsDevice?.Gl.Dispose();

        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        Zhengyan.DigitalWife.Mmd.Kernel.UseOpenCL = Options.UseOpenCL;

        GL gl = _window.CreateOpenGLES();
        GraphicsDevice = new GraphicsDevice(gl, Options.ClearColor, _window.Size);
        Input = new InputManager(_window.CreateInput());
        if (Options.EnableAudio)
        {
            try
            {
                Audio = new AudioEngine();
                AudioStatusMessage = "Audio enabled.";
            }
            catch (Exception ex)
            {
                Audio = null;
                AudioStatusMessage = $"Audio disabled: {ex.Message}";
                Console.Error.WriteLine($"Audio disabled: {ex.Message}");
            }
        }

        Initialize();

        foreach (GameComponent component in _components)
        {
            component.Attach(this);
        }

        _initialized = true;
        LoadContent();
    }

    private void OnResize(Vector2D<int> size)
    {
        if (!_initialized)
        {
            return;
        }

        GraphicsDevice.Resize(size);
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (!_initialized)
        {
            return;
        }

        Input.BeginFrame();

        GameTime gameTime = CreateGameTime(deltaSeconds);
        Update(gameTime);
        Audio?.Update();

        foreach (GameComponent component in _sortedUpdateComponents)
        {
            if (component.Enabled)
            {
                component.Update(gameTime);
            }
        }

        LateUpdate(gameTime);

        _frameCount++;
    }

    private void OnRender(double deltaSeconds)
    {
        if (!_initialized)
        {
            return;
        }

        GameTime gameTime = CreateGameTime(deltaSeconds);
        GraphicsDevice.Clear();

        Draw(gameTime);

        foreach (DrawableGameComponent component in _sortedDrawableComponents)
        {
            if (component.Visible && ShouldDrawComponent(component))
            {
                component.Draw(gameTime);
            }
        }
    }

    private void OnClosing()
    {
        Dispose();
    }

    private GameTime CreateGameTime(double deltaSeconds)
    {
        return new GameTime(TimeSpan.FromSeconds(_window.Time), TimeSpan.FromSeconds(deltaSeconds), _frameCount);
    }

    private void SortComponents()
    {
        _components.Sort(static (left, right) =>
        {
            int updateCompare = left.UpdateOrder.CompareTo(right.UpdateOrder);
            if (updateCompare != 0)
            {
                return updateCompare;
            }

            int leftDrawOrder = left is DrawableGameComponent leftDrawable ? leftDrawable.DrawOrder : int.MinValue;
            int rightDrawOrder = right is DrawableGameComponent rightDrawable ? rightDrawable.DrawOrder : int.MinValue;
            return leftDrawOrder.CompareTo(rightDrawOrder);
        });

        _sortedUpdateComponents = [.. _components.OrderBy(component => component.UpdateOrder)];
        _sortedDrawableComponents = [.. _components
            .OfType<DrawableGameComponent>()
            .OrderBy(component => component.DrawOrder)];
    }
}

