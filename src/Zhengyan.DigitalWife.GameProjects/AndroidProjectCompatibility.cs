namespace Zhengyan.DigitalWife.GameProjects;

public enum AndroidCompatibilitySeverity
{
    Warning,
    Error
}

public sealed record AndroidCompatibilityIssue(
    string Code,
    AndroidCompatibilitySeverity Severity,
    string Message,
    string? ScenePath = null,
    string? EntityName = null);

public sealed class AndroidCompatibilityReport
{
    public required IReadOnlyList<AndroidCompatibilityIssue> Issues { get; init; }

    public bool CanPublish => Issues.All(issue => issue.Severity != AndroidCompatibilitySeverity.Error);

    public int ErrorCount => Issues.Count(issue => issue.Severity == AndroidCompatibilitySeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == AndroidCompatibilitySeverity.Warning);

    public string ToStatusMessage()
    {
        string summary = CanPublish
            ? $"Android compatibility check passed with {WarningCount} warning(s)."
            : $"Android compatibility check failed with {ErrorCount} error(s) and {WarningCount} warning(s).";

        if (Issues.Count == 0)
        {
            return "Android compatibility check passed. The project uses only supported C# scripts and game-window features.";
        }

        IEnumerable<string> details = Issues.Select(issue =>
        {
            string location = issue.ScenePath is null
                ? string.Empty
                : issue.EntityName is null
                    ? $" [{issue.ScenePath}]"
                    : $" [{issue.ScenePath} / {issue.EntityName}]";
            return $"{issue.Severity}: {issue.Message}{location}";
        });

        return summary + Environment.NewLine + string.Join(Environment.NewLine, details);
    }
}

public static class AndroidProjectCompatibility
{
    private static readonly HashSet<string> SupportedScriptLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "csharp",
        "cs",
        "csx"
    };

    public static AndroidCompatibilityReport Analyze(string projectDirectory, GameProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);

        List<AndroidCompatibilityIssue> issues = [];
        AnalyzeProjectSettings(project, issues);

        GameProjectStore.NormalizeScenes(project);
        foreach (string scenePath in project.Scenes)
        {
            GameProjectScene scene;
            try
            {
                scene = GameProjectStore.LoadScene(projectDirectory, scenePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                issues.Add(new AndroidCompatibilityIssue(
                    "ANDROID_SCENE_LOAD_FAILED",
                    AndroidCompatibilitySeverity.Error,
                    $"Scene cannot be read for Android publishing: {ex.Message}",
                    scenePath));
                continue;
            }

            AnalyzeScripts(scene.LoadingScripts, scenePath, null, issues);
            foreach (GameEntity entity in scene.Entities)
            {
                AnalyzeScripts(entity.Scripts, scenePath, entity.Name, issues);
            }
        }

        return new AndroidCompatibilityReport { Issues = issues };
    }

    private static void AnalyzeProjectSettings(
        GameProject project,
        ICollection<AndroidCompatibilityIssue> issues)
    {
        if (string.Equals(project.ScriptRuntime.PreferredLanguage, "python", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_PREFERRED_SCRIPT_LANGUAGE",
                AndroidCompatibilitySeverity.Warning,
                "Python is selected as the preferred script language. Android publishing creates and runs C# scripts only."));
        }

        if (project.Window.DesktopSpriteMode
            || project.Window.DesktopSpriteClickThrough
            || project.Window.DesktopSpriteTrayEnabled)
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_DESKTOP_SPRITE_IGNORED",
                AndroidCompatibilitySeverity.Warning,
                "Desktop sprite, click-through, drag and system-tray settings are ignored by the Android GamePlayer."));
        }

        if (project.Runtime.UseOpenCL)
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_OPENCL_IGNORED",
                AndroidCompatibilitySeverity.Warning,
                "OpenCL is not used by the Android GamePlayer. OpenGL ES uses CPU skinning until a mobile GPU path is available; Vulkan may use Vulkan Compute."));
        }
    }

    private static void AnalyzeScripts(
        IEnumerable<ScriptBinding> scripts,
        string scenePath,
        string? entityName,
        ICollection<AndroidCompatibilityIssue> issues)
    {
        foreach (ScriptBinding script in scripts.Where(script => script.Enabled))
        {
            string language = NormalizeScriptLanguage(script);
            if (SupportedScriptLanguages.Contains(language))
            {
                continue;
            }

            string displayLanguage = string.IsNullOrWhiteSpace(language) ? "unknown" : language;
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_SCRIPT_LANGUAGE_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                $"Enabled script '{script.Path}' uses unsupported language '{displayLanguage}'. Android supports C# scripts only.",
                scenePath,
                entityName));
        }
    }

    private static string NormalizeScriptLanguage(ScriptBinding script)
    {
        string language = script.Language?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language;
        }

        return Path.GetExtension(script.Path).ToLowerInvariant() switch
        {
            ".cs" or ".csx" => "csharp",
            ".py" => "python",
            _ => string.Empty
        };
    }
}
