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

                string extractedRoot = Path.Combine(rootDirectory, AssetRoot);
                CopyAssetTree(assets, AssetRoot, extractedRoot);
                // Android's asset linker has used both slash conventions across
                // toolchain versions. Make the two runtime-critical files
                // explicit so a flattened asset listing cannot hide them.
                CopyKnownAsset(assets, "Resources/Particles/Sakura.png", Path.Combine(extractedRoot, "Particles", "Sakura.png"));
                CopyKnownAsset(assets, "Resources/Skybox/autumn_field_puresky.jpg", Path.Combine(extractedRoot, "Skybox", "autumn_field_puresky.jpg"));
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
            string normalizedChild = child.Replace('\\', '/');
            CopyAssetTree(assets, assetPath + "/" + normalizedChild, Path.Combine(destinationPath, normalizedChild.Replace('/', Path.DirectorySeparatorChar)));
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

    private static void CopyKnownAsset(AssetManager assets, string assetPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        string alternatePath = assetPath.Replace('/', '\\');
        try
        {
            CopyAssetFile(assets, assetPath, destinationPath);
        }
        catch (FileNotFoundException)
        {
            try
            {
                CopyAssetFile(assets, alternatePath, destinationPath);
            }
            catch (FileNotFoundException)
            {
                Log.Warn(LogTag, $"Android asset was not found: '{assetPath}'.");
            }
        }
    }
}
