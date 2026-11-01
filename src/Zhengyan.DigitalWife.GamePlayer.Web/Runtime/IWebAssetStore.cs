using Zhengyan.DigitalWife.GamePlayer.Runtime;
using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

/// <summary>Web asset store with a validated manifest.</summary>
public interface IWebAssetStore : IGameAssetStore
{
    WebAssetManifest Manifest { get; }

    ValueTask<WebAssetManifest> LoadManifestAsync(CancellationToken cancellationToken = default);
}

public static class WebAssetStoreExtensions
{
    public static async ValueTask<T> ReadJsonAsync<T>(this IWebAssetStore store, string relativePath, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await store.ReadAllBytesAsync(relativePath, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"Invalid JSON asset: {relativePath}");
    }
}
