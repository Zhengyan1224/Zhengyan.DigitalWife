namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

/// <summary>
/// Platform-neutral asynchronous access to game assets. Desktop, Android,
/// static Web directories and future in-memory packages can share loaders
/// without exposing File or browser APIs to scene code.
/// </summary>
public interface IGameAssetStore
{
    ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    ValueTask<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default);

    ValueTask<string> GetUriAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Small package-independent store for already loaded bytes. It is useful for
/// tests and is the common seam for a future .dwgame memory package loader.
/// </summary>
public sealed class MemoryAssetStore : IGameAssetStore
{
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _assets.Count;

    public void Add(string relativePath, ReadOnlySpan<byte> bytes)
    {
        _assets[NormalizePath(relativePath)] = bytes.ToArray();
    }

    public bool Remove(string relativePath) => _assets.Remove(NormalizePath(relativePath));

    public ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_assets.ContainsKey(NormalizePath(relativePath)));
    }

    public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = GetBytes(relativePath);
        return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public ValueTask<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetBytes(relativePath));
    }

    public ValueTask<string> GetUriAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult($"memory:///{NormalizePath(relativePath)}");
    }

    private byte[] GetBytes(string relativePath)
    {
        string normalized = NormalizePath(relativePath);
        return _assets.TryGetValue(normalized, out byte[]? bytes)
            ? bytes.ToArray()
            : throw new FileNotFoundException($"Memory asset not found: {normalized}", normalized);
    }

    private static string NormalizePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Asset path cannot be empty.", nameof(relativePath));
        }

        string normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Asset path escapes the memory store: {relativePath}");
        }

        return string.Join('/', parts);
    }
}
