// Minimal native event data for exercising the production touch state on net10.0.
namespace Android.Views
{
    public enum MotionEventActions { Down, PointerDown, Move, Up, PointerUp, Cancel }

    public sealed class MotionEvent(MotionEventActions action, float x = 0, float y = 0, int id = 0)
    {
        public MotionEventActions ActionMasked => action;
        public int ActionIndex => 0;
        public int PointerCount => 1;
        public int GetPointerId(int index) => id;
        public float GetX(int index) => x;
        public float GetY(int index) => y;
        public float GetPressure(int index) => 1;
    }
}

namespace Zhengyan.DigitalWife.GamePlayer.Android
{
    public sealed record AndroidDeviceInputSnapshot
    {
        public static AndroidDeviceInputSnapshot Empty { get; } = new();
    }
}
