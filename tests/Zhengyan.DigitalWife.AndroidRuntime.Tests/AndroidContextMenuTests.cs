using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using System.Numerics;

internal static class AndroidContextMenuTests
{
    public static void Targets()
    {
        using (var fixture = new AndroidRuntimeRegressionTests.ScriptFixture("return IsGuiEvent && IsContextMenuEvent ? ContextMenuTarget.TargetId + \"|\" + ContextMenuTarget.ColliderId : \"none\";"))
        using (var script = new AndroidScriptInstance(AndroidScriptCompiler.Compile(Path.Combine(fixture.Root, "script.csx"))))
        {
            fixture.Globals.Update(0, false, new("gui", "menu", "action", default)
                { ContextMenuTarget = new() { TargetType = "entity", TargetId = "other", ColliderId = "body" } });
            Check(script.ExecuteResult(fixture.Globals) as string == "other|body", "Android compiled scripts did not receive menu hit identity.");
            AndroidScriptGlobalsContract legacyGlobals = fixture.Globals;
            Check(legacyGlobals.IsContextMenuEvent && legacyGlobals.ContextMenuTarget?.TargetId == "other", "Android compatibility globals lost menu context.");
            fixture.Globals.Update(.1f, false, null);
            Check(script.ExecuteResult(fixture.Globals) as string == "none" && !legacyGlobals.IsContextMenuEvent && legacyGlobals.ContextMenuTarget is null,
                "Android retained context-menu fields in the next Update.");
            fixture.Globals.Update(0, false, new("gui", "button", "clicked", default));
            Check(script.ExecuteResult(fixture.Globals) as string == "none", "Ordinary GUI events were marked as context menus.");
        }
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
        ContextMenuSettings? Hit(float x, float y) => RuntimeContextMenuRouting.Resolve(scene, window, x, y, 1000, 1000);
        Check(ReferenceEquals(Hit(500, 500), front), "An occluded entity's menu won over the visible target.");
        Check(Hit(10, 10) is null, "An empty-space hold opened an entity menu.");
        front.TargetCollider = "wrong";
        Check(Hit(500, 500) is null, "The requested collider constraint was ignored.");
        front.TargetCollider = "front-collider";
        Check(ReferenceEquals(Hit(500, 500), front), "The bound collider did not match.");
        Check(RuntimeContextMenuRouting.TargetEntity(scene, front) == "front", "Menu events were not routed to the bound entity.");
        definition.GuiControls.Add(new() { Id = "button", TargetEntity = "back", X = 450, Y = 450, Width = 100, Height = 100 });
        Check(Hit(500, 500) is null, "Holding GUI opened the entity menu underneath.");
        ContextMenuSettings gui = new() { TargetType = "gui_control", TargetId = "button" };
        definition.ContextMenus.Add(gui);
        Check(ReferenceEquals(Hit(500, 500), gui) && RuntimeContextMenuRouting.TargetEntity(scene, gui) == "back", "GUI-specific menu routing failed.");
        gui.Items.ForEach(i => i.Enabled = false);
        Check(Hit(500, 500) is null, "A menu with no enabled items was opened.");
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

    public static void LayoutAndCameras()
    {
        ContextMenuSettings menu = new() { LayoutMode = "scaled", Width = 180, ItemHeight = 28 };
        ContextMenuLayout layout = ContextMenuLayout.Resolve(menu, 500, 500, new() { Width = 1000, Height = 500 });
        Check(layout.ContentWidth == 90 && layout.ItemHeight == 14 && layout.PaddingX == 4 && layout.FontSize == 9,
            "Menu dimensions used a uniform width scale instead of the PC layout rules.");
        menu.Style.FontSize = 0; menu.ItemHeight = -1; menu.PaddingX = -1;
        layout = ContextMenuLayout.Resolve(menu, 500, 500, new() { Width = 0, Height = 0 });
        Check(float.IsFinite(layout.Width) && layout.ItemHeight >= 12 && layout.PaddingX == 0 && layout.FontSize > 0,
            "Invalid menu settings produced nonfinite dimensions or invisible rows.");
        GameProjectScene definition = new()
        {
            Camera = new() { Position = new(0, 0, 10), Target = Vector3Dto.Zero },
            Cameras = [new() { IsMain = true, Camera = new() { Position = new(0, 0, 10), Target = Vector3Dto.Zero },
                Viewport = new() { Enabled = true, X = 500, Y = 0, Width = 500, Height = 500 } }],
            Entities = [new() { Id = "actor", Name = "Actor", Type = "empty", Colliders = [new() { Shape = "box", Size = new(2, 2, 2) }] }]
        };
        ContextMenuSettings actorMenu = new() { TargetType = "rigid-body", TargetId = "ACTOR" };
        definition.ContextMenus.Add(actorMenu);
        using RuntimeScene scene = new("menus", definition, p => p);
        GameWindowSettings window = new() { Width = 1000, Height = 500 };
        Check(ReferenceEquals(RuntimeContextMenuRouting.Resolve(scene, window, 750, 250, 1000, 500), actorMenu),
            "Menu picking ignored the viewport or the PC target aliases.");
        Check(RuntimeContextMenuRouting.Resolve(scene, window, 250, 250, 1000, 500) is null, "A menu picked outside its rendering camera.");
        definition.Sprites.Add(new() { Id = "sprite", Name = "Sprite", Path = "sprite.png", TargetEntity = "actor", X = 50, Y = 50, Width = 40, Height = 20, RotationDegrees = 90 });
        ContextMenuSettings spriteMenu = new() { TargetType = "2d-sprite", TargetId = "SPRITE" };
        definition.ContextMenus.Add(spriteMenu);
        Check(ReferenceEquals(RuntimeContextMenuRouting.Resolve(scene, window, 70, 75, 1000, 500), spriteMenu)
            && RuntimeContextMenuRouting.TargetEntity(scene, spriteMenu) == "actor", "Rotated sprite menus lost their hit box or target.");
        Check(RuntimeContextMenuRouting.Resolve(scene, window, -1, 0, 1000, 500) is null, "Out-of-window input opened a menu.");
        // Rendering clips an oversized viewport to the canvas before computing its projection.
        scene.MainCamera.Definition.Viewport.X = -100;
        scene.MainCamera.Definition.Viewport.Width = 1200;
        scene.MainCamera.Definition.Viewport.Height = 800;
        Check(ReferenceEquals(RuntimeContextMenuRouting.Resolve(scene, window, 500, 250, 1000, 500), actorMenu),
            "Menu picking used the untrimmed viewport instead of the visible render projection.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
