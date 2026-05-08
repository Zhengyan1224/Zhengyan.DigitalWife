using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.OpenGLES;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

internal sealed class EmbeddedToonTextureLibrary : IDisposable
{
    private readonly Texture2D[] _textures;

    public EmbeddedToonTextureLibrary(GL gl)
    {
        _textures = new Texture2D[10];

        for (int i = 0; i < _textures.Length; i++)
        {
            Texture2D texture = new(gl, GLEnum.ClampToEdge);
            string? toonPath = ResolveToonTexturePath(i + 1);
            if (toonPath is not null)
            {
                texture.LoadFromFile(toonPath);
            }
            else
            {
                texture.Upload(CreateDefaultToonRamp(i), 1, 256, TextureAlphaMode.Opaque);
            }

            _textures[i] = texture;
        }
    }

    public bool TryGetTexture(string texturePath, out Texture2D texture)
    {
        string fileName = Path.GetFileName(texturePath);
        if (fileName.StartsWith("toon", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> numberSpan = fileName.AsSpan(4, fileName.Length - 8);
            if (numberSpan.Length > 0 && int.TryParse(numberSpan, out int index) && index is >= 1 and <= 10)
            {
                texture = _textures[index - 1];
                return true;
            }
        }

        texture = null!;
        return false;
    }

    public void Dispose()
    {
        foreach (Texture2D texture in _textures)
        {
            texture.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] CreateDefaultToonRamp(int index)
    {
        byte[] pixels = new byte[256 * 4];
        float shadowFloor = Math.Clamp(0.55f + (index * 0.03f), 0.55f, 0.90f);
        float levels = 3.5f + (index * 0.5f);

        for (int y = 0; y < 256; y++)
        {
            float t = y / 255.0f;
            float quantized = MathF.Floor((1.0f - t) * levels) / MathF.Max(levels - 1.0f, 1.0f);
            float brightness = shadowFloor + (quantized * (1.0f - shadowFloor));
            byte value = (byte)Math.Clamp(brightness * 255.0f, 0.0f, 255.0f);

            int offset = y * 4;
            pixels[offset + 0] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        return pixels;
    }

    private static string? ResolveToonTexturePath(int toonIndex)
    {
        string canonicalName = $"toon{toonIndex:00}.bmp";
        return BundledAssetPathResolver.TryResolveFile("Resources", "MMD", canonicalName)
            ?? BundledAssetPathResolver.TryResolveFile(canonicalName);
    }
}

