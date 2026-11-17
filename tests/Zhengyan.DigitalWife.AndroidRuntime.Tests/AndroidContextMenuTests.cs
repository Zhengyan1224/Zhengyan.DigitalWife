using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using System.Numerics;

internal static class AndroidContextMenuTests
{
    public static void Targets()
    {
        GameProjectScene definition = new();
        definition.Camera.Position = new(0, 0, 10); definition.Camera.Target = Vector3Dto.Zero;
        GameEntity Box(string id, float z) => new()
        {
            Id = id, Name = id + "-name", Type = "empty", Transform = new() { Position = new(0, 0, z) },
            Colliders = [new() { Id = id + "-collider", Name = "body", Shape = "box", Size = new(2, 2, 2) }]
        };
        definition.Entities = [Box("front", 2), Box("back", 0)];
        ContextMenuSettings Menu(string id) => new() { Name = id, TargetType = "entity", TargetId = id };
        var front = Menu("front"); var back = Menu("back");
        definition.ContextMenus = [front, back];
        using RuntimeScene scene = new("menu-tests", definition, p => p);
        GameWindowSettings window = new() { Width = 1000, Height = 1000 };
        ContextMenuSettings? Hit(float x, float y) => AndroidContextMenuRouting.Resolve(scene, window, x, y, 1000, 1000);
        Check(ReferenceEquals(Hit(500, 500), front), "An occluded entity's menu won over the visible target.");
        Check(Hit(10, 10) is null, "An empty-space hold opened an entity menu.");
        front.TargetCollider = "wrong";
        Check(Hit(500, 500) is null, "The requested collider constraint was ignored.");
        front.TargetCollider = "front-collider";
        Check(ReferenceEquals(Hit(500, 500), front), "The bound collider did not match.");
        Check(AndroidContextMenuRouting.TargetEntity(scene, front) == "front", "Menu events were not routed to the bound entity.");
        definition.GuiControls.Add(new() { Id = "button", TargetEntity = "back", X = 450, Y = 450, Width = 100, Height = 100 });
        Check(Hit(500, 500) is null, "Holding GUI opened the entity menu underneath.");
        ContextMenuSettings gui = new() { TargetType = "gui_control", TargetId = "button" };
        definition.ContextMenus.Add(gui);
        Check(ReferenceEquals(Hit(500, 500), gui) && AndroidContextMenuRouting.TargetEntity(scene, gui) == "back", "GUI-specific menu routing failed.");
        gui.Items.ForEach(i => i.Enabled = false);
        Check(Hit(500, 500) is null, "A menu with no enabled items was opened.");
        RuntimeRay ray = new(new(0, 0, 10), -Vector3.UnitZ);
        Check(AndroidContextMenuRouting.RaycastBounds(ray, Matrix4x4.CreateScale(2), -Vector3.One, Vector3.One, out float distance)
            && Math.Abs(distance - 8) < .001f, "Scaled PMX bounds changed the ray's world distance.");
        Check(!AndroidContextMenuRouting.RaycastBounds(ray, Matrix4x4.CreateTranslation(10, 0, 0), -Vector3.One, Vector3.One, out _),
            "Holding away from model bounds was classified as an entity hit.");
    }

    public static void AlphaRegions()
    {
        const int width = 8, height = 8;
        byte[] rgba = new byte[width * height * 4];
        // One opaque cell at the bottom-left in renderer coordinates.
        rgba[3] = 255;
        var rects = AndroidAlphaTouchRegion.Build(rgba, [], width, height);
        Check(rects.Count == 1 && rects[0] == new AndroidTouchRect(0, 4, 4, 8), "RGBA readback was vertically flipped or transparent cells captured input.");
        int[] gui = new int[width * height]; gui[7] = unchecked((int)0xff000000);
        rects = AndroidAlphaTouchRegion.Build(rgba, gui, width, height);
        Check(rects.Contains(new(4, 0, 8, 4)), "Native GUI alpha did not contribute to the touch region.");
        Array.Fill(rgba, (byte)255);
        rects = AndroidAlphaTouchRegion.Build(rgba, [], width, height);
        Check(rects.Count == 1 && rects[0] == new AndroidTouchRect(0, 0, 8, 8), "Adjacent opaque rows were not merged.");
        Check(AndroidAlphaTouchRegion.Build(new byte[8], [], width, height).Count == 0, "An incomplete frame produced invalid regions.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
