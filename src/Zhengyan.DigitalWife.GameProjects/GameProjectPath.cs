namespace Zhengyan.DigitalWife.GameProjects;

public static class GameProjectPath
{
    public static string NormalizePathText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = path.Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
        {
            normalized = normalized[1..^1].Trim();
        }

        if (normalized.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(normalized, UriKind.Absolute, out Uri? fileUri)
            && fileUri.IsFile)
        {
            normalized = fileUri.LocalPath;
        }

        int prefixLength = 0;
        while (prefixLength < normalized.Length && IsPathPrefixNoise(normalized[prefixLength]))
        {
            prefixLength++;
        }

        if (prefixLength > 0)
        {
            normalized = normalized[prefixLength..];
        }

        if (!StartsWithVirtualPrefix(normalized))
        {
            int rootedPathStart = FindWindowsRootedPathStart(normalized);
            if (rootedPathStart > 0)
            {
                normalized = normalized[rootedPathStart..];
            }
        }

        return normalized.Trim().Trim('"');
    }

    public static string ToProjectRelative(string projectDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string normalizedPath = NormalizePathText(path);
        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        string fullPath = Path.GetFullPath(normalizedPath);
        string relative = Path.GetRelativePath(fullProjectDirectory, fullPath);
        return relative.Replace('\\', '/');
    }

    public static string ToAbsolute(string projectDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = NormalizePathText(path);
        if (normalizedPath.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(projectDirectory, normalizedPath["project:".Length..]));
        }

        if (normalizedPath.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, normalizedPath["app:".Length..]));
        }

        if (Path.IsPathRooted(normalizedPath))
        {
            return Path.GetFullPath(normalizedPath);
        }

        return Path.GetFullPath(Path.Combine(projectDirectory, normalizedPath));
    }

    public static string CopyAssetIntoProject(string projectDirectory, string sourcePath, string assetSubdirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string sourceFullPath = Path.GetFullPath(NormalizePathText(sourcePath));
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"Asset file not found: {sourceFullPath}", sourceFullPath);
        }

        if (IsPathInsideDirectory(sourceFullPath, projectDirectory))
        {
            return ToProjectRelative(projectDirectory, sourceFullPath);
        }

        string targetDirectory = Path.Combine(projectDirectory, "assets", assetSubdirectory);
        Directory.CreateDirectory(targetDirectory);

        string? existingPath = FindIdenticalFile(targetDirectory, sourceFullPath);
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            return ToProjectRelative(projectDirectory, existingPath);
        }

        string targetPath = MakeUniqueTargetPath(targetDirectory, Path.GetFileName(sourceFullPath));
        File.Copy(sourceFullPath, targetPath);
        return ToProjectRelative(projectDirectory, targetPath);
    }

    private static string? FindIdenticalFile(string directory, string sourceFullPath)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        string fileName = Path.GetFileName(sourceFullPath);
        foreach (string candidatePath in Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories))
        {
            if (AreFilesEquivalent(sourceFullPath, candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static string MakeUniqueTargetPath(string directory, string fileName)
    {
        string target = Path.Combine(directory, fileName);
        if (!File.Exists(target))
        {
            return target;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            target = Path.Combine(directory, $"{stem}_{i}{extension}");
            if (!File.Exists(target))
            {
                return target;
            }
        }
    }

    private static bool AreFilesEquivalent(string firstPath, string secondPath)
    {
        FileInfo first = new(firstPath);
        FileInfo second = new(secondPath);
        if (first.Length != second.Length)
        {
            return false;
        }

        const int bufferSize = 81920;
        using FileStream firstStream = File.OpenRead(first.FullName);
        using FileStream secondStream = File.OpenRead(second.FullName);
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];

        while (true)
        {
            int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            for (int i = 0; i < firstRead; i++)
            {
                if (firstBuffer[i] != secondBuffer[i])
                {
                    return false;
                }
            }
        }
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        string fullPath = Path.GetFullPath(path);
        string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithVirtualPrefix(string path)
    {
        return path.StartsWith("project:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("rt:", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindWindowsRootedPathStart(string path)
    {
        for (int i = 0; i + 2 < path.Length; i++)
        {
            if (char.IsAsciiLetter(path[i]) && path[i + 1] == ':' && (path[i + 2] == '\\' || path[i + 2] == '/'))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsPathPrefixNoise(char value)
    {
        return value is '\uFEFF' or '\u200B' or '\u200E' or '\u200F'
            or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069'
            or '?'
            || char.GetUnicodeCategory(value) == System.Globalization.UnicodeCategory.Format;
    }
}
