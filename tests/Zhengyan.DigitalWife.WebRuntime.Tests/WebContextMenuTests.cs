using System.Reflection;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;
using Zhengyan.DigitalWife.GameProjects;

internal static class WebContextMenuTests
{
    public static async Task RunAsync()
    {
        RuntimeContextMenuPickingTests.Run();
        GameProjectScene definition = new()
        {
            Entities = [new() { Id = "actor", Name = "Actor", Scripts = [new() { Path = "script.csx" }] },
                new() { Id = "other", Name = "Other", Scripts = [new() { Path = "script.csx" }] }],
            ContextMenus = [new() { Id = "menu", Name = "Actor Menu", LayoutMode = "relative",
                Items = [new() { Id = "disabled", Enabled = false }, new() { Id = "action", EventName = "custom-action" }] }]
        };
        byte[] assembly = File.ReadAllBytes(typeof(ContextMenuWebScript).Assembly.Location);
        Dictionary<string, byte[]> files = new()
        {
            ["game.project.json"] = Encoding.UTF8.GetBytes("""{"defaultScene":"scenes/main.scene.json","window":{"width":1000,"height":500}}"""),
            ["scenes/main.scene.json"] = JsonSerializer.SerializeToUtf8Bytes(definition),
            ["scenes/next.scene.json"] = JsonSerializer.SerializeToUtf8Bytes(definition),
            ["compiled/web/script.dll"] = assembly
        };
        WebAssetManifest manifest = new()
        {
            Assets = files.Select(f => new WebAssetEntry { Path = f.Key, Size = f.Value.Length }).ToList(),
            Scripts = [new() { Id = "actor-script", Entity = "actor", Assembly = "compiled/web/script.dll", Type = typeof(ContextMenuWebScript).FullName! },
                new() { Id = "other-script", Entity = "other", Assembly = "compiled/web/script.dll", Type = typeof(ContextMenuWebScript).FullName! }]
        };
        WebMemoryAssetStore store = new(manifest); foreach (var file in files) store.Add(file.Key, file.Value);
        FakeBrowser browser = new();
        await using WebRuntimeHost host = new(browser, new WebProjectLoader(store), new WebAudioService(browser), new WebSpeechService(browser));
        await host.StartAsync(default, default);
        Check(host.Snapshot.IsRunning, host.Snapshot.LastError ?? "The fixture did not start.");
        await host.OnCanvasResized(500, 500, 1);
        WebLoadedProject Loaded() => (WebLoadedProject)typeof(WebRuntimeHost).GetField("_loadedProject", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
        var loaded = Loaded();
        // The window menu chooses the same stable script fallback as the PC player.
        await host.OnContextMenuRequest(490, 490);
        JsonElement popup = JsonSerializer.SerializeToElement(browser.ContextMenu);
        string token = popup.GetProperty("token").GetString()!;
        Check(popup.GetProperty("width").GetSingle() == 99.5f && popup.GetProperty("itemHeight").GetSingle() == 14
            && popup.GetProperty("paddingX").GetSingle() == 4 && popup.GetProperty("fontSize").GetSingle() == 9,
            "Relative menu width/padding/font did not follow the PC's separate axis and uniform scales: " + popup);
        await host.OnContextMenuItem("menu", "disabled", token);
        Check(!loaded.RuntimeScene.Save.Exists("menu-actor"), "A disabled menu item dispatched a script event.");
        await host.OnContextMenuItem("menu", "action", token);
        Check(loaded.RuntimeScene.Save.ReadText("menu-actor") == "menu|Actor Menu|custom-action|actor", "The PC GUI event contract or target was lost.");
        Check(!loaded.RuntimeScene.Save.Exists("menu-other"), "The menu click was broadcast to unrelated scripts.");
        Check(loaded.RuntimeScene.Save.ReadJson<ContextMenuTargetInfo>("hit-actor")?.TargetType == "window", "Window context-menu identity was missing.");
        var menu = loaded.SceneDefinition.ContextMenus[0];
        loaded.SceneDefinition.GuiControls = [new() { Id = "gui-a", Name = "GUI A", TargetEntity = "actor", X = 10, Y = 10, Width = 60, Height = 60 },
            new() { Id = "gui-b", Name = "GUI B", TargetEntity = "other", X = 130, Y = 10, Width = 60, Height = 60 }];
        menu.TargetType = "gui_control";
        menu.Targets = [new() { TargetId = "gui-a" }, new() { TargetId = "gui-b" }];
        await host.OnContextMenuRequest(140, 20);
        string guiToken = JsonSerializer.SerializeToElement(browser.ContextMenu).GetProperty("token").GetString()!;
        // Selection moves the pointer to the popup. Moving the original GUI must not change the saved hit.
        loaded.SceneDefinition.GuiControls[1].X = 400;
        loaded.SceneDefinition.GuiControls[1].TargetEntity = "actor";
        await host.OnContextMenuItem("menu", "action", guiToken);
        var guiHit = loaded.RuntimeScene.Save.ReadJson<ContextMenuTargetInfo>("hit-other");
        Check(guiHit is { TargetType: "gui_control", TargetId: "gui-b", TargetName: "GUI B", EntityId: "other", PointerX: 140, PointerY: 20 },
            "Web menu clicks re-picked the moved target or were sent to the first binding.");
        loaded.SceneDefinition.GuiControls[1].X = 130;
        loaded.SceneDefinition.GuiControls[1].TargetEntity = "other";
        await host.OnContextMenuRequest(20, 20);
        string firstGuiToken = JsonSerializer.SerializeToElement(browser.ContextMenu).GetProperty("token").GetString()!;
        await host.OnContextMenuItem("menu", "action", firstGuiToken);
        Check(loaded.RuntimeScene.Save.ReadJson<ContextMenuTargetInfo>("hit-actor")?.TargetId == "gui-a", "A newer opening retained the previous GUI target.");
        menu.TargetType = "window";
        menu.Targets.Clear();
        loaded.RuntimeScene.Save.Delete("menu-actor");
        await host.OnContextMenuRequest(10, 10);
        string secondToken = JsonSerializer.SerializeToElement(browser.ContextMenu).GetProperty("token").GetString()!;
        host.OnContextMenuDismissed(token); // A delayed dismiss must not close a newer opening.
        await host.OnContextMenuItem("menu", "action", token);
        Check(!loaded.RuntimeScene.Save.Exists("menu-actor"), "A stale opening dispatched an event to a newer menu.");
        loaded.RuntimeScene.LoadScene("scenes/next.scene.json");
        await host.OnAnimationFrame(1000);
        Check(browser.ContextMenuClosed > 0 && browser.ContextMenu is null, "Scene changes left an interactive menu behind.");
        await host.OnContextMenuItem("menu", "action", secondToken);
        Check(!Loaded().RuntimeScene.Save.Exists("menu-actor"), "An old menu click reached the replacement scene.");
        Console.WriteLine("PASS Web context menu layout, GUI script contract, target, disabled items and stale scene/opening rejection");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class ContextMenuWebScript : IWebGameScript
{
    public void Start(WebScriptContext context) { }
    public void Update(WebScriptContext context, float deltaSeconds) { }
    public void OnEvent(WebScriptContext context, WebScriptEvent gameEvent)
    {
        if (gameEvent.IsContextMenuEvent) context.Scene.Save.WriteJson("hit-" + context.Entity!.Id, gameEvent.ContextMenuTarget);
        if (gameEvent.IsGuiEvent) context.Scene.Save.WriteText("menu-" + context.Entity!.Id,
            $"{gameEvent.ControlId}|{gameEvent.ControlName}|{gameEvent.EventName}|{gameEvent.EntityId}");
    }
}
