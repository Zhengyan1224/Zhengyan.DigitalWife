namespace Zhengyan.DigitalWife.Samples.DigitalHuman;

internal sealed class SamplePathResolver
{
    private readonly string _baseDirectory;
    private readonly string? _repositoryRoot;

    public SamplePathResolver(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _repositoryRoot = FindRepositoryRoot(_baseDirectory);
    }

    public string BaseDirectory => _baseDirectory;

    public string? RepositoryRoot => _repositoryRoot;

    public string ResolveRequiredFile(string path)
    {
        foreach (string candidate in EnumerateFileCandidates(path))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Required file was not found: {path}", path);
    }

    public string? ResolveOptionalFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string candidate in EnumerateFileCandidates(path))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string ResolveDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Directory path is required.", nameof(path));
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }

        return Path.GetFullPath(Path.Combine(_baseDirectory, path));
    }

    public string ResolveRequiredDirectory(string path)
    {
        string directory = ResolveDirectory(path);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Required directory was not found: {directory}");
        }

        return directory;
    }

    public string? ResolveOptionalDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return ResolveDirectory(path);
    }

    private IEnumerable<string> EnumerateFileCandidates(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        if (Path.IsPathRooted(path))
        {
            yield return Path.GetFullPath(path);
            yield break;
        }

        if (IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            yield return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }

        yield return Path.GetFullPath(Path.Combine(_baseDirectory, path));

        if (!IsRepositoryScopedPath(path) && !string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            yield return Path.GetFullPath(Path.Combine(_repositoryRoot, path));
        }
    }

    private static bool IsRepositoryScopedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("samples/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("src/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "samples"))
                && Directory.Exists(Path.Combine(directory.FullName, "assets"))
                && Directory.Exists(Path.Combine(directory.FullName, "models")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
