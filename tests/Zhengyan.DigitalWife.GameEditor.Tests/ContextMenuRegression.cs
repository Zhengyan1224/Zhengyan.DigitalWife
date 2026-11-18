using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class ContextMenuRegression
{
    private static readonly Assembly Player = typeof(CSharpScriptGlobals).Assembly;
    public static void Picking()
    {
        GameProjectScene definition = new()
        {
            Entities = [new() { Id = "a", Name = "Actor A", Transform = new() { Position = new(-2, 0, 0) },
                Colliders = [new() { Id = "body", Name = "Body", Shape = "box", Size = new(2, 2, 2) },
                    new() { Id = "head", Name = "Head", Shape = "box", Size = new(2, 2, 2), Enabled = false }] },
                new() { Id = "b", Name = "Actor B", Transform = new() { Position = new(2, 0, 0) },
                    Colliders = [new() { Id = "b-body", Shape = "box", Size = new(2, 2, 2) }] }],
            ContextMenus = [new() { TargetType = "entity", Targets = [new() { TargetId = "a", TargetColliders = ["body", "HEAD"] }, new() { TargetId = "b" }] }]
        };
        var entityConstructor = typeof(RuntimeEntity).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(GameEntity), typeof(Func<string, string>)], null)!;
        RuntimeEntity[] entities = definition.Entities.Select(e => (RuntimeEntity)entityConstructor.Invoke([e, (Func<string, string>)(p => p)])).ToArray();
        OrbitCamera camera = new() { Width = 100, Height = 100, Fov = 45 };
        camera.SetLookAt(new(0, 0, 10), Vector3.Zero);
        var runtimeCamera = (RuntimeCamera)typeof(RuntimeCamera).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke([camera, null, (Func<IEnumerable<RuntimeEntity>>)(() => entities), definition, null]);
        var scene = (RuntimeScene)RuntimeHelpers.GetUninitializedObject(typeof(RuntimeScene));
        Set(scene, "_entitiesById", entities.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase));
        Set(scene, "_entitiesByName", entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase));
        Set(scene, "_camera", runtimeCamera);
        var game = (Game)RuntimeHelpers.GetUninitializedObject(Player.GetType("Zhengyan.DigitalWife.GamePlayer.GamePlayerGame")!);
        typeof(Game).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, DispatchProxy.Create<IWindow, ContextMenuWindowProxy>());
        var overlayType = Player.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeGuiOverlayComponent")!;
        object overlay = Activator.CreateInstance(overlayType,
            (Func<IReadOnlyList<GuiControlSettings>>)(() => definition.GuiControls),
            (Func<IReadOnlyList<ContextMenuSettings>>)(() => definition.ContextMenus),
            (Func<IReadOnlyList<SpriteSettings>>)(() => definition.Sprites),
            (Func<RuntimeScene?>)(() => scene), camera, (Func<GameWindowSettings>)(() => new() { Width = 100, Height = 100 }),
            (Func<string, string>)(p => p), (Action<GuiControlSettings, string>)((_, _) => { }),
            (Action<ContextMenuSettings, ContextMenuItemSettings, ContextMenuTargetInfo>)((_, _, _) => { }))!;
        typeof(GameComponent).GetProperty("Game")!.SetValue(overlay, game);
        ContextMenuTargetInfo? Pick(float x, float y = 50)
        {
            object?[] args = [new Vector2(x, y), null];
            return overlayType.GetMethod("ResolveContextMenuAt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, args) is null ? null : (ContextMenuTargetInfo)args[1]!;
        }
        Check(Pick(25) is { TargetId: "a", ColliderId: "body" } && Pick(75) is { TargetId: "b", ColliderId: "b-body" }, "PC multi-entity routing lost identity or per-entity collider filters.");
        definition.Entities[0].Colliders[0].Enabled = false; definition.Entities[0].Colliders[1].Enabled = true;
        Check(Pick(25)?.ColliderId == "head", "PC did not match the second selected collider by name.");
        definition.ContextMenus[0].Targets[0].TargetColliders = ["body"];
        Check(Pick(25) is null && Pick(75) is not null, "PC restricted colliders affected another target entity.");
        definition.GuiControls = [new() { Id = "gui-a", Name = "GUI A", TargetEntity = "a", X = 5, Y = 5, Width = 20, Height = 20 },
            new() { Id = "gui-b", Name = "GUI B", TargetEntity = "b", X = 70, Y = 5, Width = 20, Height = 20 }];
        definition.ContextMenus = [new() { TargetType = "gui_control", Targets = [new() { TargetId = "gui-a" }, new() { TargetId = "GUI B" }] }];
        Check(Pick(10, 10) is { TargetId: "gui-a", EntityId: "a" } && Pick(75, 10) is { TargetId: "gui-b", EntityId: "b" }, "PC multi-GUI identity or recipient was lost.");
        definition.Sprites = [new() { Id = "sprite-a", Path = "sprite.png", TargetEntity = "a", X = 5, Y = 70, Width = 20, Height = 20 },
            new() { Id = "sprite-b", Path = "sprite.png", TargetEntity = "b", X = 70, Y = 70, Width = 20, Height = 20 }];
        definition.ContextMenus = [new() { TargetType = "sprite", Targets = [new() { TargetId = "sprite-a" }, new() { TargetId = "sprite-b" }] }];
        Check(Pick(10, 75) is { TargetId: "sprite-a", EntityId: "a" } && Pick(75, 75) is { TargetId: "sprite-b", EntityId: "b" }, "PC multi-sprite routing was lost.");
    }

    public static void ScriptContract()
    {
        string root = Path.GetFullPath("temp/context-menu-script-regression");
        GameProject project = new();
        GameProjectStore.Save(root, project);
        string resultPath = Path.Combine(root, "result.txt"), scriptPath = Path.Combine(root, "scripts", "menu.csx");
        File.WriteAllText(scriptPath, "File.WriteAllText(" + JsonSerializer.Serialize(resultPath) +
            ", IsContextMenuEvent && IsGuiEvent ? GuiControlId + \"|\" + ContextMenuTarget.TargetId + \"|\" + ContextMenuTarget.ColliderId : \"none\");");
        project.Scene.Entities = [new() { Id = "actor", Scripts = [new() { Path = "scripts/menu.csx" }] }];
        GameProjectStore.SaveScene(root, project.DefaultScene, project.Scene);
        var desktop = DesktopCSharpScriptPrecompiler.Precompile(root, project);
        var android = AndroidCSharpScriptPrecompiler.Precompile(root, project);
        var web = WebCSharpScriptPrecompiler.Precompile(root, project);
        Check(desktop.Entries.Count == 1 && android.Errors.Count == 0 && web.Errors.Count == 0,
            "New context-menu globals failed script export: " + string.Join(";", android.Errors.Concat(web.Errors)));
        using IDisposable script = (IDisposable)Activator.CreateInstance(Player.GetType("Zhengyan.DigitalWife.GamePlayer.CSharpScriptInstance")!, scriptPath, root)!;
        MethodInfo gui = script.GetType().GetMethod("GuiEvent")!;
        gui.Invoke(script, [null, null, null, null, "menu", "Menu", "action", new ContextMenuTargetInfo { TargetType = "entity", TargetId = "other", ColliderId = "body" }]);
        Check(File.ReadAllText(resultPath) == "menu|other|body", "PC precompiled script did not receive the actual hit object.");
        gui.Invoke(script, [null, null, null, null, "button", "Button", "clicked", null]);
        Check(File.ReadAllText(resultPath) == "none", "An ordinary PC GUI event retained a context-menu target.");

        string pythonPath = Path.Combine(root, "scripts", "menu.py");
        File.WriteAllText(pythonPath, """
            def gui_event(entity, scene, input, audio, control_id, control_name, event_name, context_menu_target=None):
                if context_menu_target is not None:
                    assert IsContextMenuEvent and ContextMenuTarget.TargetId == context_menu_target.TargetId
                    print("MENU_TARGET=" + context_menu_target.TargetId + "|" + context_menu_target.ColliderId)
                else:
                    assert not IsContextMenuEvent and ContextMenuTarget is None
                    print("MENU_TARGET=none")
            """);
        string worker = (string)Player.GetType("Zhengyan.DigitalWife.GamePlayer.PythonScriptInstance")!
            .GetMethod("EnsureWorkerScriptPath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        ProcessStartInfo start = new("python") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(worker); start.ArgumentList.Add(pythonPath); start.ArgumentList.Add(root);
        start.ArgumentList.Add("Windows");
        using Process process = Process.Start(start)!;
        process.StandardInput.WriteLine("""{"event":"gui_event","controlId":"menu","guiEventName":"action","contextMenuTarget":{"targetType":"entity","targetId":"other","colliderId":"body"}}""");
        process.StandardInput.WriteLine("""{"event":"gui_event","controlId":"button","guiEventName":"clicked"}""");
        process.StandardInput.Close();
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0 && output.Contains("MENU_TARGET=other|body") && output.Contains("MENU_TARGET=none") && !output.Contains("__DW_ERROR__"),
            "PC Python callback context failed: " + output + error);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public class ContextMenuWindowProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod?.Name == "get_Size" ? new Vector2D<int>(100, 100) : throw new NotSupportedException(targetMethod?.Name);
}
