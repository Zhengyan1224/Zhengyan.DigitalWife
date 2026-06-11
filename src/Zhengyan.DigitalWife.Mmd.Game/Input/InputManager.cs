using System.Numerics;
using Silk.NET.Input;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

public sealed class InputManager : IDisposable
{
    private readonly IInputContext _inputContext;
    private readonly IMouse _mouse;
    private readonly IKeyboard _keyboard;
    private readonly HashSet<ButtonName> _gamepadButtonsDown = [];
    private Vector2 _pendingScrollDelta;
    private Vector2 _lastMousePosition;
    private IGamepad? _gamepad;

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

    public bool HasGamepad => _gamepad is not null;

    public string GamepadName => _gamepad?.Name ?? string.Empty;

    public int GamepadIndex => _gamepad?.Index ?? -1;

    public Vector2 LeftThumbstick { get; private set; }

    public Vector2 RightThumbstick { get; private set; }

    public float LeftTrigger { get; private set; }

    public float RightTrigger { get; private set; }

    public IReadOnlyCollection<ButtonName> GamepadButtonsDown => _gamepadButtonsDown;

    public bool IsKeyDown(Key key) => _keyboard.IsKeyPressed(key);

    public bool IsMouseButtonDown(MouseButton button) => _mouse.IsButtonPressed(button);

    public bool IsGamepadButtonDown(ButtonName button) => _gamepadButtonsDown.Contains(button);

    internal void BeginFrame()
    {
        MousePosition = new Vector2(_mouse.Position.X, _mouse.Position.Y);
        MouseDelta = MousePosition - _lastMousePosition;
        _lastMousePosition = MousePosition;

        ScrollDelta = _pendingScrollDelta;
        _pendingScrollDelta = Vector2.Zero;

        CaptureGamepadState();
    }

    public void Dispose()
    {
        _inputContext.Dispose();
        GC.SuppressFinalize(this);
    }

    private void CaptureGamepadState()
    {
        _gamepad = _inputContext.Gamepads.FirstOrDefault(gamepad => gamepad.IsConnected);
        _gamepadButtonsDown.Clear();
        LeftThumbstick = Vector2.Zero;
        RightThumbstick = Vector2.Zero;
        LeftTrigger = 0.0f;
        RightTrigger = 0.0f;

        if (_gamepad is null)
        {
            return;
        }

        foreach (Button button in _gamepad.Buttons)
        {
            if (button.Pressed)
            {
                _gamepadButtonsDown.Add(button.Name);
            }
        }

        if (_gamepad.Thumbsticks.Count > 0)
        {
            Thumbstick left = _gamepad.Thumbsticks[0];
            LeftThumbstick = new Vector2(left.X, left.Y);
        }

        if (_gamepad.Thumbsticks.Count > 1)
        {
            Thumbstick right = _gamepad.Thumbsticks[1];
            RightThumbstick = new Vector2(right.X, right.Y);
        }

        if (_gamepad.Triggers.Count > 0)
        {
            LeftTrigger = _gamepad.Triggers[0].Position;
        }

        if (_gamepad.Triggers.Count > 1)
        {
            RightTrigger = _gamepad.Triggers[1].Position;
        }
    }
}

