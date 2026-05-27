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

        NormalizeScenes(project);
        project.Scene = LoadScene(projectDirectory, project.EditorScene);

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

        NormalizeScenes(project);

        string projectPath = Path.Combine(projectDirectory, ProjectFileName);
        File.WriteAllText(projectPath, JsonSerializer.Serialize(project, JsonOptions));
        SaveScene(projectDirectory, project.EditorScene, project.Scene);
    }

    public static void SaveScene(string projectDirectory, string scenePath, GameProjectScene scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(scene);

        string normalizedScenePath = NormalizeScenePath(scenePath);
        string fullScenePath = GameProjectPath.ToAbsolute(projectDirectory, normalizedScenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullScenePath)!);
        File.WriteAllText(fullScenePath, JsonSerializer.Serialize(scene, JsonOptions));
    }

    public static string CreateUniqueScenePath(string projectDirectory, GameProject project, string sceneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);

        string stem = ToSafeFileStem(sceneName);
        HashSet<string> existing = project.Scenes
            .Select(NormalizeScenePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; ; i++)
        {
            string suffix = i == 0 ? string.Empty : $"_{i + 1}";
            string candidate = NormalizeScenePath($"scenes/{stem}{suffix}.scene.json");
            string fullPath = GameProjectPath.ToAbsolute(projectDirectory, candidate);
            if (!existing.Contains(candidate) && !File.Exists(fullPath))
            {
                return candidate;
            }
        }
    }

    public static void DeleteScene(string projectDirectory, string scenePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        string fullScenePath = GameProjectPath.ToAbsolute(projectDirectory, NormalizeScenePath(scenePath));
        string projectRoot = Path.GetFullPath(projectDirectory);
        string fullRoot = projectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(fullScenePath);
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete scene outside project directory: {candidate}");
        }

        if (File.Exists(candidate))
        {
            File.Delete(candidate);
        }
    }

    public static string NormalizeScenePath(string scenePath)
    {
        string normalized = string.IsNullOrWhiteSpace(scenePath)
            ? $"scenes/{MainSceneFileName}"
            : scenePath.Trim().Trim('"').Replace('\\', '/');

        return string.IsNullOrWhiteSpace(normalized)
            ? $"scenes/{MainSceneFileName}"
            : normalized;
    }

    public static void NormalizeScenes(GameProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        List<string> scenes = project.Scenes
            .Select(NormalizeScenePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string defaultScene = NormalizeScenePath(project.DefaultScene);
        if (scenes.Count == 0)
        {
            scenes.Add(defaultScene);
        }

        if (!scenes.Contains(defaultScene, StringComparer.OrdinalIgnoreCase))
        {
            defaultScene = scenes[0];
        }

        string editorScene = NormalizeScenePath(project.EditorScene);
        if (!scenes.Contains(editorScene, StringComparer.OrdinalIgnoreCase))
        {
            editorScene = defaultScene;
        }

        project.Scenes = scenes;
        project.DefaultScene = defaultScene;
        project.EditorScene = editorScene;
    }

    public static string CreateDefaultProjectDirectory()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "GameEditorProjects", "DemoGame"));
    }

    private static string ToSafeFileStem(string value)
    {
        string stem = string.IsNullOrWhiteSpace(value) ? "scene" : value.Trim();
        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(ch, '_');
        }

        stem = stem.Replace(' ', '_');
        return string.IsNullOrWhiteSpace(stem) ? "scene" : stem;
    }
}
