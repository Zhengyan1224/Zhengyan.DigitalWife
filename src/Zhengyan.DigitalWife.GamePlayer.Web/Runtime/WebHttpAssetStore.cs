using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.GamePlayer.Runtime;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>
/// Reads a static Web game directory over HTTP. It provides the same small
/// contract that later memory and IndexedDB stores can implement.
/// </summary>
public sealed class WebHttpAssetStore(HttpClient httpClient) : IWebAssetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient = httpClient;
    private WebAssetManifest? _manifest;
    private string _rootPath = string.Empty;

    public WebAssetManifest Manifest => _manifest
        ?? throw new InvalidOperationException("The Web asset manifest has not been loaded.");

    public async ValueTask<WebAssetManifest> LoadManifestAsync(
        string rootPath = "web-game",
        CancellationToken cancellationToken = default)
    {
        _rootPath = NormalizeRootPath(rootPath);
        byte[] bytes;
        try
        {
            bytes = await GetBytesFromServerAsync("web.manifest.json", cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new WebAssetLoadException(
                "web.manifest.json",
                CombineUrl(_rootPath, "web.manifest.json"),
                $"Unable to fetch the Web manifest from '{_rootPath}'.",
                exception);
        }

        _manifest = Deserialize<WebAssetManifest>(bytes, "web.manifest.json");
        _manifest.Validate();
        if (!WebManifestSignature.Verify(_manifest))
        {
            throw new InvalidDataException("Web manifest signature is invalid.");
        }
        return _manifest;
    }

    ValueTask<WebAssetManifest> IWebAssetStore.LoadManifestAsync(CancellationToken cancellationToken)
        => LoadManifestAsync(cancellationToken: cancellationToken);

    public async ValueTask<bool> ExistsAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await ReadAllBytesAsync(relativePath, cancellationToken);
            return true;
        }
        catch (WebAssetLoadException exception) when (exception.IsMissing)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async ValueTask<Stream> OpenReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadAllBytesAsync(relativePath, cancellationToken);
        return new MemoryStream(bytes, writable: false);
    }

    public async ValueTask<byte[]> ReadAllBytesAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await EnsureManifestAsync(cancellationToken);
        string normalizedPath = NormalizeRelativePath(relativePath);
        WebAssetEntry? entry = Manifest.Assets.FirstOrDefault(asset =>
            string.Equals(NormalizeRelativePath(asset.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new WebAssetLoadException(
                normalizedPath,
                CombineUrl(_rootPath, normalizedPath),
                $"Asset is not registered in web.manifest.json: {normalizedPath}",
                isMissing: true);
        }

        byte[] bytes;
        try
        {
            bytes = await GetBytesFromServerAsync(normalizedPath, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new WebAssetLoadException(
                normalizedPath,
                CombineUrl(_rootPath, normalizedPath),
                $"Unable to fetch Web asset '{normalizedPath}' from '{_rootPath}'.",
                exception,
                exception.StatusCode == HttpStatusCode.NotFound);
        }

        if (entry.Size >= 0 && entry.Size != bytes.LongLength)
        {
            throw new WebAssetLoadException(
                normalizedPath,
                CombineUrl(_rootPath, normalizedPath),
                $"Asset size mismatch for {normalizedPath}: expected {entry.Size}, received {bytes.LongLength}.");
        }

        if (!string.IsNullOrWhiteSpace(entry.Sha256))
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new WebAssetLoadException(
                    normalizedPath,
                    CombineUrl(_rootPath, normalizedPath),
                    $"Asset SHA-256 mismatch for {normalizedPath}: expected {entry.Sha256}, received {actualHash}.");
            }
        }

        return bytes;
    }

    public async ValueTask<string> ReadTextAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadAllBytesAsync(relativePath, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    public async ValueTask<T> ReadJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadAllBytesAsync(relativePath, cancellationToken);
        return Deserialize<T>(bytes, NormalizeRelativePath(relativePath));
    }

    public ValueTask<string> GetUriAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = NormalizeRelativePath(relativePath);
        return ValueTask.FromResult(CombineUrl(_rootPath, normalized));
    }

    private async Task EnsureManifestAsync(CancellationToken cancellationToken)
    {
        if (_manifest is null)
        {
            await LoadManifestAsync(cancellationToken: cancellationToken);
        }
    }

    private async Task<byte[]> GetBytesFromServerAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        string uri = CombineUrl(_rootPath, relativePath);
        return await _httpClient.GetByteArrayAsync(uri, cancellationToken);
    }

    private static T Deserialize<T>(byte[] bytes, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException($"JSON asset is empty: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid JSON asset: {path}", exception);
        }
    }

    /* Manifest validation lives on WebAssetManifest so memory and HTTP stores
       apply exactly the same rules. */
    internal static void ValidateManifest(WebAssetManifest manifest)
    {
        if (manifest.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported Web manifest format: {manifest.FormatVersion}");
        }

        if (!string.Equals(manifest.Runtime, "web", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Web manifest runtime is not 'web': {manifest.Runtime}");
        }

        if (!string.Equals(manifest.Renderer, "webgl2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported Web renderer in manifest: {manifest.Renderer}");
        }

        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
        {
            throw new InvalidDataException("Web manifest apiVersion cannot be empty.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (WebAssetEntry entry in manifest.Assets)
        {
            string path = NormalizeRelativePath(entry.Path);
            if (!paths.Add(path))
            {
                throw new InvalidDataException($"Duplicate asset in web.manifest.json: {path}");
            }

            if (entry.Size < -1)
            {
                throw new InvalidDataException($"Invalid asset size in web.manifest.json: {path}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Sha256)
                && (entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit)))
            {
                throw new InvalidDataException($"Invalid SHA-256 in web.manifest.json: {path}");
            }
        }

        string entryScene = NormalizeRelativePath(manifest.EntryScene);
        if (!paths.Contains(entryScene))
        {
            throw new InvalidDataException($"Manifest entryScene is not registered: {entryScene}");
        }

        HashSet<string> scriptIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (WebScriptEntry script in manifest.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Id)
                || string.IsNullOrWhiteSpace(script.Assembly)
                || !scriptIds.Add(script.Id))
            {
                throw new InvalidDataException("Web manifest contains an invalid or duplicate script entry.");
            }

            string assemblyPath = NormalizeRelativePath(script.Assembly);
            if (!paths.Contains(assemblyPath))
            {
                throw new InvalidDataException($"Script assembly is not registered: {assemblyPath}");
            }
            if (!string.IsNullOrWhiteSpace(script.Sha256)
                && (script.Sha256.Length != 64 || !script.Sha256.All(Uri.IsHexDigit)))
            {
                throw new InvalidDataException($"Invalid script SHA-256 in web.manifest.json: {assemblyPath}");
            }
            WebAssetEntry assemblyEntry = manifest.Assets.First(asset =>
                string.Equals(NormalizeRelativePath(asset.Path), assemblyPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(script.Sha256)
                && !string.IsNullOrWhiteSpace(assemblyEntry.Sha256)
                && !string.Equals(script.Sha256, assemblyEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Script SHA-256 does not match its asset entry: {assemblyPath}");
            }
            if (!string.IsNullOrWhiteSpace(script.Entity) && script.Entity.Contains('\\'))
            {
                throw new InvalidDataException($"Invalid script entity id in web.manifest.json: {script.Id}");
            }
            if (string.IsNullOrWhiteSpace(script.ApiVersion))
            {
                throw new InvalidDataException($"Web script API version is empty: {script.Id}");
            }
            if (script.Dependencies.Any(dependency => string.Equals(dependency, script.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Web script depends on itself: {script.Id}");
            }
        }
        foreach (WebScriptEntry script in manifest.Scripts)
        {
            foreach (string dependency in script.Dependencies)
            {
                if (!scriptIds.Contains(dependency))
                    throw new InvalidDataException($"Web script dependency is not registered: {script.Id} -> {dependency}");
            }
        }
    }

    private static string NormalizeRootPath(string value)
    {
        string normalized = value.Replace('\\', '/').Trim().Trim('/');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Web asset root cannot be empty.", nameof(value));
        }

        return NormalizeRelativePath(normalized);
    }

    public static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Asset path cannot be empty.", nameof(value));
        }

        string normalized = value.Trim().Replace('\\', '/').TrimStart('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Asset path escapes the Web root: {value}");
        }

        return string.Join('/', parts);
    }

    private static string CombineUrl(string root, string relativePath)
    {
        return $"{root.TrimEnd('/')}/{NormalizeRelativePath(relativePath)}";
    }
}
