using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGLES.Extensions.ImGui;
using Veldrid;

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
    private readonly VeldridImGuiRenderer _controller;
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;
    private float _wheelX;
    private float _wheelY;
    private readonly List<char> _characters = [];

    public VulkanImGuiBackendController(Game game, VulkanRenderer renderer, Action? configureFonts)
    {
        _game = game;
        _controller = new VeldridImGuiRenderer(renderer, configureFonts);

        _keyboard = game.Input.Context.Keyboards.FirstOrDefault();
        _mouse = game.Input.Context.Mice.FirstOrDefault();
        if (_keyboard is not null) _keyboard.KeyChar += OnKeyChar;
        if (_mouse is not null) _mouse.Scroll += OnScroll;
    }

    public void Update(float deltaSeconds)
    {
        _controller.Update(
            Math.Max(deltaSeconds, 1f / 1000f),
            Math.Max(_game.GraphicsDevice.BackBufferSize.X, 1),
            Math.Max(_game.GraphicsDevice.BackBufferSize.Y, 1),
            _mouse,
            _keyboard,
            _wheelX,
            _wheelY,
            _characters);
        _characters.Clear();
        _wheelX = _wheelY = 0;
    }

    public void Render() => _controller.Render();

    public nint GetTextureBinding(RuntimeTextureHandle texture)
    {
        return texture.NativeResource is TextureView view
            ? _controller.GetOrCreateTextureBinding(view)
            : 0;
    }

    public void Dispose()
    {
        if (_keyboard is not null) _keyboard.KeyChar -= OnKeyChar;
        if (_mouse is not null) _mouse.Scroll -= OnScroll;
        _controller.Dispose();
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        _characters.Add(character);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _ = mouse;
        _wheelX += wheel.X;
        _wheelY += wheel.Y;
    }

}
