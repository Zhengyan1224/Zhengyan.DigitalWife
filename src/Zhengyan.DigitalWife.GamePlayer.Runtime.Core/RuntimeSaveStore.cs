using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>Script-visible synchronous state, persisted by the platform host.</summary>
public sealed partial class RuntimeSaveStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private long _revision, _persistedRevision;
    public string SaveDirectory => "save:";
    public long Revision => _revision;
    public bool HasChanges => _revision != _persistedRevision;
    public void Initialize(IReadOnlyDictionary<string, string>? files)
    {
        _files.Clear();
        if (files is not null) foreach (var file in files) _files[Normalize(file.Key)] = file.Value;
        _revision = _persistedRevision = 0;
    }
    public void WriteText(string fileName, string text) { _files[Normalize(fileName)] = text ?? ""; _revision++; }
    public string ReadText(string fileName, string fallback = "") => _files.GetValueOrDefault(Normalize(fileName), fallback);
    public void WriteJson<T>(string fileName, T value) => WriteText(fileName, JsonSerializer.Serialize(value));
    public T? ReadJson<T>(string fileName, T? fallback = default) => _files.TryGetValue(Normalize(fileName), out var json) ? JsonSerializer.Deserialize<T>(json) ?? fallback : fallback;
    public bool Exists(string fileName) => _files.ContainsKey(Normalize(fileName));
    public bool Delete(string fileName) { if (!_files.Remove(Normalize(fileName))) return false; _revision++; return true; }
    public string GetFullPath(string fileName) => "save:" + Normalize(fileName);
    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_files, StringComparer.OrdinalIgnoreCase);
    public void MarkPersisted(long revision) => _persistedRevision = revision;
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string value = path.Trim().Replace('\\', '/');
        if (value.StartsWith("save:", StringComparison.OrdinalIgnoreCase)) value = value[5..];
        if (value.StartsWith('/') || value.Contains(':') || value.Split('/').Any(p => p is ".." or "."))
            throw new ArgumentException("Save paths must stay inside the game's save directory.", nameof(path));
        return value;
    }
}
