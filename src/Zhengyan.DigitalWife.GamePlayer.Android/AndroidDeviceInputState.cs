using Android.Views;
using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public readonly record struct AndroidGamepadSnapshot(
    bool Connected,
    string Name,
    Vector2 LeftStick,
    Vector2 RightStick,
    float LeftTrigger,
    float RightTrigger,
    IReadOnlySet<Keycode> Buttons)
{
    public static AndroidGamepadSnapshot Empty { get; } = new(false, string.Empty, Vector2.Zero, Vector2.Zero, 0, 0, new HashSet<Keycode>());
    public bool IsButtonDown(Keycode key) => Buttons.Contains(key);
}

public readonly record struct AndroidDeviceInputSnapshot(
    IReadOnlySet<Keycode> Keys,
    IReadOnlySet<Keycode> PressedKeys,
    IReadOnlySet<Keycode> ReleasedKeys,
    Vector2 MousePosition,
    Vector2 MouseDelta,
    Vector2 ScrollDelta,
    IReadOnlySet<int> MouseButtons,
    IReadOnlySet<int> PressedMouseButtons,
    IReadOnlySet<int> ReleasedMouseButtons,
    AndroidGamepadSnapshot Gamepad)
{
    public static AndroidDeviceInputSnapshot Empty { get; } = new(new HashSet<Keycode>(), new HashSet<Keycode>(), new HashSet<Keycode>(), Vector2.Zero, Vector2.Zero, Vector2.Zero, new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), AndroidGamepadSnapshot.Empty);
    public bool IsKeyDown(Keycode key) => Keys.Contains(key);
    public bool IsKeyPressed(Keycode key) => PressedKeys.Contains(key);
    public bool IsKeyReleased(Keycode key) => ReleasedKeys.Contains(key);
    public bool IsMouseButtonDown(int button) => MouseButtons.Contains(button);
}

internal sealed class AndroidDeviceInputState
{
    private readonly object _sync = new();
    private readonly HashSet<Keycode> _keys = [];
    private readonly HashSet<Keycode> _pressedKeys = [];
    private readonly HashSet<Keycode> _releasedKeys = [];
    private readonly HashSet<int> _mouseButtons = [];
    private readonly HashSet<int> _pressedMouseButtons = [];
    private readonly HashSet<int> _releasedMouseButtons = [];
    private readonly HashSet<Keycode> _gamepadButtons = [];
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    private Vector2 _leftStick;
    private Vector2 _rightStick;
    private float _leftTrigger;
    private float _rightTrigger;
    private string _gamepadName = string.Empty;
    private bool _gamepadConnected;

    public void ApplyKey(KeyEvent? keyEvent, bool down)
    {
        if (keyEvent is null) return;
        lock (_sync)
        {
            Keycode key = keyEvent.KeyCode;
            bool gamepad = IsGamepadSource((int)keyEvent.Source);
            if (gamepad)
            {
                _gamepadConnected = true;
                _gamepadName = keyEvent.Device?.Name ?? _gamepadName;
                if (IsGamepadButton(key)) { if (down) _gamepadButtons.Add(key); else _gamepadButtons.Remove(key); }
            }
            if (down) { if (_keys.Add(key)) _pressedKeys.Add(key); } else { if (_keys.Remove(key)) _releasedKeys.Add(key); }
        }
    }

    public void ApplyMotion(MotionEvent? motionEvent)
    {
        if (motionEvent is null) return;
        int source = (int)motionEvent.Source;
        lock (_sync)
        {
            if ((source & (int)InputSourceType.Mouse) != 0)
            {
                Vector2 position = new(motionEvent.GetX(), motionEvent.GetY());
                _mouseDelta += position - _mousePosition;
                _mousePosition = position;
                if (motionEvent.ActionMasked == MotionEventActions.Scroll)
                    _scrollDelta += new Vector2(motionEvent.GetAxisValue(Axis.Hscroll), motionEvent.GetAxisValue(Axis.Vscroll));
            }
            if (IsGamepadSource(source))
            {
                _gamepadConnected = true;
                _gamepadName = motionEvent.Device?.Name ?? _gamepadName;
                _leftStick = new Vector2(DeadZone(motionEvent.GetAxisValue(Axis.X)), DeadZone(motionEvent.GetAxisValue(Axis.Y)));
                _rightStick = new Vector2(DeadZone(motionEvent.GetAxisValue(Axis.Z)), DeadZone(motionEvent.GetAxisValue(Axis.Rz)));
                _leftTrigger = NormalizeTrigger(motionEvent.GetAxisValue(Axis.Ltrigger));
                _rightTrigger = NormalizeTrigger(motionEvent.GetAxisValue(Axis.Rtrigger));
            }
        }
    }

    public void SetMouseButton(int button, bool down)
    {
        lock (_sync)
        {
            if (down) { _mouseButtons.Add(button); _pressedMouseButtons.Add(button); }
            else { _mouseButtons.Remove(button); _releasedMouseButtons.Add(button); }
        }
    }

    public AndroidDeviceInputSnapshot BeginFrame()
    {
        lock (_sync)
        {
            AndroidDeviceInputSnapshot result = new(new HashSet<Keycode>(_keys), new HashSet<Keycode>(_pressedKeys), new HashSet<Keycode>(_releasedKeys), _mousePosition, _mouseDelta, _scrollDelta,
                new HashSet<int>(_mouseButtons), new HashSet<int>(_pressedMouseButtons), new HashSet<int>(_releasedMouseButtons),
                new AndroidGamepadSnapshot(_gamepadConnected, _gamepadName, _leftStick, _rightStick, _leftTrigger, _rightTrigger, new HashSet<Keycode>(_gamepadButtons)));
            _mouseDelta = Vector2.Zero; _scrollDelta = Vector2.Zero; _pressedMouseButtons.Clear(); _releasedMouseButtons.Clear(); _pressedKeys.Clear(); _releasedKeys.Clear();
            return result;
        }
    }

    private static bool IsGamepadSource(int source) => (source & ((int)InputSourceType.Gamepad | (int)InputSourceType.Joystick | (int)InputSourceType.Dpad)) != 0;
    private static bool IsGamepadButton(Keycode key) => key is Keycode.ButtonA or Keycode.ButtonB or Keycode.ButtonX or Keycode.ButtonY or Keycode.ButtonL1 or Keycode.ButtonR1 or Keycode.ButtonL2 or Keycode.ButtonR2 or Keycode.ButtonStart or Keycode.ButtonSelect or Keycode.ButtonMode or Keycode.ButtonThumbl or Keycode.ButtonThumbr or Keycode.DpadUp or Keycode.DpadDown or Keycode.DpadLeft or Keycode.DpadRight;
    private static float DeadZone(float value) => MathF.Abs(value) < 0.12f ? 0.0f : Math.Clamp(value, -1.0f, 1.0f);
    private static float NormalizeTrigger(float value) => Math.Clamp(value < 0 ? (value + 1.0f) * 0.5f : value, 0.0f, 1.0f);
}
