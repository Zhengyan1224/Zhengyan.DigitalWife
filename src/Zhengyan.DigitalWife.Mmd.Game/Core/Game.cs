using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game;

public abstract class Game : IDisposable
{
    private readonly List<GameComponent> _components = [];
    private GameComponent[] _sortedUpdateComponents = [];
    private DrawableGameComponent[] _sortedDrawableComponents = [];
    private readonly IWindow? _window;
    private readonly IRenderer _renderer;
    private bool _disposed;
    private bool _initialized;
    private bool _isActive = true;
    private long _frameCount;
    private double _hostedTotalSeconds;

    protected Game(GameOptions? options = null)
    {
        Options = options ?? new GameOptions();
        RendererSelection = RendererFactory.Select(Options.GraphicsBackend);
        _renderer = RendererFactory.Create(RendererSelection);

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
        Options.Samples = AntiAliasingSamples.NormalizeRequested(Options.Samples);
        windowOptions.Samples = Options.Samples;
        RendererFactory.ConfigureWindow(ref windowOptions, RendererSelection.ResolvedBackend);
        windowOptions.PreferredDepthBufferBits = Options.PreferredDepthBufferBits;
        windowOptions.PreferredStencilBufferBits = Options.PreferredStencilBufferBits;
        windowOptions.PreferredBitDepth = new Vector4D<int>(8);

        _window = Silk.NET.Windowing.Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.FocusChanged += isFocused =>
        {
            _isActive = isFocused;
            if (!isFocused)
            {
                Input?.CancelTouches();
            }
        };
    }

    /// <summary>
    /// Creates a game whose renderer and presentation surface are owned by an external platform host.
    /// The supplied renderer must already be initialized.
    /// </summary>
    protected Game(GameOptions options, IRenderer initializedRenderer, Vector2D<int> backBufferSize)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _renderer = initializedRenderer ?? throw new ArgumentNullException(nameof(initializedRenderer));
        RendererSelection = new RendererSelection(options.GraphicsBackend, initializedRenderer.Backend);
        Options.Samples = AntiAliasingSamples.NormalizeRequested(Options.Samples);
        GraphicsDevice = new GraphicsDevice(_renderer, Options.ClearColor);
        if (GraphicsDevice.BackBufferSize.X != backBufferSize.X
            || GraphicsDevice.BackBufferSize.Y != backBufferSize.Y)
        {
            GraphicsDevice.Resize(backBufferSize);
        }
        Zhengyan.DigitalWife.Mmd.Kernel.UseOpenCL =
            GraphicsDevice.Backend == GraphicsBackend.OpenGL && Options.UseOpenCL;
    }

    public GameOptions Options { get; }

    public RendererSelection RendererSelection { get; }

    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    public InputManager Input { get; private set; } = null!;

    public AudioEngine? Audio { get; private set; }

    public bool IsAudioAvailable => Audio is not null;

    public string? AudioStatusMessage { get; private set; }

    public IWindow Window => _window
        ?? throw new InvalidOperationException("This game is driven by an external platform host and has no Silk.NET window.");

    public bool IsActive => _isActive;

    // Planar reflections render the scene a second time with a mirrored camera.
    // Components can use this context to skip view-dependent auxiliary passes.
    internal bool IsPlanarReflectionPass { get; set; }

    public IReadOnlyList<GameComponent> Components => _components;

    public string Title
    {
        get => _window?.Title ?? Options.Title;
        set
        {
            Options.Title = value;
            if (_window is not null) _window.Title = value;
        }
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
        if (_window is not null) _window.Size = Options.WindowSize;
    }

    public void SetFullscreen(bool fullscreen)
    {
        Options.IsFullscreen = fullscreen;
        if (_window is not null) _window.WindowState = fullscreen ? WindowState.Fullscreen : WindowState.Normal;
    }

    public void SetResizable(bool resizable)
    {
        Options.IsResizable = resizable;
        if (_window is not null)
        {
            _window.WindowBorder = Options.HideWindowBorder
                ? WindowBorder.Hidden
                : resizable ? WindowBorder.Resizable : WindowBorder.Fixed;
        }
    }

    public void SetTopMost(bool topMost)
    {
        Options.IsTopMost = topMost;
        if (_window is not null) _window.TopMost = topMost;
    }

    public void SetWindowBorderHidden(bool hidden)
    {
        Options.HideWindowBorder = hidden;
        if (_window is not null)
        {
            _window.WindowBorder = hidden
                ? WindowBorder.Hidden
                : Options.IsResizable ? WindowBorder.Resizable : WindowBorder.Fixed;
        }
    }

    public void Run() => Window.Run();

    public void Exit() => Window.Close();

    public void InitializeHosted()
    {
        if (_window is not null)
        {
            throw new InvalidOperationException("Hosted initialization is only valid for an externally hosted game.");
        }
        if (_initialized)
        {
            return;
        }

        InitializeGameContent();
    }

    public void ResizeHosted(Vector2D<int> size)
    {
        if (_window is not null)
        {
            throw new InvalidOperationException("Hosted resize is only valid for an externally hosted game.");
        }
        if (_initialized) GraphicsDevice.Resize(size);
    }

    public void UpdateHosted(double deltaSeconds)
    {
        if (_window is not null)
        {
            throw new InvalidOperationException("Hosted update is only valid for an externally hosted game.");
        }
        _hostedTotalSeconds += Math.Max(deltaSeconds, 0.0);
        UpdateFrame(deltaSeconds);
    }

    public void RenderHosted(double deltaSeconds)
    {
        if (_window is not null)
        {
            throw new InvalidOperationException("Hosted rendering is only valid for an externally hosted game.");
        }
        RenderFrame(deltaSeconds);
    }

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

    protected virtual void AfterPresent() { }

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
        _renderer.Dispose();

        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        IWindow window = Window;
        _renderer.Initialize(window, window.Size, Options.Samples);
        GraphicsDevice = new GraphicsDevice(_renderer, Options.ClearColor);

        // OpenCL is only a valid compute option for the OpenGL compatibility backend.
        Zhengyan.DigitalWife.Mmd.Kernel.UseOpenCL =
            GraphicsDevice.Backend == GraphicsBackend.OpenGL && Options.UseOpenCL;

        Input = new InputManager(window.CreateInput(), window);
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

        InitializeGameContent();
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
        => UpdateFrame(deltaSeconds);

    private void UpdateFrame(double deltaSeconds)
    {
        if (!_initialized)
        {
            return;
        }

        Input?.BeginFrame();

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
        => RenderFrame(deltaSeconds);

    private void RenderFrame(double deltaSeconds)
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

        _renderer.Present();
        AfterPresent();
    }

    private void OnClosing()
    {
        Dispose();
    }

    private GameTime CreateGameTime(double deltaSeconds)
    {
        double totalSeconds = _window?.Time ?? _hostedTotalSeconds;
        return new GameTime(TimeSpan.FromSeconds(totalSeconds), TimeSpan.FromSeconds(deltaSeconds), _frameCount);
    }

    private void InitializeGameContent()
    {
        Initialize();

        foreach (GameComponent component in _components)
        {
            component.Attach(this);
        }

        _initialized = true;
        LoadContent();
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

