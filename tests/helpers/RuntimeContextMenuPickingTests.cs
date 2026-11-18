using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

internal static class RuntimeContextMenuPickingTests
{
    public static void Run()
    {
        MultipleTargets();
        GameProjectScene definition = new()
        {
            Camera = new() { Position = new(0, 0, 10), Target = Vector3Dto.Zero },
            Entities =
            [
                new() { Id = "actor", Type = "pmx_model", Colliders =
                    [new() { Id = "body", Name = "Body Collider", Shape = "box", Size = new(2, 2, 2) }] },
                new() { Id = "outfit", Type = "pmx_model", Transform = new() { Position = new(0, 0, 5) } }
            ],
            ContextMenus = [new() { TargetType = "entity", TargetId = "actor", TargetCollider = "body" }]
        };
        using RuntimeScene scene = new("pc-menu-picking", definition, p => p);
        scene.MainCamera.Position = new(0, 0, 10);
        scene.MainCamera.Target = Vector3.Zero;
        GameWindowSettings window = new() { Width = 100, Height = 100 };
        ContextMenuSettings? Menu(float x = 50) => RuntimeContextMenuRouting.Resolve(scene, window, x, 50, 100, 100);
        RuntimeRay ray = scene.MainCamera.ScreenPointToRay(50, 50, 100, 100);
        Check(scene.MainCamera.RaycastEntity(ray, out var hit) && hit.Entity.Id == "actor" && hit.ColliderId == "body",
            "A nearer collider-free outfit took priority over the character's explicit collider.");
        Check(ReferenceEquals(Menu(), definition.ContextMenus[0]), "The character's collider menu was blocked by its outfit.");
        definition.ContextMenus[0].TargetCollider = "BODY COLLIDER";
        Check(Menu() is not null, "PC collider name matching was lost.");
        definition.ContextMenus[0].TargetCollider = "wrong";
        Check(Menu() is null, "A menu opened without hitting its bound collider.");

        // A missed enabled collider cannot turn into a model fallback hit.
        definition.Entities[0].Colliders[0].Position = new(10, 0, 0);
        definition.Entities[1].Transform.Position = new(10, 0, 5);
        Check(!scene.MainCamera.RaycastEntity(ray, out _), "An entity with an enabled but missed collider used a fallback sphere.");
        definition.Entities[0].Colliders[0].Enabled = false;
        Check(scene.MainCamera.RaycastEntity(ray, out hit) && hit.ColliderShape == "fallback_sphere",
            "Disabling the collider did not restore the PC PMX fallback.");
        Check(Menu() is null, "A sphere fallback satisfied a menu's explicit collider constraint.");
        definition.ContextMenus[0].TargetCollider = "";
        Check(Menu() is not null, "A collider-free PMX entity could not open its menu through the PC fallback.");

        definition.Entities[0].Transform.Scale = new(.2f, .2f, .2f);
        Check(Menu(52) is null, "The fallback sphere ignored the PMX model's scale.");
        definition.Entities[0].Transform.Scale = new(.2f, 2, .2f);
        Check(Menu(52) is not null, "The fallback sphere did not use the largest scale axis.");
        definition.Entities[0].Type = "empty";
        Check(Menu() is null, "An empty entity received a PMX-only fallback sphere.");

        // DemoGame08: scaled body capsule and a separate outfit without colliders.
        definition.Entities[0].Type = "pmx_model";
        definition.Entities[0].Transform.Scale = new(.2f, .2f, .2f);
        definition.Entities[0].Colliders = [new() { Id = "body", Shape = "capsule", Position = new(0, 9.74f, 0), Radius = 3, Height = 20 }];
        definition.Entities[1].Transform.Position = Vector3Dto.Zero;
        definition.Entities[1].Transform.Scale = new(.2f, .2f, .2f);
        definition.ContextMenus[0].TargetCollider = "body";
        scene.MainCamera.Position = new(0, 2, 8);
        scene.MainCamera.Target = new(0, 1.2f, 0);
        Check(Menu() is not null, "The DemoGame08 scaled character capsule did not open its bound menu.");
    }

    private static void MultipleTargets()
    {
        ContextMenuSettings legacy = System.Text.Json.JsonSerializer.Deserialize<ContextMenuSettings>(
            """{"TargetType":"entity","TargetId":"actor","TargetCollider":"c1"}""")!;
        Check(legacy.GetTargets().Single().TargetColliders.SequenceEqual(["c1"]), "Old single-target scene bindings were lost.");
        legacy.Targets = [new() { TargetId = "other" }];
        var restored = System.Text.Json.JsonSerializer.Deserialize<ContextMenuSettings>(System.Text.Json.JsonSerializer.Serialize(legacy))!;
        Check(restored.GetTargets().Single().TargetId == "other", "New bindings did not override stale legacy fields on serialization.");
        GameProjectScene definition = new()
        {
            Entities = [new() { Id = "actor", Name = "Actor", Transform = new() { Position = new(-2, 0, 0) },
                Colliders = [new() { Id = "c1", Name = "First", Shape = "box", Size = new(2, 2, 2) },
                    new() { Id = "c2", Name = "Second", Shape = "box", Size = new(2, 2, 2), Enabled = false },
                    new() { Id = "c3", Name = "Third", Shape = "box", Size = new(2, 2, 2), Enabled = false }] },
                new() { Id = "other", Name = "Other", Transform = new() { Position = new(2, 0, 0) },
                    Colliders = [new() { Id = "other-collider", Name = "Other Collider", Shape = "box", Size = new(2, 2, 2) }] }],
            ContextMenus = [new() { TargetType = "entity", Targets =
                [new() { TargetId = "ACTOR", TargetColliders = ["c1", "SECOND"] }, new() { TargetId = "other" }] }]
        };
        using RuntimeScene scene = new("multi-menu", definition, p => p);
        scene.MainCamera.Position = new(0, 0, 10); scene.MainCamera.Target = Vector3.Zero;
        GameWindowSettings window = new() { Width = 100, Height = 100 };
        ContextMenuSelection? Pick(float x, float y = 50) => RuntimeContextMenuRouting.ResolveSelection(scene, window, x, y, 100, 100);
        var first = Pick(25);
        Check(first?.Target is { TargetType: "entity", TargetId: "actor", TargetName: "Actor", EntityId: "actor", ColliderId: "c1", PointerX: 25 },
            "The first entity/collider identity was not captured.");
        Check(Pick(75)?.Target.ColliderId == "other-collider", "The unrestricted second entity inherited the first entity's collider filter.");
        definition.Entities[0].Colliders[0].Enabled = false;
        definition.Entities[0].Colliders[1].Enabled = true;
        Check(Pick(25)?.Target.ColliderId == "c2", "The second selected collider did not match by name.");
        definition.Entities[0].Colliders[1].Enabled = false;
        definition.Entities[0].Colliders[2].Enabled = true;
        Check(Pick(25) is null, "An unselected collider opened a restricted entity menu.");
        Check(first!.Target.ColliderId == "c1", "An opening's collider snapshot changed after the scene changed.");
        definition.ContextMenus[0].Targets[0].TargetColliders.Clear();
        Check(Pick(25)?.Target.ColliderId == "c3", "All colliders did not apply to the whole entity.");

        definition.GuiControls = [new() { Id = "gui-a", Name = "GUI A", TargetEntity = "actor", X = 5, Y = 5, Width = 20, Height = 20 },
            new() { Id = "gui-b", Name = "GUI B", TargetEntity = "other", X = 70, Y = 5, Width = 20, Height = 20 }];
        definition.ContextMenus = [new() { TargetType = "gui_control", Targets = [new() { TargetId = "gui-a" }, new() { TargetId = "GUI B" }] }];
        Check(Pick(10, 10)?.Target is { TargetId: "gui-a", TargetName: "GUI A", EntityId: "actor", ColliderId: "" }
            && Pick(75, 10)?.Target is { TargetId: "gui-b", EntityId: "other" }, "Multiple GUI targets lost their hit identity or script recipient.");
        definition.Sprites = [new() { Id = "sprite-a", Name = "Sprite A", Path = "sprite.png", X = 5, Y = 70, Width = 20, Height = 20, TargetEntity = "actor" },
            new() { Id = "sprite-b", Name = "Sprite B", Path = "sprite.png", X = 70, Y = 70, Width = 20, Height = 20, TargetEntity = "other" }];
        definition.ContextMenus = [new() { TargetType = "sprite", Targets = [new() { TargetId = "sprite-a" }, new() { TargetId = "sprite-b" }] }];
        Check(Pick(10, 75)?.Target is { TargetType: "sprite", TargetId: "sprite-a", EntityId: "actor" }
            && Pick(75, 75)?.Target is { TargetId: "sprite-b", EntityId: "other" }, "Multiple sprite targets lost their identity.");
        definition.ContextMenus[0].Targets = [new() { TargetId = "missing" }, new()];
        Check(Pick(10, 75) is null, "An empty or missing target row became a whole-window binding.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
