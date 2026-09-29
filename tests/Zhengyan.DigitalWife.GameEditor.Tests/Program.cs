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
    ("Save and export all scenes after opening a project", TestMultiSceneExport)
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
    Console.WriteLine($"Export verified: {roundTrip.Scenes.Count} scenes, {expectedScripts.Count} scripts per platform, {result.TotalBytes:N0} bytes.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
