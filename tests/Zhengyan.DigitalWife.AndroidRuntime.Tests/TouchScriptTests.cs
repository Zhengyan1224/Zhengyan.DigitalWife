using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.Mmd.Game.Input;
using NativeEvent = Android.Views.MotionEvent;
using NativePhase = Android.Views.MotionEventActions;

internal static class TouchScriptTests
{
    internal static void TestAndroidInput()
    {
        AndroidTouchState state = new();
        AndroidScriptInputApi input = new AndroidScriptInput(AndroidInputSnapshot.Empty);
        Check(input.IsTouchAvailable && !input.HasTouch && input.PrimaryTouch is null, "Empty Android touch capability is incorrect.");
        state.Apply(new NativeEvent(NativePhase.Down, 100, 50, 7));
        state.Apply(new NativeEvent(NativePhase.PointerDown, 1, (7, 100, 50, .5f), (12, 300, 150, .75f)));
        AndroidScriptInput scriptInput = (AndroidScriptInput)input;
        scriptInput.Update(state.BeginFrame(1000, 500));
        IReadOnlyList<TouchPoint> saved = input.Touches;
        Check(input.TouchCount == 2 && input.ActiveTouchCount == 2 && input.IsTouchStarted && input.IsTouchDown,
            "Native multi-touch did not reach the typed script API.");
        Check(input.PrimaryTouch?.Id == 7 && input.TryGetTouch(12, out TouchPoint second)
            && second.X == 300 && second.Y == 150 && second.Pressure == .75f && second.Kind == TouchInputKind.Touch,
            "Stable pointer ID, pixel coordinates or pressure were lost.");
        Check(!input.TryGetTouch(99, out _), "Missing touch lookup succeeded.");
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.Touches.All(t => t.Phase == TouchPhase.Stationary) && !input.IsTouchStarted, "Press lasted more than one frame.");
        state.Apply(new NativeEvent(NativePhase.Move, 0, (12, 310, 160, .75f), (7, 120, 60, .5f)));
        state.Apply(new NativeEvent(NativePhase.Move, 0, (7, 125, 65, .5f), (12, 315, 165, .75f)));
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.TryGetTouch(7, out TouchPoint first) && first.DeltaX == 25 && first.DeltaY == 15
            && first.Phase == TouchPhase.Moved, "Pointer index changes or multiple moves broke the per-frame delta.");
        Check(saved[0].X == 100 && saved[0].Phase == TouchPhase.Started, "A saved script snapshot was mutated by later input.");
        state.Apply(new NativeEvent(NativePhase.PointerUp, 1, (12, 315, 165, .75f), (7, 125, 65, .5f)));
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.HasTouch && input.TouchCount == 2 && input.ActiveTouchCount == 1 && input.PrimaryTouch?.Id == 12
            && input.IsTouchEnded && input.TryGetTouch(7, out first) && first.Phase == TouchPhase.Ended && first.Pressure == 0,
            "Release, active count or primary-finger fallback is incorrect.");
        state.Apply(new NativeEvent(NativePhase.Cancel));
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.HasTouch && !input.IsTouchDown && input.Touches.Single().Phase == TouchPhase.Cancelled,
            "System cancellation did not reach scripts.");
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(!input.HasTouch && input.TouchCount == 0 && input.TouchTransitions.Count == 0, "Ended points survived another frame.");
        state.Apply(new NativeEvent(NativePhase.Down, 50, 30, 2));
        state.Apply(new NativeEvent(NativePhase.Up, 80, 30, 2));
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.IsTouchStarted && input.IsTouchEnded && !input.IsTouchDown
            && input.TouchTransitions.Select(t => t.Phase).SequenceEqual([TouchPhase.Started, TouchPhase.Ended])
            && input.TouchTransitions[0].X == 50 && input.TouchTransitions[1].X == 80,
            "A tap completed between frames lost its ordered press/release coordinates.");
        state.BeginFrame(1000, 500);
        state.Apply(new NativeEvent(NativePhase.Down, 40, 50) { ToolType = Android.Views.MotionEventToolType.Mouse });
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(!input.HasTouch, "An external mouse click was reported as touchscreen input.");
        state.Apply(new NativeEvent(NativePhase.Down, 40, 50) { ToolType = Android.Views.MotionEventToolType.Stylus });
        scriptInput.Update(state.BeginFrame(1000, 500));
        Check(input.PrimaryTouch?.Kind == TouchInputKind.Pen, "Android stylus input lost its device kind.");
    }

    internal static void TestDesktopTransitions()
    {
        // Exercise the real desktop touch accumulator without creating a GUI window.
        InputManager input = (InputManager)RuntimeHelpers.GetUninitializedObject(typeof(InputManager));
        TouchSource source = new();
        Set("_touchInputSource", source);
        Set("_touches", new List<TouchPoint>());
        Set("_touchTransitions", new List<TouchPoint>());
        FieldInfo active = typeof(InputManager).GetField("_activeTouches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        active.SetValue(input, Activator.CreateInstance(active.FieldType));
        MethodInfo capture = typeof(InputManager).GetMethod("CaptureTouchState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        source.Events = [new(4, 10, 20, TouchPhase.Started, TouchInputKind.Touch, 1), new(4, 15, 20, TouchPhase.Ended, TouchInputKind.Touch, 0)];
        capture.Invoke(input, null);
        Check(input.IsTouchStarted && input.IsTouchEnded && input.HasTouch && !input.IsTouchDown
            && input.TouchTransitions.Select(t => t.Phase).SequenceEqual([TouchPhase.Started, TouchPhase.Ended]),
            "Desktop quick tap differs from Android script semantics.");
        capture.Invoke(input, null);
        Check(!input.HasTouch && input.TouchTransitions.Count == 0, "Desktop transitions were replayed.");
        source.Events = [new(4, 10, 20, TouchPhase.Started, TouchInputKind.Touch, 1)];
        capture.Invoke(input, null);
        Set("_cancelTouches", true);
        capture.Invoke(input, null);
        Check(input.TouchTransitions.Single().Phase == TouchPhase.Cancelled && input.IsTouchEnded,
            "Desktop focus loss did not produce a cancel transition.");
        void Set(string field, object value) => typeof(InputManager).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(input, value);
    }

    private sealed class TouchSource : ITouchInputSource
    {
        internal TouchInputEvent[] Events = [];
        public bool IsAvailable => true;
        public IReadOnlyList<TouchInputEvent> ConsumeEvents() { var events = Events; Events = []; return events; }
        public void Dispose() { }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
