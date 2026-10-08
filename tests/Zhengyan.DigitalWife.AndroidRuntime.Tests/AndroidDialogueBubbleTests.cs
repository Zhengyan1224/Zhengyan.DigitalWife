using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class AndroidDialogueBubbleTests
{
    private static OrbitCamera Camera()
    {
        OrbitCamera camera = new() { Width = 800, Height = 600, ProjectionMode = CameraProjectionMode.Orthographic, OrthographicSize = 3 };
        camera.SetLookAt(new(0, 2, 10), new(0, 2, 0));
        return camera;
    }

    internal static void TestFollowing()
    {
        // Same attachment/offsets as DemoGame01's Body2/Body3 scripts.
        RuntimeDialogueBubble bubble = new("voice-chat-bubble");
        bubble.AttachToEntity("actor", useModelTopAnchor: true);
        bubble.SetWorldOffset(0, 0.2f, 0);
        bubble.SetScreenOffset(0, -16);
        OrbitCamera camera = Camera();
        Vector2 Project(Matrix4x4 world)
        {
            Vector3 anchor = AndroidDialogueBubbleLayout.ModelAnchor(new(-1, 0, -1), new(1, 2, 1), world, bubble.UseEntityTopAnchor);
            if (!AndroidDialogueBubbleLayout.TryResolveAnchor(bubble, anchor, camera.View, camera.Projection,
                new(0, 0, camera.Width, camera.Height), new(camera.Width, camera.Height), new(800, 600), out Vector2 position))
                throw new Exception("Visible character lost its dialogue bubble.");
            return position;
        }
        Near(Project(Matrix4x4.Identity), new(400, 264), "Bubble did not anchor above the model top.");
        Near(AndroidDialogueBubbleLayout.TopLeft(bubble, Project(Matrix4x4.Identity), new(440, 100)),
            new(180, 164), "Default bottom-center pivot did not put the entire panel above the character.");
        Near(Project(Matrix4x4.CreateTranslation(2, 0, 0)), new(600, 264), "Bubble stayed centered when its character moved.");
        Near(Project(Matrix4x4.CreateScale(2)), new(400, 64), "Model scale did not move the top anchor.");
        Near(Project(Matrix4x4.CreateScale(2) * Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(3, 1, 0)),
            new(300, 364), "Top anchor ignored the model's rotation/translation or rotated the world offset.");
        bubble.AttachToEntity("actor", useModelTopAnchor: false);
        Near(Project(Matrix4x4.Identity), new(400, 464), "Origin anchoring was ignored.");
        bubble.AttachToEntity("actor");
        bubble.LayoutMode = "relative";
        camera.Width = 1600; camera.Height = 900;
        Near(Project(Matrix4x4.Identity), new(800, 396), "Resize did not update projection and relative pixel offsets.");

        bubble.LayoutMode = "absolute";
        camera.Width = 800; camera.Height = 600;
        camera.ProjectionMode = CameraProjectionMode.Perspective;
        Vector2 beforeZoom = Project(Matrix4x4.CreateTranslation(2, 0, 0));
        camera.Dolly(1);
        if (Project(Matrix4x4.CreateTranslation(2, 0, 0)).X <= beforeZoom.X)
            throw new Exception("Bubble did not follow perspective camera zoom.");
        camera.SetLookAt(new(2, 2, 10), new(2, 2, 0));
        if (MathF.Abs(Project(Matrix4x4.CreateTranslation(2, 0, 0)).X - 400) > 0.01f)
            throw new Exception("Bubble did not follow camera movement.");
    }

    internal static void TestAnchorModesAndVisibility()
    {
        OrbitCamera camera = Camera();
        RuntimeDialogueBubble bubble = new("test");
        bool Resolve(Vector3? entity, LayoutRect viewport, out Vector2 position) =>
            AndroidDialogueBubbleLayout.TryResolveAnchor(bubble, entity, camera.View, camera.Projection,
                viewport, new(1600, 900), new(800, 600), out position);
        bubble.UseScreenSpace(100, 80, "relative");
        bubble.SetScreenOffset(10, -20);
        if (!Resolve(null, new(0, 0, 1600, 900), out Vector2 screen)) throw new Exception("Screen-space bubble was hidden.");
        Near(screen, new(220, 90), "Screen position/offset were not scaled from the reference resolution.");
        bubble.SetPivot(0.25f, 0.5f);
        Near(AndroidDialogueBubbleLayout.TopLeft(bubble, screen, new(200, 100)), new(170, 40), "Custom pivot was ignored.");

        bubble.LayoutMode = "absolute";
        bubble.UseWorldSpace(0, 2, 0);
        bubble.SetWorldOffset(0, 0, 0);
        bubble.SetScreenOffset(0, 0);
        if (!Resolve(null, new(80, 60, 400, 300), out Vector2 world)) throw new Exception("World-space bubble was hidden.");
        Near(world, new(280, 210), "Projection ignored the camera viewport's top-left offset/size.");
        bubble.AttachToEntity("removed-actor");
        if (Resolve(null, new(0, 0, 1600, 900), out _)) throw new Exception("Removed entity fell back to fixed screen UI.");
        camera.ProjectionMode = CameraProjectionMode.Perspective;
        foreach (Vector3 invisible in new[] { new Vector3(0, 2, 20), new Vector3(0, 2, -2000), new Vector3(float.NaN, 2, 0) })
            if (Resolve(invisible, new(0, 0, 1600, 900), out _)) throw new Exception("Invalid/off-depth anchor became fixed screen UI.");
    }

    private static void Near(Vector2 actual, Vector2 expected, string message)
    {
        if (Vector2.Distance(actual, expected) > 0.02f) throw new Exception($"{message} expected={expected}, actual={actual}");
    }
}
