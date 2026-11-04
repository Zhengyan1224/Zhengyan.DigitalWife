using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

if (args is ["--export-project", string projectDirectory, string outputPath])
{
    // Use a disposable project copy: export saves resources and precompiled scripts.
    ExportAndCheck(projectDirectory, outputPath);
    return 0;
}

(string Name, Action Run)[] tests =
[
    ("OpenGL and Vulkan viewport placement and underwater layout", TestViewportCoordinates),
    ("Editor sends matching viewport, scissor, and clear rectangles", ViewportRenderingRegression.Run),
    ("Startup directory is available before the overlay captures it", TestStartupDirectory),
    ("Scene requests preserve the current scene until the next update", TestDeferredSceneSwitch),
    ("Save and export all scenes after opening a project", TestMultiSceneExport),
    ("Web static directory export writes a browser manifest", TestWebStaticExport),
    ("Failed Web precompile removes a stale assembly", TestWebPrecompileRemovesStaleAssembly),
    ("Web ABI precompiles GUI audio motion and speech globals", TestWebAbiPrecompile)
];
int failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
    }
}
return failures == 0 ? 0 : 1;

static void TestViewportCoordinates()
{
    // DemoGame01 Camera 2, resized targets, full screen, and a top viewport.
    (int TargetHeight, int LayoutY, int Height, int OpenGlY)[] cases =
    [
        (720, 492, 207, 21),
        (1080, 738, 310, 32),
        (360, 246, 104, 10),
        (720, 0, 720, 0),
        (720, 20, 120, 580)
    ];
    using OpenGlRenderer openGl = new();
    using VulkanRenderer vulkan = new();
    foreach (IRenderer renderer in new IRenderer[] { openGl, vulkan })
    {
        GraphicsDevice device = new(renderer, Vector4.Zero);
        foreach (var item in cases)
        {
            int expectedY = renderer.Backend == GraphicsBackend.OpenGL ? item.OpenGlY : item.LayoutY;
            int framebufferY = device.ResolveFramebufferViewportY(item.LayoutY, item.Height, item.TargetHeight);
            Check(framebufferY == expectedY, $"{renderer.Backend}: expected viewport Y {expectedY}, got {framebufferY}.");
            Check(device.ResolveLayoutViewportY(framebufferY, item.Height, item.TargetHeight) == item.LayoutY,
                $"{renderer.Backend}: underwater/background sprite layout moved vertically.");
        }
    }
}

static void TestStartupDirectory()
{
    string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DemoGame01-startup-test"));
    using GameEditorGame editor = new(GraphicsBackend.OpenGL, $"\"{directory}\"");
    Check(editor.ProjectDirectory == directory, "The startup project was replaced by the default directory.");
    Check(CaptureDirectoryInput(editor) == directory, "The overlay captured a stale default directory.");

    using GameEditorGame defaultEditor = new(GraphicsBackend.OpenGL);
    Check(CaptureDirectoryInput(defaultEditor) == GameProjectStore.CreateDefaultProjectDirectory(),
        "Starting without a project should retain the default directory.");
}

static void TestDeferredSceneSwitch()
{
    string root = Path.Combine(Path.GetTempPath(), "dw-switch-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        GameProject project = new() { EditorScene = "scenes/a.scene.json", DefaultScene = "scenes/a.scene.json",
            Scenes = ["scenes/a.scene.json", "scenes/b.scene.json"] };
        GameProjectStore.Save(root, project);
        GameProjectStore.SaveScene(root, "scenes/b.scene.json", new GameProjectScene { Name = "B" });
        using GameEditorGame editor = new(GraphicsBackend.OpenGL, root);
        using VulkanRenderer renderer = new();
        typeof(Zhengyan.DigitalWife.Mmd.Game.Game).GetProperty("GraphicsDevice")!
            .SetValue(editor, new GraphicsDevice(renderer, Vector4.Zero));
        typeof(GameEditorGame).GetProperty(nameof(GameEditorGame.Project))!.SetValue(editor, project);
        for (int i = 0; i < 8; i++)
        {
            string previous = editor.ActiveScenePath;
            string next = i % 2 == 0 ? "scenes/b.scene.json" : "scenes/a.scene.json";
            editor.SwitchScene(next);
            Check(editor.ActiveScenePath == previous, "SwitchScene mutated the scene while UI drawing was still active.");
            typeof(GameEditorGame).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [default(Zhengyan.DigitalWife.Mmd.Game.GameTime)]);
            Check(editor.ActiveScenePath == next, "The queued scene change was not applied at update time.");
        }
    }
    finally { Directory.Delete(root, recursive: true); }
}

static void TestMultiSceneExport()
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dw-editor-tests-" + Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(root);
    try
    {
        string projectDirectory = Path.Combine(root, "DemoGame01");
        GameProject project = new()
        {
            Name = "Multi-scene export regression",
            Scenes = ["scenes/main.scene.json", "scenes/next.scene.json", "scenes/scene2.scene.json", "scenes/scene3.scene.json"]
        };
        GameProjectStore.Save(projectDirectory, project);
        foreach (string scenePath in project.Scenes)
        {
            string scriptPath = "scripts/" + Path.GetFileNameWithoutExtension(scenePath) + ".csx";
            File.WriteAllText(Path.Combine(projectDirectory, scriptPath), "return null;");
            GameProjectStore.SaveScene(projectDirectory, scenePath, new GameProjectScene
            {
                Name = scenePath,
                LoadingScripts = [new ScriptBinding { Path = scriptPath }]
            });
        }
        ExportAndCheck(projectDirectory, Path.Combine(root, "DemoGame01.dwgame"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void TestWebStaticExport()
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dw-web-editor-tests-" + Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(root);
    try
    {
        string projectDirectory = Path.Combine(root, "WebProject");
        GameProject project = new()
        {
            Name = "Web static export regression",
            Scenes = ["scenes/main.scene.json"],
            DefaultScene = "scenes/main.scene.json",
            EditorScene = "scenes/main.scene.json"
        };
        GameProjectStore.Save(projectDirectory, project);
        GameProjectStore.SaveScene(projectDirectory, "scenes/main.scene.json", new GameProjectScene { Name = "Main" });
        string outputDirectory = Path.Combine(root, "WebProject-output");

        using GameEditorGame editor = new(GraphicsBackend.OpenGL, projectDirectory);
        typeof(GameEditorGame).GetProperty(nameof(GameEditorGame.Project))!
            .SetValue(editor, GameProjectStore.Load(projectDirectory));
        editor.SetProjectDirectory(projectDirectory);
        WebGamePackageResult result = editor.ExportWebStaticDirectory(outputDirectory);

        Check(result.OutputDirectory == Path.GetFullPath(outputDirectory), "Web export returned an unexpected output directory.");
        Check(File.Exists(result.ManifestPath), "Web export did not write web.manifest.json.");
        Check(File.Exists(Path.Combine(outputDirectory, "game.project.json")), "Web export did not write game.project.json.");
        Check(File.Exists(Path.Combine(outputDirectory, "compiled", "web", "manifest.json")), "Web export did not preserve the Web script manifest.");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        Check(manifest.RootElement.GetProperty("runtime").GetString() == "web", "Web export manifest has the wrong runtime.");
        Check(manifest.RootElement.GetProperty("renderer").GetString() == "webgl2", "Web export manifest has the wrong renderer.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void TestWebPrecompileRemovesStaleAssembly()
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dw-web-precompile-tests-" + Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(root);
    try
    {
        GameProject project = new()
        {
            Name = "Web precompile regression",
            Scenes = ["scenes/main.scene.json"],
            DefaultScene = "scenes/main.scene.json",
            EditorScene = "scenes/main.scene.json"
        };
        GameProjectStore.Save(root, project);
        GameProjectStore.SaveScene(root, "scenes/main.scene.json", new GameProjectScene
        {
            Name = "Main",
            LoadingScripts = [new ScriptBinding { Path = "scripts/body.csx", Language = "csharp" }]
        });
        string sourcePath = Path.Combine(root, "scripts", "body.csx");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "return null;");

        WebScriptPrecompileResult first = WebCSharpScriptPrecompiler.Precompile(root, project);
        string assemblyPath = Path.Combine(root, "compiled", "web", "scripts", "body.dll");
        Check(first.Errors.Count == 0 && File.Exists(assemblyPath), "The compatible Web script did not produce an assembly.");

        File.WriteAllText(sourcePath, "UnknownWebSymbol = 1;");
        WebScriptPrecompileResult second = WebCSharpScriptPrecompiler.Precompile(root, project);
        Check(second.Errors.Count > 0 && !File.Exists(assemblyPath), "A failed Web compile left a stale assembly behind.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void TestWebAbiPrecompile()
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dw-web-abi-tests-" + Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(root);
    try
    {
        GameProject project = new()
        {
            Name = "Web ABI regression",
            Scenes = ["scenes/main.scene.json"],
            DefaultScene = "scenes/main.scene.json",
            EditorScene = "scenes/main.scene.json"
        };
        GameProjectStore.Save(root, project);
        GameProjectStore.SaveScene(root, "scenes/main.scene.json", new GameProjectScene
        {
            Name = "Main",
            Entities =
            [
                new GameEntity
                {
                    Id = "avatar",
                    Type = "pmx_model",
                    Scripts = [new ScriptBinding { Path = "scripts/body.csx", Language = "csharp" }]
                }
            ]
        });
        string sourcePath = Path.Combine(root, "scripts", "body.csx");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, """
            if (IsGuiEvent && GuiEventName == "clicked")
            {
                Audio.Play("click");
                Entity.ApplyMotion("assets/motions/click.vmd");
            }
            if (IsSpeechEvent && SpeechCallbackName == "after_speak")
            {
                Audio.Stop("click");
            }
            """);

        WebScriptPrecompileResult result = WebCSharpScriptPrecompiler.Precompile(root, project);
        Check(result.Errors.Count == 0, $"Web ABI globals failed to compile: {string.Join(Environment.NewLine, result.Errors)}");
        Check(result.Entries.Count == 1, "Web ABI regression did not emit the script assembly.");
        Check(File.Exists(Path.Combine(root, "compiled", "web", "scripts", "body.dll")), "Web ABI regression assembly is missing.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static string CaptureDirectoryInput(GameEditorGame editor)
{
    GameEditorOverlayComponent overlay = new(editor);
    return (string)typeof(GameEditorOverlayComponent)
        .GetField("_projectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
}

static void ExportAndCheck(string projectDirectory, string outputPath)
{
    using GameEditorGame editor = new(GraphicsBackend.OpenGL, projectDirectory);
    // Match startup ordering without initializing a native window or loading models.
    string directoryInput = CaptureDirectoryInput(editor);
    typeof(GameEditorGame).GetProperty(nameof(GameEditorGame.Project))!
        .SetValue(editor, GameProjectStore.Load(projectDirectory));
    Check(directoryInput == Path.GetFullPath(projectDirectory), "Save would redirect this project to the default directory.");
    editor.SetProjectDirectory(directoryInput); // The Project panel's Save action.
    GameProjectPackageBuildResult result = editor.ExportProjectPackage(outputPath);
    Check(result.TotalBytes > 0, "Export produced an empty package.");

    using GameProjectPackageSession extracted = GameProjectPackage.OpenOrExtract(result.OutputPath,
        new GameProjectPackageOpenOptions
        {
            UsePersistentCache = false,
            TempRootDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, "extracted-" + Guid.NewGuid().ToString("N")),
            SaveDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, "test-saves")
        });
    GameProject roundTrip = GameProjectStore.Load(extracted.ProjectDirectory);
    Check(roundTrip.Scenes.SequenceEqual(editor.Project.Scenes), "The package lost scene registrations.");
    HashSet<string> expectedScripts = new(StringComparer.OrdinalIgnoreCase);
    foreach (string scenePath in roundTrip.Scenes)
    {
        GameProjectScene scene = GameProjectStore.LoadScene(extracted.ProjectDirectory, scenePath);
        Check(scene.Name == GameProjectStore.LoadScene(projectDirectory, scenePath).Name, $"Scene changed: {scenePath}");
        foreach (ScriptBinding binding in scene.LoadingScripts.Concat(scene.Entities.SelectMany(entity => entity.Scripts)))
        {
            if (binding.Enabled && binding.Language is "csharp" or "cs" or "csx")
                expectedScripts.Add(binding.Path.Replace('\\', '/'));
        }
    }
    foreach (string platform in new[] { "android", "desktop" })
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(extracted.ProjectDirectory, "compiled", platform, "manifest.json")));
        HashSet<string> actualScripts = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entry in manifest.RootElement.GetProperty("scripts").EnumerateArray())
        {
            actualScripts.Add(entry.GetProperty("Source").GetString()!);
            string assemblyPath = Path.Combine(extracted.ProjectDirectory, entry.GetProperty("Assembly").GetString()!);
            Check(File.Exists(assemblyPath), $"Missing precompiled {platform} assembly: {assemblyPath}");
        }
        Check(actualScripts.SetEquals(expectedScripts), $"{platform} precompile omitted a scene's scripts.");
    }
    using (JsonDocument webManifest = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(extracted.ProjectDirectory, "compiled", "web", "manifest.json"))))
    {
        HashSet<string> webScripts = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entry in webManifest.RootElement.GetProperty("scripts").EnumerateArray())
        {
            string source = entry.GetProperty("Source").GetString()!;
            webScripts.Add(source);
            string assemblyPath = Path.Combine(extracted.ProjectDirectory, entry.GetProperty("Assembly").GetString()!);
            Check(File.Exists(assemblyPath), $"Missing precompiled web assembly: {assemblyPath}");
        }
        Check(webManifest.RootElement.TryGetProperty("errors", out JsonElement errors), "Web precompile manifest omitted diagnostics.");
        Check(webScripts.Count <= expectedScripts.Count, "Web precompile emitted more scripts than the project contains.");
        Check(errors.GetArrayLength() == 0 || webScripts.Count < expectedScripts.Count,
            "Web precompile reported errors without identifying an omitted script.");
    }
    Console.WriteLine($"Export verified: {roundTrip.Scenes.Count} scenes, {expectedScripts.Count} scripts per platform, {result.TotalBytes:N0} bytes.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
