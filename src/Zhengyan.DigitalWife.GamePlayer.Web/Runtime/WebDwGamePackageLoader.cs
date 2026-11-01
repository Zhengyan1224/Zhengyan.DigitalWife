using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebPackageLoadResult(
    WebMemoryAssetStore Store,
    WebAssetManifest Manifest,
    bool Encrypted,
    long PayloadBytes);

/// <summary>Loads the native .dwgame container directly from browser memory.</summary>
public static class WebDwGamePackageLoader
{
    private static readonly byte[] Magic = "ZDWPKG1\0"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task<WebPackageLoadResult> LoadAsync(
        ReadOnlyMemory<byte> package,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(package.Span, password, cancellationToken));
    }

    public static Task<WebPackageLoadResult> LoadSplitAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> parts,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0) throw new InvalidDataException("The Web package has no parts.");
        long length = parts.Sum(part => (long)part.Length);
        if (length > int.MaxValue) throw new InvalidDataException("The Web package is too large for browser memory.");
        byte[] combined = new byte[length];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> part in parts)
        {
            part.Span.CopyTo(combined.AsSpan(offset));
            offset += part.Length;
        }
        return LoadAsync(combined, password, cancellationToken);
    }

    private static WebPackageLoadResult Load(ReadOnlySpan<byte> package, string? password, CancellationToken cancellationToken)
    {
        using MemoryStream input = new(package.ToArray(), writable: false);
        byte[] magic = ReadExactly(input, Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Invalid game package magic header.");
        byte[] lengthBytes = ReadExactly(input, 4);
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (headerLength <= 0 || headerLength > 1024 * 1024) throw new InvalidDataException("Invalid game package header length.");
        PackageHeader header = JsonSerializer.Deserialize<PackageHeader>(ReadExactly(input, headerLength), JsonOptions)
            ?? throw new InvalidDataException("Invalid game package header.");
        if (header.Version != 1 || !string.Equals(header.PayloadFormat, "zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported game package version or payload format: {header.Version}/{header.PayloadFormat}");
        if (header.PayloadLength < 0 || header.PayloadLength > input.Length - input.Position)
            throw new InvalidDataException("Package payload length is invalid.");

        if (header.Crypto is not null)
        {
            throw new PlatformNotSupportedException(
                "Encrypted .dwgame packages cannot be opened directly in the browser. Export a Web static package or use an unencrypted Web package.");
        }
        byte[] zip = ReadExactly(input, checked((int)header.PayloadLength));
        string hash = Convert.ToHexString(SHA256.HashData(zip));
        if (!string.Equals(hash, header.PlainSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Package integrity check failed.");

        Dictionary<string, byte[]> files = ReadZip(zip, cancellationToken);
        WebAssetManifest manifest = files.TryGetValue("web.manifest.json", out byte[]? manifestBytes)
            ? JsonSerializer.Deserialize<WebAssetManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("Invalid web.manifest.json in package.")
            : CreateManifest(files);
        if (!files.ContainsKey("web.manifest.json"))
        {
            files["web.manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        }
        manifest.Validate();
        WebMemoryAssetStore store = new(manifest);
        foreach ((string path, byte[] bytes) in files) store.Add(path, bytes);
        return new WebPackageLoadResult(store, manifest, header.Crypto is not null, zip.LongLength);
    }

    private static Dictionary<string, byte[]> ReadZip(byte[] zip, CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
        using MemoryStream stream = new(zip, writable: false);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = NormalizeEntryPath(entry.FullName);
            if (path.Length == 0 || entry.Name.Length == 0) continue;
            using Stream source = entry.Open();
            using MemoryStream target = new();
            source.CopyTo(target);
            if (!files.TryAdd(path, target.ToArray())) throw new InvalidDataException($"Duplicate package entry: {path}");
        }
        return files;
    }

    private static WebAssetManifest CreateManifest(IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(GameProjectStore.ProjectFileName, out byte[]? projectBytes))
            throw new InvalidDataException($"Package does not contain {GameProjectStore.ProjectFileName}.");
        GameProject project = JsonSerializer.Deserialize<GameProject>(projectBytes, JsonOptions)
            ?? throw new InvalidDataException("Invalid game.project.json in package.");
        GameProjectStore.NormalizeScenes(project);
        string entryScene = WebHttpAssetStore.NormalizeRelativePath(project.DefaultScene);
        List<WebAssetEntry> assets = files.Keys
            .Where(path => !string.Equals(path, "web.manifest.json", StringComparison.OrdinalIgnoreCase))
            .Select(path => new WebAssetEntry
            {
                Path = path,
                Kind = GetKind(path),
                Size = files[path].LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(files[path]))
            }).ToList();
        List<WebScriptEntry> scripts = [];
        foreach (string scenePath in project.Scenes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!files.TryGetValue(WebHttpAssetStore.NormalizeRelativePath(scenePath), out byte[]? sceneBytes)) continue;
            GameProjectScene scene = JsonSerializer.Deserialize<GameProjectScene>(sceneBytes, JsonOptions) ?? new();
            foreach ((ScriptBinding Binding, string Entity) binding in scene.LoadingScripts.Select(item => (item, string.Empty))
                .Concat(scene.Entities.SelectMany(entity => entity.Scripts.Select(item => (item, entity.Id)))))
            {
                if (!binding.Binding.Enabled || !IsCSharp(binding.Binding.Path, binding.Binding.Language)) continue;
                string source = WebHttpAssetStore.NormalizeRelativePath(binding.Binding.Path);
                string assembly = $"compiled/web/{Path.ChangeExtension(source, ".dll")}".Replace('\\', '/');
                if (!files.ContainsKey(assembly) || scripts.Any(item => item.Id.Equals(source, StringComparison.OrdinalIgnoreCase))) continue;
                scripts.Add(new WebScriptEntry
                {
                    Id = source,
                    Assembly = assembly,
                    Entity = binding.Entity,
                    Sha256 = Convert.ToHexString(SHA256.HashData(files[assembly]))
                });
            }
        }
        return new WebAssetManifest
        {
            EntryScene = entryScene,
            Assets = assets,
            Scripts = scripts,
            Capabilities = ["scene", "pmx", "vmd", "webgl2", "precompiled-scripts"]
        };
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static string NormalizeEntryPath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or "..")) throw new InvalidDataException($"Package entry escapes root: {path}");
        return string.Join('/', parts);
    }

    private static string GetKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "json", ".pmx" => "pmx", ".vmd" => "vmd", ".dll" => "assembly",
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => "texture", _ => "binary"
    };

    private static bool IsCSharp(string path, string language)
        => language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
            || language.Equals("cs", StringComparison.OrdinalIgnoreCase)
            || language.Equals("csx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);

    private sealed class PackageHeader
    {
        public int Version { get; set; }
        public string PayloadFormat { get; set; } = string.Empty;
        public long PayloadLength { get; set; }
        public string PlainSha256 { get; set; } = string.Empty;
        public PackageCryptoHeader? Crypto { get; set; }
    }

    private sealed class PackageCryptoHeader
    {
        public string Algorithm { get; set; } = string.Empty;
        public string Kdf { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
        public string BaseNonce { get; set; } = string.Empty;
        public int ChunkSize { get; set; }
        public int TagSize { get; set; }
    }
}
