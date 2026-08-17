namespace Zhengyan.DigitalWife.GameProjects;

public enum AndroidCompatibilitySeverity
{
    Warning,
    Error
}

public enum AndroidCompatibilityStatus
{
    Supported,
    Degraded,
    Rejected
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

    public AndroidCompatibilityStatus Status => ErrorCount > 0
        ? AndroidCompatibilityStatus.Rejected
        : WarningCount > 0
            ? AndroidCompatibilityStatus.Degraded
            : AndroidCompatibilityStatus.Supported;

    public int ErrorCount => Issues.Count(issue => issue.Severity == AndroidCompatibilitySeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == AndroidCompatibilitySeverity.Warning);

    public string ToStatusMessage()
    {
        string summary = Status switch
        {
            AndroidCompatibilityStatus.Supported => "Android compatibility check passed.",
            AndroidCompatibilityStatus.Degraded => $"Android compatibility check passed in degraded mode with {WarningCount} warning(s).",
            _ => $"Android compatibility check failed with {ErrorCount} error(s) and {WarningCount} warning(s)."
        };

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
            AnalyzeSceneFeatures(scene, scenePath, issues);
            foreach (GameEntity entity in scene.Entities)
            {
                AnalyzeScripts(entity.Scripts, scenePath, entity.Name, issues);
                AnalyzeEntity(entity, scenePath, issues);
            }
        }

        return new AndroidCompatibilityReport { Issues = issues };
    }

    private static void AnalyzeSceneFeatures(
        GameProjectScene scene,
        string scenePath,
        ICollection<AndroidCompatibilityIssue> issues)
    {
        if (scene.RenderTextures.Any(texture => texture.Enabled))
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_RENDER_TEXTURE_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "Android supports main and viewport cameras, but render-texture camera targets are not implemented yet.",
                scenePath));
        }

        if (scene.GuiControls.Count != 0 || scene.ContextMenus.Count != 0)
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_GUI_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "GUI controls and context menus are not implemented by the current Android runtime.",
                scenePath));
        }

        if (scene.Sprites.Any(sprite => sprite.Visible))
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_GAME_SPRITE_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "Game-scene foreground/background sprites are not implemented by the current Android runtime.",
                scenePath));
        }

        if (scene.Audio.Any(audio => audio.PlayOnStart))
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_AUDIO_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "Scene audio is not implemented by the current Android runtime.",
                scenePath));
        }

        if (scene.LoadingScripts.Any(script => script.Enabled)
            || !string.IsNullOrWhiteSpace(scene.LoadingScreen.BackgroundImagePath))
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_LOADING_SCREEN_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "Loading-screen images and loading scripts are not implemented by the current Android runtime.",
                scenePath));
        }

        if (scene.Skybox.Enabled)
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_SKYBOX_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                "Skybox rendering is not implemented by the current Android runtime.",
                scenePath));
        }
    }

    private static void AnalyzeEntity(
        GameEntity entity,
        string scenePath,
        ICollection<AndroidCompatibilityIssue> issues)
    {
        string type = entity.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        if (type is "point_light" or "pointlight" or "spot_light" or "spotlight")
        {
            bool castsShadows = type is "point_light" or "pointlight"
                ? entity.PointLight.CastShadows
                : entity.SpotLight.CastShadows;
            if (castsShadows)
            {
                issues.Add(new AndroidCompatibilityIssue(
                    "ANDROID_LOCAL_LIGHT_SHADOW_DEGRADED",
                    AndroidCompatibilitySeverity.Warning,
                    "Point and spot lights render on Android, but their shadow maps are not implemented yet.",
                    scenePath,
                    entity.Name));
            }
            return;
        }

        if (type is "empty" or "game_object" or "gameobject")
        {
            return;
        }

        if (type is not "pmx_model")
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_ENTITY_TYPE_UNSUPPORTED",
                AndroidCompatibilitySeverity.Error,
                $"Entity type '{entity.Type}' is not implemented by the current Android runtime.",
                scenePath,
                entity.Name));
            return;
        }

        if (entity.PointLight.CastShadows || entity.SpotLight.CastShadows)
        {
            issues.Add(new AndroidCompatibilityIssue(
                "ANDROID_LOCAL_LIGHT_SHADOW_DEGRADED",
                AndroidCompatibilitySeverity.Warning,
                "Android supports directional PMX shadows, but point/spot-light shadow maps are not implemented yet.",
                scenePath,
                entity.Name));
        }
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
                issues.Add(new AndroidCompatibilityIssue(
                    "ANDROID_CSHARP_PRECOMPILE_REQUIRED",
                    AndroidCompatibilitySeverity.Error,
                    $"Enabled C# script '{script.Path}' must be precompiled by the future Android publisher; the current generic APK cannot execute source scripts.",
                    scenePath,
                    entityName));
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
