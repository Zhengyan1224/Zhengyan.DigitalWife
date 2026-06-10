using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer;

public sealed class RuntimeSaveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _saveDirectory;

    internal RuntimeSaveStore(string saveDirectory)
    {
        _saveDirectory = Path.GetFullPath(saveDirectory);
        Directory.CreateDirectory(_saveDirectory);
    }

    public string SaveDirectory => _saveDirectory;

    public void WriteText(string fileName, string text)
    {
        string path = ResolveSavePath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text ?? string.Empty);
    }

    public string ReadText(string fileName, string fallback = "")
    {
        string path = ResolveSavePath(fileName);
        return File.Exists(path) ? File.ReadAllText(path) : fallback;
    }

    public void WriteJson<T>(string fileName, T value)
    {
        WriteText(fileName, JsonSerializer.Serialize(value, JsonOptions));
    }

    public T? ReadJson<T>(string fileName, T? fallback = default)
    {
        string path = ResolveSavePath(fileName);
        if (!File.Exists(path))
        {
            return fallback;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
    }

    public bool Exists(string fileName)
    {
        return File.Exists(ResolveSavePath(fileName));
    }

    public bool Delete(string fileName)
    {
        string path = ResolveSavePath(fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public string GetFullPath(string fileName)
    {
        return ResolveSavePath(fileName);
    }

    private string ResolveSavePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Save file name cannot be empty.", nameof(fileName));
        }

        string normalized = fileName.Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(_saveDirectory, normalized));
        string root = Path.GetFullPath(_saveDirectory);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Save path is outside the save directory: {fileName}");
        }

        return fullPath;
    }
}
