using Android.Content;
using Android.Database;
using Android.Provider;
using AndroidUri = Android.Net.Uri;
using Android.Util;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidGameProjectLoadResult : IDisposable
{
    public GameProject? Project { get; init; }

    public GameProjectPackageSession? PackageSession { get; init; }

    public string ProjectDirectory => PackageSession?.ProjectDirectory ?? string.Empty;

    public AndroidCompatibilityReport? Compatibility { get; init; }

    public string Source { get; init; } = string.Empty;

    public string? Error { get; init; }

    public bool Succeeded => Project is not null && Error is null;

    public void Dispose()
    {
        PackageSession?.Dispose();
    }
}

internal static class AndroidGameProjectLoader
{
    private const string LogTag = "ZhengyanGamePlayer";
    private const string ProjectPathExtra = "zhengyan.project_path";

    public static bool HasProjectInput(Activity activity, Intent? intent)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!string.IsNullOrWhiteSpace(intent?.GetStringExtra(ProjectPathExtra))
            || ResolveIntentUris(intent).Count > 0)
        {
            return true;
        }

        string filesRoot = activity.FilesDir?.AbsolutePath ?? string.Empty;
        string bundledProject = Path.Combine(filesRoot, "GameProject");
        return Directory.Exists(bundledProject) || File.Exists(bundledProject);
    }

    public static AndroidGameProjectLoadResult Load(Activity activity, Intent? intent, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(activity);

        try
        {
            AndroidBundledResourceStore.EnsureExtracted(activity);

            string? inputPath = ResolveInputPath(activity, intent);
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return Failure("No game project was supplied. Open a project directory or .dwgame package with this app, or copy it to the app's files/GameProject directory.");
            }

            GameProjectPackageSession session = GameProjectPackage.OpenOrExtract(
                inputPath,
                new GameProjectPackageOpenOptions
                {
                    Password = password,
                    SaveDirectory = Path.Combine(activity.FilesDir?.AbsolutePath ?? activity.CacheDir?.AbsolutePath ?? string.Empty, "saves"),
                    TempRootDirectory = Path.Combine(activity.CacheDir?.AbsolutePath ?? string.Empty, "GamePackages"),
                    PersistentCacheDirectory = Path.Combine(activity.FilesDir?.AbsolutePath ?? activity.CacheDir?.AbsolutePath ?? string.Empty, "PackageCache"),
                    UsePersistentCache = true
                });

            GameProject project = GameProjectStore.Load(session.ProjectDirectory);
            AndroidCompatibilityReport compatibility = AndroidProjectCompatibility.Analyze(session.ProjectDirectory, project);
            string source = session.SourcePath;

            Log.Info(LogTag, $"Loaded project '{project.Name}' from {source}; scenes={project.Scenes.Count}; androidErrors={compatibility.ErrorCount}; warnings={compatibility.WarningCount}");
            foreach (AndroidCompatibilityIssue issue in compatibility.Issues)
            {
                Log.Warn(LogTag, $"{issue.Code}: {issue.Message}");
            }

            return new AndroidGameProjectLoadResult
            {
                Project = project,
                PackageSession = session,
                Compatibility = compatibility,
                Source = source
            };
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Game project load failed: {ex}");
            return Failure(ex.Message);
        }
    }

    private static string? ResolveInputPath(Activity activity, Intent? intent)
    {
        string? explicitPath = intent?.GetStringExtra(ProjectPathExtra);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return NormalizeLocalPath(explicitPath);
        }

        IReadOnlyList<AndroidUri> intentUris = ResolveIntentUris(intent);
        if (intentUris.Count > 0)
        {
            string? copiedPath = TryCopyContentUris(activity, intentUris);
            if (!string.IsNullOrWhiteSpace(copiedPath))
            {
                return copiedPath;
            }

            AndroidUri data = intentUris[0];
            string? localPath = data.Path;
            if (!string.IsNullOrWhiteSpace(localPath) && (data.Scheme is null || data.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase)))
            {
                return NormalizeLocalPath(localPath);
            }
        }

        string filesRoot = activity.FilesDir?.AbsolutePath ?? string.Empty;
        string bundledProject = Path.Combine(filesRoot, "GameProject");
        if (Directory.Exists(bundledProject) || File.Exists(bundledProject))
        {
            return bundledProject;
        }

        return null;
    }

    private static IReadOnlyList<AndroidUri> ResolveIntentUris(Intent? intent)
    {
        List<AndroidUri> uris = [];
        if (intent?.Data is { } data)
        {
            uris.Add(data);
        }

        if (intent?.ClipData is { ItemCount: > 0 } clipData)
        {
            for (int i = 0; i < clipData.ItemCount; i++)
            {
                if (clipData.GetItemAt(i)?.Uri is { } clipUri
                    && !uris.Any(existing => string.Equals(existing.ToString(), clipUri.ToString(), StringComparison.Ordinal)))
                {
                    uris.Add(clipUri);
                }
            }
        }

        if (intent is null)
        {
            return uris;
        }

        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
#pragma warning disable CA1416 // Guarded by the Android 13 runtime version check above.
            if (intent.GetParcelableExtra(
                Intent.ExtraStream,
                Java.Lang.Class.FromType(typeof(AndroidUri))) is AndroidUri streamUri
                && !uris.Any(existing => string.Equals(existing.ToString(), streamUri.ToString(), StringComparison.Ordinal)))
            {
                uris.Add(streamUri);
            }
#pragma warning restore CA1416
        }

#pragma warning disable CS0618, CA1422 // Type-safe overload is unavailable below Android 13.
        if (intent.GetParcelableExtra(Intent.ExtraStream) is AndroidUri legacyUri
            && !uris.Any(existing => string.Equals(existing.ToString(), legacyUri.ToString(), StringComparison.Ordinal)))
        {
            uris.Add(legacyUri);
        }
#pragma warning restore CS0618, CA1422
        return uris;
    }

    private static string? TryCopyContentUris(Activity activity, IReadOnlyList<AndroidUri> uris)
    {
        if (uris.Count == 0 || uris.Any(uri => !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string cacheRoot = activity.CacheDir?.AbsolutePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return null;
        }

        Directory.CreateDirectory(cacheRoot);
        string importRoot = Path.Combine(cacheRoot, "imported-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(importRoot);
        List<string> copiedPaths = [];
        for (int index = 0; index < uris.Count; index++)
        {
            AndroidUri uri = uris[index];
            string fileName = ResolveDisplayName(activity, uri);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = uri.LastPathSegment ?? string.Empty;
            }

            fileName = SanitizeFileName(fileName);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                fileName += index == 0 && uris.Count == 1 ? GameProjectPackage.PackageExtension : $"{GameProjectPackage.PackageExtension}.{index + 1:000}";
            }

            string targetPath = Path.Combine(importRoot, fileName);
            if (File.Exists(targetPath))
            {
                targetPath = Path.Combine(importRoot, $"part-{index + 1:000}{Path.GetExtension(fileName)}");
            }

            using Stream? source = activity.ContentResolver?.OpenInputStream(uri);
            if (source is null) return null;
            using FileStream target = File.Create(targetPath);
            source.CopyTo(target);
            copiedPaths.Add(targetPath);
        }

        string? firstPart = copiedPaths.FirstOrDefault(path => Path.GetExtension(path).Length == 4 && Path.GetExtension(path)[1..].All(char.IsDigit));
        return firstPart ?? copiedPaths.FirstOrDefault();
    }

    private static string ResolveDisplayName(Activity activity, AndroidUri uri)
    {
        try
        {
            string[] projection = [IOpenableColumns.DisplayName];
            using ICursor? cursor = activity.ContentResolver?.Query(uri, projection, null, null, null);
            if (cursor?.MoveToFirst() == true && cursor.GetString(0) is { } name)
            {
                return name;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string SanitizeFileName(string value)
    {
        string name = Path.GetFileName(value.Trim().Trim('"'));
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "part.dwgame" : name;
    }

    private static string NormalizeLocalPath(string path)
    {
        return path.Trim().Trim('"');
    }

    private static AndroidGameProjectLoadResult Failure(string message)
    {
        return new AndroidGameProjectLoadResult { Error = message };
    }
}
