namespace Zhengyan.DigitalWife.GameProjects;

public static class GameProjectPath
{
    public static string ToProjectRelative(string projectDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(fullProjectDirectory, fullPath);
        return relative.Replace('\\', '/');
    }

    public static string ToAbsolute(string projectDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(projectDirectory, path));
    }

    public static string CopyAssetIntoProject(string projectDirectory, string sourcePath, string assetSubdirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string sourceFullPath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"Asset file not found: {sourceFullPath}", sourceFullPath);
        }

        string targetDirectory = Path.Combine(projectDirectory, "assets", assetSubdirectory);
        Directory.CreateDirectory(targetDirectory);

        string targetPath = MakeUniqueTargetPath(targetDirectory, Path.GetFileName(sourceFullPath));
        File.Copy(sourceFullPath, targetPath);
        return ToProjectRelative(projectDirectory, targetPath);
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
}
