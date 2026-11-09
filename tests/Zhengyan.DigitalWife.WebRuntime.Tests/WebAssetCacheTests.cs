using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

internal static class WebAssetCacheTests
{
    private const string Model = "assets/models/Body/Body.pmx";
    private const string Texture = "assets/textures/shared.bmp";

    public static async Task RunAsync()
    {
        using CacheServer server = new();
        using HttpClient client = new(server) { BaseAddress = new Uri("https://fixture.invalid/") };
        WebHttpAssetStore store = new(client);
        await store.LoadManifestAsync();
        byte[] first = await store.ReadAllBytesAsync(Model);
        string firstTexture = await store.GetUriAsync(Texture);
        await using WebIndexedDbAssetCache cache = new(new FakeBrowser());
        WebCachedAssetStore cached = new(store, cache);
        await cached.LoadManifestAsync();
        string firstCachedTexture = await cached.GetUriAsync(Texture);

        server.Body = Encoding.UTF8.GetBytes("a larger model from another game");
        server.Texture = Encoding.UTF8.GetBytes("next texture");
        await cached.LoadManifestAsync();
        byte[] next = await store.ReadAllBytesAsync(Model);
        string nextTexture = await store.GetUriAsync(Texture);
        Check(next.SequenceEqual(server.Body) && !next.SequenceEqual(first), "Project replacement reused the previous model.");
        Check(firstTexture != nextTexture && nextTexture.Contains(Hash(server.Texture).ToLowerInvariant()), "Texture URLs do not follow their manifest hashes.");
        Check(firstCachedTexture != await cached.GetUriAsync(Texture), "Reloading the manifest retained an old in-memory texture URI.");
        Check(server.ManifestRequests.All(request => request), "A mutable manifest was read through the browser cache.");
        Check(server.AssetRequests.All(uri => uri.Query.StartsWith("?v=")), "An asset was fetched under an unversioned pathname.");
        Console.WriteLine("PASS switching projects replaces model bytes, texture URLs and cached texture URIs");

        server.UseHashes = false;
        await store.LoadManifestAsync();
        string fallback = await store.GetUriAsync(Model);
        await store.LoadManifestAsync();
        Check(fallback != await store.GetUriAsync(Model), "Unhashed legacy assets were cached across project loads.");

        server.UseHashes = true;
        server.Body = Encoding.UTF8.GetBytes("a genuinely damaged export");
        server.CorruptResponse = true;
        await store.LoadManifestAsync();
        bool rejected = false;
        try { await store.ReadAllBytesAsync(Model); }
        catch (WebAssetLoadException exception) { rejected = exception.Message.Contains("SHA-256 mismatch"); }
        Check(rejected, "Content-addressed fetching bypassed export integrity validation.");
        Console.WriteLine("PASS unhashed assets refresh and corrupt exports still fail integrity validation");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class CacheServer : HttpMessageHandler
    {
        public byte[] Body { get; set; } = Encoding.UTF8.GetBytes("old model");
        public byte[] Texture { get; set; } = Encoding.UTF8.GetBytes("old texture");
        public bool UseHashes { get; set; } = true;
        public bool CorruptResponse { get; set; }
        public List<bool> ManifestRequests { get; } = [];
        public List<Uri> AssetRequests { get; } = [];
        private readonly Dictionary<string, byte[]> _cache = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            bool noStore = request.Options.Select(item => item.Value).OfType<IDictionary<string, object>>()
                .Any(options => options.TryGetValue("cache", out object? value) && Equals(value, "no-store"));
            byte[] bytes;
            if (uri.AbsolutePath.EndsWith("web.manifest.json"))
            {
                ManifestRequests.Add(noStore);
                WebAssetManifest manifest = new()
                {
                    Assets = [
                        new() { Path = "scenes/main.scene.json", Size = 2, Sha256 = UseHashes ? Hash(Encoding.UTF8.GetBytes("{}")) : "" },
                        new() { Path = Model, Size = Body.Length, Sha256 = UseHashes ? Hash(Body) : "" },
                        new() { Path = WebAssetCacheTests.Texture, Size = Texture.Length, Sha256 = UseHashes ? Hash(Texture) : "" }
                    ]
                };
                bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            else
            {
                AssetRequests.Add(uri);
                bytes = uri.AbsolutePath.EndsWith("Body.pmx") ? Body : Texture;
                if (CorruptResponse) bytes = Enumerable.Repeat((byte)'!', bytes.Length).ToArray();
            }
            // Model a browser that retains fresh cached URLs across reloads.
            if (!noStore && _cache.TryGetValue(uri.AbsoluteUri, out byte[]? old)) bytes = old;
            else _cache[uri.AbsoluteUri] = bytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
