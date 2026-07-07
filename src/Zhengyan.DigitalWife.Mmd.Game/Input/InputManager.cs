using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

public sealed class InputManager : IDisposable
{
    private readonly IInputContext _inputContext;
    private readonly IMouse _mouse;
    private readonly IKeyboard _keyboard;
    private readonly HashSet<ButtonName> _gamepadButtonsDown = [];
    private readonly Dictionary<int, TouchState> _activeTouches = [];
    private readonly List<TouchPoint> _touches = [];
    private readonly ITouchInputSource _touchInputSource;
    private Vector2 _pendingScrollDelta;
    private Vector2 _pendingMouseDelta;
    private Vector2 _lastMousePosition;
    private Vector2 _eventMousePosition;
    private IGamepad? _gamepad;
    private bool _hasPendingMouseDelta;
    private bool _hasMouseMoveEvent;
    private bool _cancelTouches;

    public InputManager(IInputContext inputContext, IWindow window)
    {
        _inputContext = inputContext;
        _mouse = inputContext.Mice.Count > 0
            ? inputContext.Mice[0]
            : throw new InvalidOperationException("No mouse device is available.");
        _keyboard = inputContext.Keyboards.Count > 0
            ? inputContext.Keyboards[0]
            : throw new InvalidOperationException("No keyboard device is available.");

        Vector2 initialMousePosition = new(_mouse.Position.X, _mouse.Position.Y);
        MousePosition = initialMousePosition;
        _lastMousePosition = initialMousePosition;
        _eventMousePosition = initialMousePosition;
        _hasMouseMoveEvent = true;

        _mouse.MouseMove += (_, position) =>
        {
            Vector2 current = new(position.X, position.Y);
            if (_hasMouseMoveEvent)
            {
                _pendingMouseDelta += current - _eventMousePosition;
                _hasPendingMouseDelta = true;
            }

            _eventMousePosition = current;
            _hasMouseMoveEvent = true;
        };
        _mouse.Scroll += (_, wheel) => _pendingScrollDelta += new Vector2(wheel.X, wheel.Y);
        _touchInputSource = TouchInputSourceFactory.Create(window);
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

    public IReadOnlyList<TouchPoint> Touches => _touches;

    public TouchPoint? PrimaryTouch { get; private set; }

    public bool IsTouchAvailable => _touchInputSource.IsAvailable;

    public bool HasTouch => _touches.Count > 0;

    public int TouchCount => _touches.Count;

    public int ActiveTouchCount { get; private set; }

    public bool IsTouchDown => ActiveTouchCount > 0;

    public bool IsTouchStarted { get; private set; }

    public bool IsTouchEnded { get; private set; }

    public bool IsCursorVisible => GetCursorMode() is not CursorMode.Hidden and not CursorMode.Disabled and not CursorMode.Raw;

    public bool IsCursorLocked => GetCursorMode() is CursorMode.Disabled or CursorMode.Raw;

    public bool IsRawMouseInput => GetCursorMode() is CursorMode.Raw;

    public string CursorModeName => ToCursorModeName(GetCursorMode());

    public bool IsKeyDown(Key key) => _keyboard.IsKeyPressed(key);

    public bool IsMouseButtonDown(MouseButton button) => _mouse.IsButtonPressed(button);

    public bool IsGamepadButtonDown(ButtonName button) => _gamepadButtonsDown.Contains(button);

    public bool TryGetTouch(int id, out TouchPoint touch)
    {
        foreach (TouchPoint candidate in _touches)
        {
            if (candidate.Id == id)
            {
                touch = candidate;
                return true;
            }
        }

        touch = default;
        return false;
    }

    public void CancelTouches()
    {
        _cancelTouches = true;
    }

    public bool TrySetCursorVisible(bool visible)
    {
        CursorMode mode = visible ? Silk.NET.Input.CursorMode.Normal : Silk.NET.Input.CursorMode.Hidden;
        return TrySetCursorMode(mode);
    }

    public bool TrySetCursorLocked(bool locked, bool rawInput = false)
    {
        if (!locked)
        {
            return TrySetCursorMode(Silk.NET.Input.CursorMode.Normal);
        }

        CursorMode mode = rawInput ? Silk.NET.Input.CursorMode.Raw : Silk.NET.Input.CursorMode.Disabled;
        if (TrySetCursorMode(mode))
        {
            return true;
        }

        return rawInput && TrySetCursorMode(Silk.NET.Input.CursorMode.Disabled);
    }

    private bool TrySetCursorMode(CursorMode mode)
    {
        try
        {
            ICursor cursor = _mouse.Cursor;
            if (cursor.CursorMode == mode)
            {
                return true;
            }

            if (!cursor.IsSupported(mode))
            {
                return false;
            }

            cursor.CursorMode = mode;
            ResetMouseTracking();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void BeginFrame()
    {
        MousePosition = new Vector2(_mouse.Position.X, _mouse.Position.Y);
        Vector2 positionDelta = MousePosition - _lastMousePosition;
        MouseDelta = _hasPendingMouseDelta ? _pendingMouseDelta : positionDelta;
        _lastMousePosition = MousePosition;
        _pendingMouseDelta = Vector2.Zero;
        _hasPendingMouseDelta = false;

        ScrollDelta = _pendingScrollDelta;
        _pendingScrollDelta = Vector2.Zero;

        CaptureGamepadState();
        CaptureTouchState();
    }

    public void Dispose()
    {
        _touchInputSource.Dispose();
        _inputContext.Dispose();
        GC.SuppressFinalize(this);
    }

    private CursorMode GetCursorMode()
    {
        try
        {
            return _mouse.Cursor.CursorMode;
        }
        catch
        {
            return CursorMode.Normal;
        }
    }

    private void ResetMouseTracking()
    {
        Vector2 current = new(_mouse.Position.X, _mouse.Position.Y);
        MousePosition = current;
        MouseDelta = Vector2.Zero;
        _lastMousePosition = current;
        _eventMousePosition = current;
        _pendingMouseDelta = Vector2.Zero;
        _hasPendingMouseDelta = false;
        _hasMouseMoveEvent = true;
    }

    private static string ToCursorModeName(CursorMode mode)
    {
        return mode switch
        {
            CursorMode.Normal => "normal",
            CursorMode.Hidden => "hidden",
            CursorMode.Disabled => "disabled",
            CursorMode.Raw => "raw",
            _ => mode.ToString().ToLowerInvariant()
        };
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

    private void CaptureTouchState()
    {
        foreach (TouchState state in _activeTouches.Values)
        {
            state.DeltaX = 0.0f;
            state.DeltaY = 0.0f;
            state.Phase = TouchPhase.Stationary;
            state.RemoveAfterFrame = false;
        }

        foreach (TouchInputEvent touchEvent in _touchInputSource.ConsumeEvents())
        {
            ApplyTouchEvent(touchEvent);
        }

        if (_cancelTouches)
        {
            _cancelTouches = false;
            foreach (TouchState state in _activeTouches.Values)
            {
                state.Phase = TouchPhase.Cancelled;
                state.Pressure = 0.0f;
                state.RemoveAfterFrame = true;
            }
        }

        _touches.Clear();
        foreach (TouchState state in _activeTouches.Values
            .OrderBy(static state => state.IsActive ? 0 : 1)
            .ThenBy(static state => state.Id))
        {
            _touches.Add(state.ToTouchPoint());
        }

        ActiveTouchCount = _touches.Count(static touch => touch.IsActive);
        IsTouchStarted = _touches.Any(static touch => touch.Phase == TouchPhase.Started);
        IsTouchEnded = _touches.Any(static touch => touch.IsEnded);
        PrimaryTouch = _touches.Count == 0 ? null : _touches[0];

        foreach (int id in _activeTouches
            .Where(static item => item.Value.RemoveAfterFrame)
            .Select(static item => item.Key)
            .ToArray())
        {
            _activeTouches.Remove(id);
        }
    }

    private void ApplyTouchEvent(TouchInputEvent touchEvent)
    {
        if (!_activeTouches.TryGetValue(touchEvent.Id, out TouchState? state))
        {
            state = new TouchState(touchEvent.Id, touchEvent.X, touchEvent.Y, touchEvent.Kind);
            _activeTouches[touchEvent.Id] = state;
        }

        float oldX = state.X;
        float oldY = state.Y;
        state.X = touchEvent.X;
        state.Y = touchEvent.Y;
        state.DeltaX += touchEvent.X - oldX;
        state.DeltaY += touchEvent.Y - oldY;
        state.Kind = touchEvent.Kind;
        state.Pressure = Math.Clamp(touchEvent.Pressure, 0.0f, 1.0f);

        switch (touchEvent.Phase)
        {
            case TouchPhase.Started:
                state.Phase = TouchPhase.Started;
                state.RemoveAfterFrame = false;
                break;
            case TouchPhase.Moved:
                if (state.Phase != TouchPhase.Started)
                {
                    state.Phase = MathF.Abs(state.DeltaX) > 0.001f || MathF.Abs(state.DeltaY) > 0.001f
                        ? TouchPhase.Moved
                        : TouchPhase.Stationary;
                }

                state.RemoveAfterFrame = false;
                break;
            case TouchPhase.Ended:
                state.Phase = TouchPhase.Ended;
                state.Pressure = 0.0f;
                state.RemoveAfterFrame = true;
                break;
            case TouchPhase.Cancelled:
                state.Phase = TouchPhase.Cancelled;
                state.Pressure = 0.0f;
                state.RemoveAfterFrame = true;
                break;
            case TouchPhase.Stationary:
            default:
                break;
        }
    }

    private sealed class TouchState(int id, float x, float y, TouchInputKind kind)
    {
        public int Id { get; } = id;

        public float X { get; set; } = x;

        public float Y { get; set; } = y;

        public float DeltaX { get; set; }

        public float DeltaY { get; set; }

        public TouchPhase Phase { get; set; } = TouchPhase.Started;

        public TouchInputKind Kind { get; set; } = kind;

        public float Pressure { get; set; } = 1.0f;

        public bool RemoveAfterFrame { get; set; }

        public bool IsActive => Phase is TouchPhase.Started or TouchPhase.Moved or TouchPhase.Stationary;

        public TouchPoint ToTouchPoint()
        {
            return new TouchPoint(Id, X, Y, DeltaX, DeltaY, Phase, Kind, Pressure);
        }
    }
}
