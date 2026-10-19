using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

internal static class AndroidGuiEventTests
{
    internal static void TestQuickTap()
    {
        AndroidTouchState state = new();
        state.Apply(new(global::Android.Views.MotionEventActions.Down, 100, 75));
        state.Apply(new(global::Android.Views.MotionEventActions.Move, 105, 75));
        state.Apply(new(global::Android.Views.MotionEventActions.Up, 105, 75));
        AndroidInputSnapshot input = state.BeginFrame(200, 150);
        Check(input.TouchTransitions.Select(point => point.Phase).SequenceEqual([AndroidTouchPhase.Began, AndroidTouchPhase.Ended]),
            "A quick tap lost or reordered its down/up transitions.");
        Check(input.TouchTransitions[0].PixelPosition == new Vector2(100, 75)
            && input.TouchTransitions[1].PixelPosition == new Vector2(105, 75), "Touch transitions lost their own hit-test positions.");
        Check(input.IsTouchStarted && input.IsTouchEnded && input.ActiveTouchCount == 0, "Tap snapshot changed the current touch state.");
        Check(input.WithDeviceInput(new()).TouchTransitions.SequenceEqual(input.TouchTransitions), "Device input discarded the touch transitions.");

        using RuntimeScene scene = CreateScene();
        AndroidGuiPointerEvents pointer = new();
        List<AndroidRuntimeEvent> events = [];
        foreach (AndroidTouchPoint touch in input.TouchTransitions)
        {
            pointer.Dispatch(scene, touch.Id, touch.Phase == AndroidTouchPhase.Began ? AndroidGuiPointerPhase.Pressed : AndroidGuiPointerPhase.Released,
                touch.PixelPosition, touch.Position, new(200, 150), new(800, 600), events.Add);
        }
        Check(events.Select(value => value.EventName).SequenceEqual(["pressed", "released", "clicked"]), "A between-frame tap failed to click the button.");
        Check(state.BeginFrame(200, 150).TouchTransitions.Count == 0, "Completed transitions leaked into the next frame.");

        state.Apply(new(global::Android.Views.MotionEventActions.Down, 100, 75));
        state.Apply(new(global::Android.Views.MotionEventActions.Cancel));
        input = state.BeginFrame(200, 150);
        Check(input.TouchTransitions.Select(point => point.Phase).SequenceEqual([AndroidTouchPhase.Began, AndroidTouchPhase.Cancelled]),
            "A native cancellation lost its preceding press.");
    }

    internal static void TestRecordingEvents()
    {
        using RuntimeScene scene = CreateScene();
        AndroidGuiPointerEvents pointer = new();
        List<AndroidRuntimeEvent> events = [];
        bool recording = false;
        int starts = 0;
        int stops = 0;
        int unrelatedSpeech = 0;
        void Dispatch(AndroidRuntimeEvent value)
        {
            events.Add(value);
            RuntimeEntity? target = Resolve(scene, value.TargetEntity);
            if (target?.Name != "Body")
            {
                if (value.EventName == "clicked") unrelatedSpeech++;
                return;
            }
            // Same guards as DemoGame01's Body2.csx recording handlers.
            if (value.Text == "RecordButton" && value.EventName == "pressed" && !recording)
            {
                recording = true;
                starts++;
            }
            if (value.Text == "RecordButton" && value.EventName == "released" && recording)
            {
                recording = false;
                stops++;
            }
        }

        Check(Send(pointer, scene, 1, AndroidGuiPointerPhase.Pressed, new(100, 75), Dispatch), "Recording press missed the relative button.");
        Check(recording && starts == 1 && stops == 0, "Press did not start recording immediately.");
        Check(Send(pointer, scene, 1, AndroidGuiPointerPhase.Released, new(100, 75), Dispatch), "Recording release missed its button.");
        Check(!recording && starts == 1 && stops == 1, "Release did not stop recording.");
        Check(events.Select(value => value.EventName).SequenceEqual(["pressed", "released", "clicked"]), "Button events differ from the desktop order.");
        Check(events.All(value => value.Id == "record" && value.Text == "RecordButton" && value.Position == new Vector2(.5f, .5f)), "Button event fields lost their control or touch coordinates.");
        Check(unrelatedSpeech == 0, "Recording clicked was broadcast to the house or outfit.");

        scene.Definition.GuiControls[0].EventName = "custom_click";
        events.Clear();
        Send(pointer, scene, 2, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        Send(pointer, scene, 2, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Select(value => value.EventName).SequenceEqual(["pressed", "released", "custom_click"]), "Custom click replaced the press or release event.");

        scene.Definition.GuiControls[0].EventName = " ";
        events.Clear();
        Send(pointer, scene, 3, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        Send(pointer, scene, 3, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Last().EventName == "clicked", "Blank click event did not use the desktop default.");
    }

    internal static void TestReleaseAndCancellation()
    {
        using RuntimeScene scene = CreateScene();
        AndroidGuiPointerEvents pointer = new();
        List<AndroidRuntimeEvent> events = [];

        Send(pointer, scene, 1, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        Check(Send(pointer, scene, 1, AndroidGuiPointerPhase.Released, new(190, 140), events.Add), "Release outside the button was not consumed.");
        Check(events.Select(value => value.EventName).SequenceEqual(["pressed", "released"]), "Leaving the button failed to release or generated a click.");

        events.Clear();
        Send(pointer, scene, 2, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        Send(pointer, scene, 2, AndroidGuiPointerPhase.Cancelled, new(100, 75), events.Add);
        Send(pointer, scene, 2, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Select(value => value.EventName).SequenceEqual(["pressed", "released"]), "Canceled touch clicked or kept the button pressed.");

        events.Clear();
        Send(pointer, scene, 3, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Count == 0, "A release without a button press generated events.");
        Send(pointer, scene, 3, AndroidGuiPointerPhase.Pressed, new(190, 140), events.Add);
        Send(pointer, scene, 3, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Count == 0, "Dragging into a button clicked it.");

        Send(pointer, scene, 4, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        events.Clear();
        using RuntimeScene next = CreateScene();
        Send(pointer, next, 4, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Count == 0, "A scene change released or clicked an old scene's button.");

        Send(pointer, next, 5, AndroidGuiPointerPhase.Pressed, new(100, 75), events.Add);
        pointer.Reset();
        events.Clear();
        Send(pointer, next, 5, AndroidGuiPointerPhase.Released, new(100, 75), events.Add);
        Check(events.Count == 0, "Reset retained a captured button.");
    }

    internal static void TestTargets()
    {
        using RuntimeScene scene = CreateScene();
        Check(Resolve(scene, "")?.Name == "Body", "Default GUI target ignored the outfit's relation to Body.");
        Check(Resolve(scene, "maid")?.Name == "MaidOutfit", "Explicit entity ID did not override the default.");
        Check(Resolve(scene, "FOREIGNHOUSE")?.Name == "foreignhouse", "Explicit entity name was not resolved case-insensitively.");
        Check(Resolve(scene, "missing")?.Name == "Body", "Missing explicit target did not fall back like desktop.");

        RuntimeEntity body = scene.GetEntity("body")!;
        body.Definition.Scripts[0].Enabled = false;
        Check(Resolve(scene, "")?.Name == "foreignhouse", "A disabled script remained a fallback target.");
        Check(Resolve(scene, "body") == body, "An explicit unscripted target must not broadcast or select another entity.");
        scene.GetEntity("house")!.Definition.Scripts.Clear();
        Check(Resolve(scene, "")?.Name == "MaidOutfit", "Single scripted PMX fallback was not selected.");
        scene.GetEntity("maid")!.Definition.Scripts.Clear();
        Check(Resolve(scene, "") is null, "An unscripted scene acquired a default target.");

        scene.AddEntity(new() { Id = "controller", Name = "Controller", Type = "empty", Scripts = [new() { Path = "controller.csx" }] });
        Check(Resolve(scene, "")?.Name == "Controller", "Non-PMX script fallback was lost.");
        scene.AddEntity(new() { Id = "z", Name = "Zulu", Type = "pmx_model", Scripts = [new() { Path = "z.csx" }] });
        scene.AddEntity(new() { Id = "a", Name = "Alpha", Type = "pmx_model", Scripts = [new() { Path = "a.csx" }] });
        Check(Resolve(scene, "")?.Name == "Alpha", "Equal PMX targets did not use desktop name ordering.");
    }

    private static RuntimeEntity? Resolve(RuntimeScene scene, string target)
        => AndroidGuiEventRouting.ResolveTarget(scene, target, entity => entity.Definition.Scripts.Any(binding => binding.Enabled));

    private static bool Send(AndroidGuiPointerEvents pointer, RuntimeScene scene, int id, AndroidGuiPointerPhase phase,
        Vector2 pixel, Action<AndroidRuntimeEvent> dispatch)
        => pointer.Dispatch(scene, id, phase, pixel, new(.5f, .5f), new(200, 150), new(800, 600), dispatch);

    private static RuntimeScene CreateScene()
        => new("scene2.scene.json", new()
        {
            Entities =
            [
                new() { Id = "house", Name = "foreignhouse", Type = "pmx_model", Scripts = [new() { Path = "house.csx" }] },
                new() { Id = "body", Name = "Body", Type = "pmx_model", Scripts = [new() { Path = "body.csx" }] },
                new() { Id = "maid", Name = "MaidOutfit", Type = "pmx_model", Scripts = [new() { Path = "maid.csx" }],
                    Relation = new() { Enabled = true, RelationEntity = "BODY" } }
            ],
            GuiControls = [new() { Id = "record", Name = "RecordButton", Type = "button", LayoutMode = "relative", X = 200, Y = 200, Width = 400, Height = 200 }]
        }, path => path);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
