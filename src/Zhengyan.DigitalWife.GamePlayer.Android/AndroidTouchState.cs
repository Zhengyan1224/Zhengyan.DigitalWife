using Android.Views;
using System.Numerics;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public enum AndroidTouchPhase
{
    Began,
    Moved,
    Stationary,
    Ended,
    Cancelled
}

public readonly record struct AndroidTouchPoint(
    int Id,
    Vector2 Position,
    Vector2 PixelPosition,
    Vector2 Delta,
    float Pressure,
    AndroidTouchPhase Phase,
    bool IsPrimary)
{
    public bool IsActive => Phase is AndroidTouchPhase.Began
        or AndroidTouchPhase.Moved
        or AndroidTouchPhase.Stationary;
}

public sealed class AndroidInputSnapshot
{
    public static AndroidInputSnapshot Empty { get; } = new([], null, 0, false, false);

    internal AndroidInputSnapshot(
        IReadOnlyList<AndroidTouchPoint> touches,
        AndroidTouchPoint? primaryTouch,
        int activeTouchCount,
        bool touchStarted,
        bool touchEnded)
    {
        Touches = touches;
        PrimaryTouch = primaryTouch;
        ActiveTouchCount = activeTouchCount;
        IsTouchStarted = touchStarted;
        IsTouchEnded = touchEnded;
    }

    public IReadOnlyList<AndroidTouchPoint> Touches { get; }

    public AndroidTouchPoint? PrimaryTouch { get; }

    public int ActiveTouchCount { get; }

    public bool HasTouch => ActiveTouchCount > 0;

    public bool IsTouchStarted { get; }

    public bool IsTouchEnded { get; }
}

internal sealed class AndroidTouchState
{
    private readonly object _sync = new();
    private readonly Dictionary<int, TouchState> _touches = [];

    public void Apply(MotionEvent motionEvent)
    {
        ArgumentNullException.ThrowIfNull(motionEvent);

        lock (_sync)
        {
            switch (motionEvent.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.PointerDown:
                    UpdatePoint(motionEvent, motionEvent.ActionIndex, AndroidTouchPhase.Began);
                    break;
                case MotionEventActions.Move:
                    for (int index = 0; index < motionEvent.PointerCount; index++)
                    {
                        UpdatePoint(motionEvent, index, AndroidTouchPhase.Moved);
                    }

                    break;
                case MotionEventActions.Up:
                case MotionEventActions.PointerUp:
                    UpdatePoint(motionEvent, motionEvent.ActionIndex, AndroidTouchPhase.Ended);
                    break;
                case MotionEventActions.Cancel:
                    foreach (TouchState state in _touches.Values)
                    {
                        state.Phase = AndroidTouchPhase.Cancelled;
                        state.Pressure = 0.0f;
                        state.EndedThisFrame = true;
                    }

                    break;
            }
        }
    }

    public AndroidInputSnapshot BeginFrame(int width, int height)
    {
        float safeWidth = Math.Max(width, 1);
        float safeHeight = Math.Max(height, 1);

        lock (_sync)
        {
            List<TouchState> ordered = _touches.Values
                .OrderBy(static state => state.Id)
                .ToList();
            int primaryId = ordered.FirstOrDefault(static state => state.IsActive)?.Id
                ?? ordered.FirstOrDefault()?.Id
                ?? -1;

            AndroidTouchPoint[] points = ordered
                .Select(state => state.ToPoint(safeWidth, safeHeight, state.Id == primaryId))
                .ToArray();
            AndroidTouchPoint? primary = points.FirstOrDefault(point => point.IsPrimary) is { } primaryPoint
                ? primaryPoint
                : null;

            AndroidInputSnapshot snapshot = new(
                points,
                primary,
                ordered.Count(static state => state.IsActive),
                ordered.Any(static state => state.StartedThisFrame),
                ordered.Any(static state => state.EndedThisFrame));

            foreach (int id in _touches
                .Where(static pair => !pair.Value.IsActive)
                .Select(static pair => pair.Key)
                .ToArray())
            {
                _touches.Remove(id);
            }

            foreach (TouchState state in _touches.Values)
            {
                state.Delta = Vector2.Zero;
                state.Phase = AndroidTouchPhase.Stationary;
                state.StartedThisFrame = false;
                state.EndedThisFrame = false;
            }

            return snapshot;
        }
    }

    private void UpdatePoint(MotionEvent motionEvent, int pointerIndex, AndroidTouchPhase phase)
    {
        if (pointerIndex < 0 || pointerIndex >= motionEvent.PointerCount)
        {
            return;
        }

        int id = motionEvent.GetPointerId(pointerIndex);
        Vector2 position = new(motionEvent.GetX(pointerIndex), motionEvent.GetY(pointerIndex));
        if (!_touches.TryGetValue(id, out TouchState? state))
        {
            state = new TouchState(id, position);
            _touches.Add(id, state);
        }

        state.Delta += position - state.PixelPosition;
        state.PixelPosition = position;
        state.Pressure = Math.Clamp(motionEvent.GetPressure(pointerIndex), 0.0f, 1.0f);
        state.Phase = phase;
        if (phase == AndroidTouchPhase.Began)
        {
            state.StartedThisFrame = true;
        }
        else if (phase is AndroidTouchPhase.Ended or AndroidTouchPhase.Cancelled)
        {
            state.Pressure = 0.0f;
            state.EndedThisFrame = true;
        }
    }

    private sealed class TouchState(int id, Vector2 position)
    {
        public int Id { get; } = id;

        public Vector2 PixelPosition { get; set; } = position;

        public Vector2 Delta { get; set; }

        public float Pressure { get; set; } = 1.0f;

        public AndroidTouchPhase Phase { get; set; } = AndroidTouchPhase.Began;

        public bool StartedThisFrame { get; set; } = true;

        public bool EndedThisFrame { get; set; }

        public bool IsActive => Phase is AndroidTouchPhase.Began
            or AndroidTouchPhase.Moved
            or AndroidTouchPhase.Stationary;

        public AndroidTouchPoint ToPoint(float width, float height, bool isPrimary)
        {
            return new AndroidTouchPoint(
                Id,
                new Vector2(PixelPosition.X / width, PixelPosition.Y / height),
                PixelPosition,
                Delta,
                Pressure,
                Phase,
                isPrimary);
        }
    }
}
