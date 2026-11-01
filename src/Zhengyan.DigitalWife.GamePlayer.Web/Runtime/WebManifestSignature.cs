#pragma warning disable CA1416
using System.Security.Cryptography;
using System.Text.Json;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public static class WebManifestSignature
{
    public static bool Verify(WebAssetManifest manifest, bool requireSignature = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Signature)) return !requireSignature;
        if (!string.Equals(manifest.SignatureAlgorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(manifest.PublicKey)) return false;
        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(manifest.PublicKey);
            return rsa.VerifyData(CanonicalBytes(manifest), Convert.FromBase64String(manifest.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }

    public static byte[] CanonicalBytes(WebAssetManifest manifest)
    {
        var canonical = new
        {
            formatVersion = manifest.FormatVersion,
            runtime = manifest.Runtime,
            renderer = manifest.Renderer,
            apiVersion = manifest.ApiVersion,
            entryScene = manifest.EntryScene,
            assets = manifest.Assets,
            scripts = manifest.Scripts,
            capabilities = manifest.Capabilities,
            keyId = manifest.KeyId
        };
        return JsonSerializer.SerializeToUtf8Bytes(canonical, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

#pragma warning restore CA1416
