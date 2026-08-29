using Android.Content;
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

    public static AndroidGameProjectLoadResult Load(Activity activity, Intent? intent)
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

        AndroidUri? data = ResolveIntentUri(intent);
        if (data is not null)
        {
            string? copiedPath = TryCopyContentUri(activity, data);
            if (!string.IsNullOrWhiteSpace(copiedPath))
            {
                return copiedPath;
            }

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

    private static AndroidUri? ResolveIntentUri(Intent? intent)
    {
        if (intent?.Data is { } data)
        {
            return data;
        }

        if (intent?.ClipData is { ItemCount: > 0 } clipData
            && clipData.GetItemAt(0)?.Uri is { } clipUri)
        {
            return clipUri;
        }

        if (intent is null)
        {
            return null;
        }

        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
#pragma warning disable CA1416 // Guarded by the Android 13 runtime version check above.
            return intent.GetParcelableExtra(
                Intent.ExtraStream,
                Java.Lang.Class.FromType(typeof(AndroidUri))) as AndroidUri;
#pragma warning restore CA1416
        }

#pragma warning disable CS0618, CA1422 // Type-safe overload is unavailable below Android 13.
        return intent.GetParcelableExtra(Intent.ExtraStream) as AndroidUri;
#pragma warning restore CS0618, CA1422
    }

    private static string? TryCopyContentUri(Activity activity, AndroidUri data)
    {
        if (!string.Equals(data.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string cacheRoot = activity.CacheDir?.AbsolutePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return null;
        }

        Directory.CreateDirectory(cacheRoot);
        string extension = data.ToString()?.EndsWith(GameProjectPackage.PackageExtension, StringComparison.OrdinalIgnoreCase) == true
            ? GameProjectPackage.PackageExtension
            : ".dwgame";
        string targetPath = Path.Combine(cacheRoot, "imported-project" + extension);

        using Stream? source = activity.ContentResolver?.OpenInputStream(data);
        if (source is null)
        {
            return null;
        }

        using FileStream target = File.Create(targetPath);
        source.CopyTo(target);
        return targetPath;
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
