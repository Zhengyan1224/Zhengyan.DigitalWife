using System.Numerics;
using Silk.NET.Input;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

public sealed class InputManager : IDisposable
{
    private readonly IInputContext _inputContext;
    private readonly IMouse _mouse;
    private readonly IKeyboard _keyboard;
    private Vector2 _pendingScrollDelta;
    private Vector2 _lastMousePosition;

    public InputManager(IInputContext inputContext)
    {
        _inputContext = inputContext;
        _mouse = inputContext.Mice.Count > 0
            ? inputContext.Mice[0]
            : throw new InvalidOperationException("No mouse device is available.");
        _keyboard = inputContext.Keyboards.Count > 0
            ? inputContext.Keyboards[0]
            : throw new InvalidOperationException("No keyboard device is available.");

        _mouse.Scroll += (_, wheel) => _pendingScrollDelta += new Vector2(wheel.X, wheel.Y);
    }

    public Vector2 MousePosition { get; private set; }

    public Vector2 MouseDelta { get; private set; }

    public Vector2 ScrollDelta { get; private set; }

    public IInputContext Context => _inputContext;

    public bool IsAltDown => IsKeyDown(Key.AltLeft) || IsKeyDown(Key.AltRight);

    public bool IsControlDown => IsKeyDown(Key.ControlLeft) || IsKeyDown(Key.ControlRight);

    public bool IsKeyDown(Key key) => _keyboard.IsKeyPressed(key);

    public bool IsMouseButtonDown(MouseButton button) => _mouse.IsButtonPressed(button);

    internal void BeginFrame()
    {
        MousePosition = new Vector2(_mouse.Position.X, _mouse.Position.Y);
        MouseDelta = MousePosition - _lastMousePosition;
        _lastMousePosition = MousePosition;

        ScrollDelta = _pendingScrollDelta;
        _pendingScrollDelta = Vector2.Zero;
    }

    public void Dispose()
    {
        _inputContext.Dispose();
        GC.SuppressFinalize(this);
    }
}

