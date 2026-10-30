using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Input;

internal static class PointerClickTests
{
    internal static void TestGestures()
    {
        PointerClickGesture click = new();
        TouchPoint down = Touch(1, 200, 200, TouchPhase.Started);
        TouchPoint up = down with { Phase = TouchPhase.Ended };
        Check(!Step([down], [down]), "Touch-down moved the actor before tap recognition.");
        Check(Step([up], [up]) && !Step([], []), "Tap was lost or repeated.");
        Check(Step([up], [down, up]), "A complete tap between frames was lost.");

        Step([down], [down]);
        Step([down with { X = 250, Phase = TouchPhase.Moved }], []);
        Check(!Step([up], [up]), "Dragging out and back was classified as a tap.");
        Step([down], [down]);
        Step([down with { Phase = TouchPhase.Stationary }], [], .4);
        Check(!Step([up], [up]), "Long press was classified as a tap.");
        Step([down], [down]);
        TouchPoint cancel = up with { Phase = TouchPhase.Cancelled };
        Check(!Step([cancel], [cancel]), "Cancellation was classified as a tap.");

        TouchPoint second = down with { Id = 2 };
        Step([down], [down]);
        Step([down, second with { Phase = TouchPhase.Ended }], [second, second with { Phase = TouchPhase.Ended }]);
        Check(!Step([up], [up]), "Pinch followed by the remaining finger's release became a tap.");
        Check(Step([up], [down, up]), "Multi-touch suppressed a later independent tap.");

        click.Reset();
        bool Blocked(Vector2 p) => p.X < 100;
        TouchPoint guiDown = down with { X = 20 };
        Step([guiDown], [guiDown], blocked: Blocked, threshold: 500);
        Check(!Step([up], [up], blocked: Blocked, threshold: 500), "GUI capture was lost after leaving its bounds.");
        Step([down], [down], blocked: Blocked, threshold: 500);
        Check(!Step([guiDown with { Phase = TouchPhase.Ended }], [guiDown with { Phase = TouchPhase.Ended }],
            blocked: Blocked, threshold: 500), "Release over GUI clicked the scene.");

        click.Reset();
        Check(!click.Update(true, new(200, 200), [], [], .02, out _), "Mouse-down triggered a click.");
        Check(click.Update(false, new(200, 200), [], [], .02, out Vector2 point) && point == new Vector2(200, 200),
            "Mouse release did not produce the shared screen coordinate.");
        click.Update(true, new(200, 200), [], [], .02, out _);
        Check(!click.Update(false, new(250, 200), [], [], .02, out _), "Mouse drag became a click.");
        click.Update(true, new(200, 200), [down], [down], .02, out _);
        Check(click.Update(true, new(200, 200), [up], [up], .02, out _)
            && !click.Update(false, new(200, 200), [], [], .02, out _), "Synthetic mouse input repeated a touchscreen tap.");

        float slop = new AndroidScriptInput(AndroidInputSnapshot.Empty, displayDensity: 3).TouchSlopPixels;
        Check(slop == 36, "Android movement tolerance ignores display density.");
        click.Reset();
        Check(Step([up with { X = 220 }], [down, up with { X = 220 }], threshold: slop), "Density-scaled tap was rejected.");
        click.Reset();
        Check(!Step([up with { X = 220 }], [down, up with { X = 220 }]), "Movement tolerance was not applied.");

        bool Step(TouchPoint[] touches, TouchPoint[] transitions, double seconds = .02,
            Func<Vector2, bool>? blocked = null, float threshold = 12)
            => click.Update(false, Vector2.Zero, touches, transitions, seconds, out _, threshold, blocked);
    }

    internal static void TestCameraRays()
    {
        using RuntimeScene core = new("main", new(), p => p);
        Vector2 size = new(2000, 1000);
        GameWindowSettings window = new() { Width = 1000, Height = 500 };
        AndroidScriptScene scene = new(core, "", _ => { }, () => 60, null!, () => size, window);
        core.MainCamera.Position = new(0, 0, 5);
        core.MainCamera.Target = Vector3.Zero;
        core.MainCamera.FieldOfView = 90;
        Check(Near(scene.Camera.ScreenPointToRay(1000, 500).Direction, -Vector3.UnitZ), "Full-screen ray used configured viewport dimensions.");
        Check(Near(scene.Camera.ScreenPointToRay(2000, 0).Direction, Vector3.Normalize(new(2, 1, -1))), "Ray aspect ratio is incorrect.");
        AndroidScriptInput mouse = new(AndroidInputSnapshot.Empty.WithDeviceInput(
            AndroidDeviceInputSnapshot.Empty with { MousePosition = new(1000, 500) }));
        Check(scene.Camera.MousePointToRay(mouse) == scene.Camera.TouchPointToRay(Touch(1, 1000, 500, TouchPhase.Started)),
            "Mouse and touch helpers produced different rays.");
        size = new(1000, 2000);
        Check(Near(scene.Camera.ScreenPointToRay(500, 1000).Direction, -Vector3.UnitZ), "Ray ignored a surface resize.");
        core.MainCamera.Definition.Viewport = new() { Enabled = true, LayoutMode = "absolute", X = 100, Y = 200, Width = 400, Height = 200 };
        Check(Near(scene.Camera.ScreenPointToRay(300, 300).Direction, -Vector3.UnitZ), "Ray ignored viewport offset.");
        core.MainCamera.ProjectionMode = "orthographic";
        core.MainCamera.Settings.OrthographicSize = 2;
        RuntimeRay ray = scene.Camera.ScreenPointToRay(500, 300);
        Check(Near(ray.Origin, new(4, 0, 5)) && Near(ray.Direction, -Vector3.UnitZ), "Orthographic picking used perspective rays.");
        core.Definition.GuiControls.Add(new() { X = 10, Y = 20, Width = 100, Height = 50, LayoutMode = "relative" });
        Check(scene.IsScreenPointOverGui(20, 100) && !scene.IsScreenPointOverGui(200, 100), "GUI hit test ignored actual/reference dimensions.");
    }

    internal static void TestCameraGuiCapture()
    {
        using RuntimeScene scene = new("main", new() { GuiControls = [new() { X = 0, Y = 0, Width = 100, Height = 100 }] }, p => p);
        AndroidCameraTouchInput camera = new();
        AndroidTouchPoint down = new(1, new(.02f, .02f), new(20, 20), Vector2.Zero, 1, AndroidTouchPhase.Began, true);
        AndroidInputSnapshot Snapshot(AndroidTouchPoint point, params AndroidTouchPoint[] transitions)
            => new([point], point, point.IsActive ? 1 : 0, false, false, AndroidDeviceInputSnapshot.Empty, transitions);
        RuntimeCameraInput Read(AndroidInputSnapshot input) => camera.GetInput(scene, input, 1000, 1000, 1000, 1000);
        Read(Snapshot(down, down));
        AndroidTouchPoint moved = down with { PixelPosition = new(200, 200), Delta = new(180, 180), Phase = AndroidTouchPhase.Moved };
        Check(Read(Snapshot(moved)) == RuntimeCameraInput.None, "Dragging outside a GUI control rotated the camera.");
        AndroidTouchPoint ended = moved with { Phase = AndroidTouchPhase.Ended };
        Read(Snapshot(ended, ended));
        down = moved with { Phase = AndroidTouchPhase.Began, Delta = Vector2.Zero };
        Read(Snapshot(down, down));
        moved = down with { Phase = AndroidTouchPhase.Moved, Delta = new(5, 3) };
        Check(Read(Snapshot(moved)).LookDelta == new Vector2(5, 3), "GUI capture survived into a new contact with the same ID.");
        AndroidTouchPoint other = moved with { Id = 2, PixelPosition = new(300, 200), Delta = new(-3, 1) };
        RuntimeCameraInput twoFinger = Read(new([moved, other], moved, 2, false, false, AndroidDeviceInputSnapshot.Empty));
        Check(twoFinger.LookDelta == Vector2.Zero && twoFinger.PanDelta == new Vector2(1, 2)
            && float.IsFinite(twoFinger.ZoomDelta), "GUI filtering broke two-finger camera pan/zoom.");
    }

    internal static void TestIgnoredEntity()
    {
        GameEntity Box(string id, float y) => new() { Id = id, Type = "empty",
            Transform = new() { Position = new(0, y, 0) },
            Colliders = [new() { Shape = "box", Position = Vector3Dto.Zero, Size = new(2, 2, 2) }] };
        using RuntimeScene scene = new("main", new() { Entities = [Box("actor", 3), Box("ground", 0)] }, p => p);
        AndroidScriptEntity actor = new(scene.GetEntity("actor")!, _ => { }, (_, _) => { }, () => null);
        RuntimeRay ray = new(new(0, 8, 0), -Vector3.UnitY);
        Check(scene.Physics.Raycast(ray, out RuntimeRaycastHit self) && self.Entity!.Id == "actor", "Ray fixture did not hit the actor.");
        Check(scene.Physics.Raycast(ray, out RuntimeRaycastHit hit, ignoredEntity: actor) && hit.Entity!.Id == "ground",
            "Android wrapper did not ignore the underlying actor during raycast.");
        Check(scene.Physics.SampleGround(0, 0, out hit, originY: 8, ignoredEntity: actor) && hit.Entity!.Id == "ground",
            "Android wrapper did not ignore the actor during ground sampling.");
        AndroidScriptEntity? missing = null;
        RuntimeEntity? converted = missing;
        Check(converted is null, "Null entity conversion dereferenced its wrapper.");
    }

    private static TouchPoint Touch(int id, float x, float y, TouchPhase phase) => new(id, x, y, 0, 0, phase, TouchInputKind.Touch, 1);
    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.0001f;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
