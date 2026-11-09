using Microsoft.JSInterop;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebIndexedDbAssetCache(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private IJSObjectReference? _module;

    public async ValueTask PutAsync(string key, ReadOnlyMemory<byte> bytes)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("putAsset", key, bytes.ToArray());
    }

    public async ValueTask<byte[]?> GetAsync(string key)
    {
        IJSObjectReference module = await ModuleAsync();
        return await module.InvokeAsync<byte[]?>("getAsset", key);
    }

    public async ValueTask RemoveAsync(string key)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("removeAsset", key);
    }

    public async ValueTask ClearAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("clearAssets");
    }

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", WebRuntimeModuleUrl.Resolve("web-storage.js"));

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await _module.DisposeAsync();
        _module = null;
    }
}

public sealed class WebCachedAssetStore(IWebAssetStore source, WebIndexedDbAssetCache cache) : IWebAssetStore
{
    private readonly IWebAssetStore _source = source;
    private readonly WebIndexedDbAssetCache _cache = cache;
    private readonly Dictionary<string, string> _uris = new(StringComparer.OrdinalIgnoreCase);

    public WebAssetManifest Manifest => _source.Manifest;

    public ValueTask<WebAssetManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
        => _source.LoadManifestAsync(cancellationToken);

    public async ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        => await _source.ExistsAsync(relativePath, cancellationToken);

    public async ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        => new MemoryStream(await ReadAllBytesAsync(relativePath, cancellationToken), writable: false);

    public async ValueTask<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        string path = WebHttpAssetStore.NormalizeRelativePath(relativePath);
        byte[]? cached = await _cache.GetAsync(path);
        if (cached is not null && IsValid(path, cached)) return cached;
        byte[] bytes = await _source.ReadAllBytesAsync(path, cancellationToken);
        await _cache.PutAsync(path, bytes);
        return bytes;
    }

    public async ValueTask<string> GetUriAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        string path = WebHttpAssetStore.NormalizeRelativePath(relativePath);
        if (_uris.TryGetValue(path, out string? uri)) return uri;
        byte[] bytes = await ReadAllBytesAsync(path, cancellationToken);
        uri = $"data:application/octet-stream;base64,{Convert.ToBase64String(bytes)}";
        _uris[path] = uri;
        return uri;
    }

    private bool IsValid(string path, byte[] bytes)
    {
        WebAssetEntry? entry = Manifest.Assets.FirstOrDefault(item =>
            string.Equals(WebHttpAssetStore.NormalizeRelativePath(item.Path), path, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        if (entry.Size >= 0 && entry.Size != bytes.LongLength) return false;
        return string.IsNullOrWhiteSpace(entry.Sha256)
            || string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
