using Android.App;
using Android.Content.Res;
using Android.Util;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidBundledResourceStore
{
    private const string LogTag = "ZhengyanGamePlayer";
    private const string AssetRoot = "Resources";
    private static readonly object SyncRoot = new();

    public static string? RootDirectory { get; private set; }

    public static void EnsureExtracted(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        lock (SyncRoot)
        {
            string filesDirectory = activity.FilesDir?.AbsolutePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filesDirectory))
            {
                return;
            }

            string rootDirectory = Path.Combine(filesDirectory, "EngineResources");
            try
            {
                AssetManager? assets = activity.Assets;
                if (assets is null)
                {
                    return;
                }

                CopyAssetTree(assets, AssetRoot, Path.Combine(rootDirectory, AssetRoot));
                RootDirectory = rootDirectory;
                Log.Info(LogTag, $"Android engine resources are available at '{rootDirectory}'.");
            }
            catch (Exception ex)
            {
                RootDirectory = Directory.Exists(rootDirectory) ? rootDirectory : null;
                Log.Warn(LogTag, $"Android engine resource extraction failed: {ex.Message}");
            }
        }
    }

    private static void CopyAssetTree(AssetManager assets, string assetPath, string destinationPath)
    {
        string[] children = assets.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            CopyAssetFile(assets, assetPath, destinationPath);
            return;
        }

        Directory.CreateDirectory(destinationPath);
        foreach (string child in children)
        {
            CopyAssetTree(assets, assetPath + "/" + child, Path.Combine(destinationPath, child));
        }
    }

    private static void CopyAssetFile(AssetManager assets, string assetPath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Asset destination has no parent directory."));
        using Stream source = assets.Open(assetPath, Access.Streaming)
            ?? throw new FileNotFoundException($"Android asset '{assetPath}' could not be opened.");
        using FileStream destination = File.Create(destinationPath);
        source.CopyTo(destination);
    }
}
