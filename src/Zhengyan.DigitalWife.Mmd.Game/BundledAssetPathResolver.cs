namespace Zhengyan.DigitalWife.Mmd.Game;

internal static class BundledAssetPathResolver
{
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
        string? current = AppContext.BaseDirectory;

        for (int depth = 0; depth < 3 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            yield return current;
            current = Directory.GetParent(current)?.FullName;
        }
    }
}
