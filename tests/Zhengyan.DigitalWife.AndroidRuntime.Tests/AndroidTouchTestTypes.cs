// Minimal native event data for exercising the production touch state on net10.0.
namespace Android.Views
{
    public enum MotionEventActions { Down, PointerDown, Move, Up, PointerUp, Cancel }
    public enum MotionEventToolType { Unknown, Finger, Stylus, Mouse, Eraser }

    public enum Keycode { Unknown, ButtonA, ButtonB, ButtonX, ButtonY, ButtonL1, ButtonR1, ButtonSelect, ButtonStart, ButtonMode, ButtonThumbl, ButtonThumbr, DpadUp, DpadDown, DpadLeft, DpadRight }

    public sealed class MotionEvent
    {
        private readonly (int Id, float X, float Y, float Pressure)[] _points;
        public MotionEvent(MotionEventActions action, float x = 0, float y = 0, int id = 0)
            : this(action, 0, (id, x, y, 1)) { }
        public MotionEvent(MotionEventActions action, int actionIndex, params (int Id, float X, float Y, float Pressure)[] points)
        { ActionMasked = action; ActionIndex = actionIndex; _points = points; }
        public MotionEventActions ActionMasked { get; }
        public int ActionIndex { get; }
        public int PointerCount => _points.Length;
        public MotionEventToolType ToolType { get; init; } = MotionEventToolType.Finger;
        public MotionEventToolType GetToolType(int index) => ToolType;
        public int GetPointerId(int index) => _points[index].Id;
        public float GetX(int index) => _points[index].X;
        public float GetY(int index) => _points[index].Y;
        public float GetPressure(int index) => _points[index].Pressure;
    }
}

namespace Zhengyan.DigitalWife.GamePlayer.Android
{
    public sealed record AndroidDeviceInputSnapshot
    {
        public static AndroidDeviceInputSnapshot Empty { get; } = new();
        public System.Numerics.Vector2 MousePosition => default;
        public System.Numerics.Vector2 MouseDelta => default;
        public System.Numerics.Vector2 ScrollDelta => default;
        public IReadOnlySet<int> PressedMouseButtons { get; } = new HashSet<int>();
        public IReadOnlySet<int> ReleasedMouseButtons { get; } = new HashSet<int>();
        public AndroidGamepadSnapshot Gamepad { get; } = new();
        public bool IsKeyDown(global::Android.Views.Keycode key) => false;
        public bool IsKeyPressed(global::Android.Views.Keycode key) => false;
        public bool IsKeyReleased(global::Android.Views.Keycode key) => false;
        public bool IsMouseButtonDown(int button) => false;
    }
    public sealed class AndroidGamepadSnapshot
    {
        public bool Connected => false;
        public string Name => "";
        public System.Numerics.Vector2 LeftStick => default;
        public System.Numerics.Vector2 RightStick => default;
        public float LeftTrigger => 0;
        public float RightTrigger => 0;
        public bool IsButtonDown(global::Android.Views.Keycode key) => false;
    }
}
