using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace Zhengyan.DigitalWife.GameProjects;

public static class GameProjectStore
{
    public const string ProjectFileName = "game.project.json";
    public const string MainSceneFileName = "main.scene.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static GameProject Load(string projectDirectory)
    {
        string projectPath = Path.Combine(projectDirectory, ProjectFileName);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: {projectPath}", projectPath);
        }

        GameProject project = JsonSerializer.Deserialize<GameProject>(File.ReadAllText(projectPath), JsonOptions)
            ?? throw new InvalidDataException($"Invalid project file: {projectPath}");

        project.Scene = LoadScene(projectDirectory, project.DefaultScene);

        return project;
    }

    public static GameProjectScene LoadScene(string projectDirectory, string scenePath)
    {
        string fullScenePath = GameProjectPath.ToAbsolute(projectDirectory, scenePath);
        if (!File.Exists(fullScenePath))
        {
            throw new FileNotFoundException($"Scene file not found: {fullScenePath}", fullScenePath);
        }

        return JsonSerializer.Deserialize<GameProjectScene>(File.ReadAllText(fullScenePath), JsonOptions)
            ?? throw new InvalidDataException($"Invalid scene file: {fullScenePath}");
    }

    public static void Save(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "models"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "audio"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "motions"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "particles"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "sprites"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "assets", "tts"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "scenes"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "scripts"));

        project.DefaultScene = "scenes/main.scene.json";
        if (!project.Scenes.Contains(project.DefaultScene, StringComparer.OrdinalIgnoreCase))
        {
            project.Scenes.Insert(0, project.DefaultScene);
        }

        string projectPath = Path.Combine(projectDirectory, ProjectFileName);
        string scenePath = Path.Combine(projectDirectory, "scenes", MainSceneFileName);
        File.WriteAllText(projectPath, JsonSerializer.Serialize(project, JsonOptions));
        File.WriteAllText(scenePath, JsonSerializer.Serialize(project.Scene, JsonOptions));
    }

    public static string CreateDefaultProjectDirectory()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "GameEditorProjects", "DemoGame"));
    }
}
