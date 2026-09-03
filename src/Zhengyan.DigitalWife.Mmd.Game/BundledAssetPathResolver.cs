namespace Zhengyan.DigitalWife.Mmd.Game;

public static class BundledAssetPathResolver
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> AdditionalRoots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a platform-owned directory containing the engine Resources folder.</summary>
    public static void RegisterSearchRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        lock (SyncRoot)
        {
            AdditionalRoots.Add(Path.GetFullPath(directory));
        }
    }

    public static string ResolveRequiredFile(string description, params string[] segments)
    {
        return TryResolveFile(segments)
            ?? throw new FileNotFoundException($"{description} was not found: {Path.Combine(segments)}", Path.Combine(segments));
    }

    public static string? TryResolveFile(params string[] segments)
    {
        string relativePath = Path.Combine(segments);

        foreach (string baseDirectory in EnumerateCandidateBaseDirectories())
        {
            string candidate = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateBaseDirectories()
    {
        string[] additionalRoots;
        lock (SyncRoot)
        {
            additionalRoots = AdditionalRoots.ToArray();
        }

        foreach (string root in additionalRoots)
        {
            yield return root;
        }

        string? current = AppContext.BaseDirectory;

        for (int depth = 0; depth < 3 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }
    }
}
