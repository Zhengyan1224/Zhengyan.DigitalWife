using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

internal static class RuntimeContextMenuPickingTests
{
    public static void Run()
    {
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
