using System.Text;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Validated in-memory Web package store.</summary>
public sealed class WebMemoryAssetStore : IWebAssetStore
{
    private readonly MemoryAssetStore _bytes = new();
    private readonly Dictionary<string, string> _uris = new(StringComparer.OrdinalIgnoreCase);

    public WebMemoryAssetStore(WebAssetManifest manifest)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Manifest.Validate();
        if (!WebManifestSignature.Verify(Manifest)) throw new InvalidDataException("Web manifest signature is invalid.");
    }

    public WebAssetManifest Manifest { get; }

    public int Count => _bytes.Count;

    public void Add(string relativePath, ReadOnlySpan<byte> bytes)
    {
        string normalized = WebHttpAssetStore.NormalizeRelativePath(relativePath);
        _bytes.Add(normalized, bytes);
        _uris.Remove(normalized);
    }

    public ValueTask<WebAssetManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Manifest);
    }

    public ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        => _bytes.ExistsAsync(relativePath, cancellationToken);

    public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        => _bytes.OpenReadAsync(relativePath, cancellationToken);

    public ValueTask<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default)
        => _bytes.ReadAllBytesAsync(relativePath, cancellationToken);

    public async ValueTask<string> GetUriAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = WebHttpAssetStore.NormalizeRelativePath(relativePath);
        if (_uris.TryGetValue(normalized, out string? uri)) return uri;
        byte[] bytes = await _bytes.ReadAllBytesAsync(normalized, cancellationToken);
        string mime = GetMimeType(normalized);
        uri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        _uris[normalized] = uri;
        return uri;
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".json" => "application/json",
        ".vmd" => "application/octet-stream",
        ".pmx" => "application/octet-stream",
        ".dll" => "application/octet-stream",
        _ => "application/octet-stream"
    };
}
