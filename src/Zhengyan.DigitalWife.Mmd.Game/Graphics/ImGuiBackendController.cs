using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Veldrid;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public interface IImGuiBackendController : IDisposable
{
    void Update(float deltaSeconds);
    void Render();
    nint GetTextureBinding(RuntimeTextureHandle texture);
}

public static class ImGuiBackendController
{
    public static IImGuiBackendController Create(Game game, Action? configureFonts = null)
    {
        return game.GraphicsDevice.Renderer switch
        {
            OpenGlRenderer => new OpenGlImGuiBackendController(game, configureFonts),
            VulkanRenderer vulkan => new VulkanImGuiBackendController(game, vulkan, configureFonts),
            _ => throw new NotSupportedException($"ImGui is not available on {game.GraphicsDevice.Backend}.")
        };
    }
}

internal sealed class OpenGlImGuiBackendController : IImGuiBackendController
{
    private readonly ImGuiController _controller;

    public OpenGlImGuiBackendController(Game game, Action? configureFonts)
    {
        _controller = configureFonts is null
            ? new ImGuiController(game.GraphicsDevice.Gl, game.Window, game.Input.Context)
            : new ImGuiController(game.GraphicsDevice.Gl, game.Window, game.Input.Context, configureFonts);
    }

    public void Update(float deltaSeconds) => _controller.Update(deltaSeconds);
    public void Render() => _controller.Render();
    public nint GetTextureBinding(RuntimeTextureHandle texture) => (nint)texture.LegacyTextureId;
    public void Dispose() => _controller.Dispose();
}

internal sealed class VulkanImGuiBackendController : IImGuiBackendController
{
    private readonly Game _game;
    private readonly VulkanRenderer _renderer;
    private readonly Veldrid.ImGuiRenderer _controller;
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;
    private readonly Dictionary<TextureView, nint> _bindings = [];
    private float _wheelX;
    private float _wheelY;

    public VulkanImGuiBackendController(Game game, VulkanRenderer renderer, Action? configureFonts)
    {
        _game = game;
        _renderer = renderer;
        _controller = new Veldrid.ImGuiRenderer(
            renderer.NativeDevice,
            renderer.NativeOutputDescription,
            Math.Max(game.GraphicsDevice.BackBufferSize.X, 1),
            Math.Max(game.GraphicsDevice.BackBufferSize.Y, 1));
        if (configureFonts is not null)
        {
            configureFonts();
            _controller.RecreateFontDeviceTexture(renderer.NativeDevice);
        }

        _keyboard = game.Input.Context.Keyboards.FirstOrDefault();
        _mouse = game.Input.Context.Mice.FirstOrDefault();
        if (_keyboard is not null) _keyboard.KeyChar += OnKeyChar;
        if (_mouse is not null) _mouse.Scroll += OnScroll;
    }

    public void Update(float deltaSeconds)
    {
        SilkInputSnapshot snapshot = new(_mouse, _keyboard, _wheelY);
        _wheelX = _wheelY = 0;
        _controller.Update(Math.Max(deltaSeconds, 1f / 1000f), snapshot);
    }

    public void Render() => _controller.Render(_renderer.NativeDevice, _renderer.NativeCommandList);

    public nint GetTextureBinding(RuntimeTextureHandle texture)
    {
        if (texture.NativeResource is not TextureView view) return 0;
        if (!_bindings.TryGetValue(view, out nint binding))
        {
            binding = _controller.GetOrCreateImGuiBinding(_renderer.NativeDevice.ResourceFactory, view);
            _bindings.Add(view, binding);
        }
        return binding;
    }

    public void Dispose()
    {
        if (_keyboard is not null) _keyboard.KeyChar -= OnKeyChar;
        if (_mouse is not null) _mouse.Scroll -= OnScroll;
        _controller.Dispose();
    }

    private static readonly (SilkKey, ImGuiKey)[] KeyMap =
    [
        (SilkKey.Tab, ImGuiKey.Tab), (SilkKey.Left, ImGuiKey.LeftArrow), (SilkKey.Right, ImGuiKey.RightArrow),
        (SilkKey.Up, ImGuiKey.UpArrow), (SilkKey.Down, ImGuiKey.DownArrow), (SilkKey.PageUp, ImGuiKey.PageUp),
        (SilkKey.PageDown, ImGuiKey.PageDown), (SilkKey.Home, ImGuiKey.Home), (SilkKey.End, ImGuiKey.End),
        (SilkKey.Delete, ImGuiKey.Delete), (SilkKey.Backspace, ImGuiKey.Backspace), (SilkKey.Enter, ImGuiKey.Enter),
        (SilkKey.Escape, ImGuiKey.Escape), (SilkKey.Space, ImGuiKey.Space), (SilkKey.A, ImGuiKey.A),
        (SilkKey.C, ImGuiKey.C), (SilkKey.V, ImGuiKey.V), (SilkKey.X, ImGuiKey.X), (SilkKey.Y, ImGuiKey.Y), (SilkKey.Z, ImGuiKey.Z)
    ];

    private static void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        ImGui.GetIO().AddInputCharacter(character);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _ = mouse;
        _wheelX += wheel.X;
        _wheelY += wheel.Y;
    }

    private sealed class SilkInputSnapshot : InputSnapshot
    {
        private readonly IMouse? _mouse;

        public SilkInputSnapshot(IMouse? mouse, IKeyboard? keyboard, float wheelDelta)
        {
            _mouse = mouse;
            MousePosition = mouse?.Position ?? System.Numerics.Vector2.Zero;
            WheelDelta = wheelDelta;
        }

        public IReadOnlyList<KeyEvent> KeyEvents { get; } = Array.Empty<KeyEvent>();
        public IReadOnlyList<MouseEvent> MouseEvents { get; } = Array.Empty<MouseEvent>();
        public IReadOnlyList<char> KeyCharPresses { get; } = Array.Empty<char>();
        public System.Numerics.Vector2 MousePosition { get; }
        public float WheelDelta { get; }
        public bool IsMouseDown(Veldrid.MouseButton button) => button switch
        {
            Veldrid.MouseButton.Left => _mouse?.IsButtonPressed(SilkMouseButton.Left) == true,
            Veldrid.MouseButton.Right => _mouse?.IsButtonPressed(SilkMouseButton.Right) == true,
            Veldrid.MouseButton.Middle => _mouse?.IsButtonPressed(SilkMouseButton.Middle) == true,
            _ => false
        };
    }
}
