using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Pfim;
using StbImageSharp;
using System.Buffers.Binary;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

public enum TextureAlphaMode
{
    Opaque = 0,
    Blend = 1,
    ColorMask = 2,
    BlendMaskColor = 3
}

public unsafe class Texture2D : IDisposable
{
    private readonly GL _gl;
    private const float SoftAlphaOverlayThreshold = 0.25f;
    private bool _disposed;

    public uint Id { get; }

    public bool HasAlpha { get; private set; } = true;

    public TextureAlphaMode AlphaMode { get; private set; } = TextureAlphaMode.Opaque;

    public Texture2D(GL gl, GLEnum wrapMode = GLEnum.Repeat)
    {
        _gl = gl;
        Id = _gl.GenTexture();

        _gl.BindTexture(GLEnum.Texture2D, Id);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)wrapMode);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)wrapMode);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBaseLevel, 0);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, 8);
        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public void LoadFromFile(string filePath)
    {
        if (LooksLikeDds(filePath))
        {
            LoadDdsTexture(filePath);
            return;
        }

        if (Path.GetExtension(filePath).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            LoadBmpTexture(filePath);
            return;
        }

        LoadStandardImageTexture(filePath);
    }

    public void Upload(byte[] bytes, uint width, uint height, TextureAlphaMode alphaMode = TextureAlphaMode.Blend)
    {
        fixed (byte* ptr = bytes)
        {
            Upload(ptr, new Vector2D<uint>(width, height), GLEnum.Rgba, GLEnum.UnsignedByte, alphaMode);
        }
    }

    public void Fill(byte red, byte green, byte blue, byte alpha = 255)
    {
        Upload([red, green, blue, alpha], 1, 1, alpha < 255 ? TextureAlphaMode.Blend : TextureAlphaMode.Opaque);
    }

    public void Upload(void* image, Vector2D<uint> size, GLEnum format, GLEnum type, TextureAlphaMode alphaMode = TextureAlphaMode.Blend)
    {
        AlphaMode = alphaMode;
        HasAlpha = alphaMode != TextureAlphaMode.Opaque;

        _gl.BindTexture(GLEnum.Texture2D, Id);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)format, size.X, size.Y, 0, format, type, image);

        if (alphaMode == TextureAlphaMode.Opaque)
        {
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBaseLevel, 0);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, 8);
            _gl.GenerateMipmap(GLEnum.Texture2D);
        }
        else
        {
            // Semi-transparent decals and overlays are sensitive to mipmap color bleeding.
            // Keep them at base level instead of applying texture-name-specific fixes.
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBaseLevel, 0);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, 0);
        }

        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteTexture(Id);
        GC.SuppressFinalize(this);
    }

    private void LoadStandardImageTexture(string filePath)
    {
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(filePath), ColorComponents.RedGreenBlueAlpha);

        TextureAlphaMode alphaMode = DetermineAlphaMode(image.Data);
        Upload(image.Data, (uint)image.Width, (uint)image.Height, alphaMode);
    }

    private void LoadDdsTexture(string filePath)
    {
        using IImage image = Pfimage.FromFile(filePath);
        byte[] rgba = ConvertPfimToRgba(image);
        TextureAlphaMode alphaMode = DetermineAlphaMode(rgba);
        Upload(rgba, (uint)image.Width, (uint)image.Height, alphaMode);
    }

    private void LoadBmpTexture(string filePath)
    {
        if (TryLoadBitfieldBmp(filePath, out byte[]? rgba, out uint width, out uint height))
        {
            TextureAlphaMode alphaMode = DetermineAlphaMode(rgba);
            Upload(rgba, width, height, alphaMode);
            return;
        }

        LoadStandardImageTexture(filePath);
    }

    private static TextureAlphaMode DetermineAlphaMode(byte[] rgba)
    {
        AlphaStats alphaStats = AnalyzeAlpha(rgba);
        if (!alphaStats.HasAlpha)
        {
            return TextureAlphaMode.Opaque;
        }

        return alphaStats.SoftAlphaRatio >= SoftAlphaOverlayThreshold
            ? TextureAlphaMode.BlendMaskColor
            : TextureAlphaMode.Blend;
    }

    private readonly record struct AlphaStats(int NonZeroAlphaPixels, int SoftAlphaPixels)
    {
        public bool HasAlpha => SoftAlphaPixels > 0;

        public float SoftAlphaRatio => NonZeroAlphaPixels == 0
            ? 0.0f
            : SoftAlphaPixels / (float)NonZeroAlphaPixels;
    }

    private static AlphaStats AnalyzeAlpha(byte[] rgba)
    {
        int nonZeroAlphaPixels = 0;
        int softAlphaPixels = 0;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            byte alpha = rgba[i];
            if (alpha == 0)
            {
                continue;
            }

            nonZeroAlphaPixels++;
            if (alpha < byte.MaxValue)
            {
                softAlphaPixels++;
            }
        }

        return new AlphaStats(nonZeroAlphaPixels, softAlphaPixels);
    }

    private static bool LooksLikeDds(string texturePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(texturePath);
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) == 4)
            {
                return header[0] == (byte)'D'
                    && header[1] == (byte)'D'
                    && header[2] == (byte)'S'
                    && header[3] == (byte)' ';
            }
        }
        catch
        {
        }

        return false;
    }

    private static byte[] ConvertPfimToRgba(IImage image)
    {
        int width = image.Width;
        int height = image.Height;
        int stride = image.Stride;
        byte[] source = image.Data;
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * stride;
            int dstRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int dst = dstRow + (x * 4);
                switch (image.Format)
                {
                    case ImageFormat.Rgba32:
                    {
                        int src = srcRow + (x * 4);
                        // Pfim outputs BGRA ordering for DDS.
                        rgba[dst + 0] = source[src + 2];
                        rgba[dst + 1] = source[src + 1];
                        rgba[dst + 2] = source[src + 0];
                        rgba[dst + 3] = source[src + 3];
                        break;
                    }
                    case ImageFormat.Rgb24:
                    {
                        int src = srcRow + (x * 3);
                        rgba[dst + 0] = source[src + 2];
                        rgba[dst + 1] = source[src + 1];
                        rgba[dst + 2] = source[src + 0];
                        rgba[dst + 3] = 255;
                        break;
                    }
                    case ImageFormat.Rgb8:
                    {
                        int src = srcRow + x;
                        byte value = source[src];
                        rgba[dst + 0] = value;
                        rgba[dst + 1] = value;
                        rgba[dst + 2] = value;
                        rgba[dst + 3] = 255;
                        break;
                    }
                    case ImageFormat.R5g6b5:
                    {
                        int src = srcRow + (x * 2);
                        ushort packed = (ushort)(source[src] | (source[src + 1] << 8));
                        int r = (packed >> 11) & 0x1F;
                        int g = (packed >> 5) & 0x3F;
                        int b = packed & 0x1F;
                        rgba[dst + 0] = (byte)(r * 255 / 31);
                        rgba[dst + 1] = (byte)(g * 255 / 63);
                        rgba[dst + 2] = (byte)(b * 255 / 31);
                        rgba[dst + 3] = 255;
                        break;
                    }
                    case ImageFormat.R5g5b5:
                    {
                        int src = srcRow + (x * 2);
                        ushort packed = (ushort)(source[src] | (source[src + 1] << 8));
                        int r = (packed >> 10) & 0x1F;
                        int g = (packed >> 5) & 0x1F;
                        int b = packed & 0x1F;
                        rgba[dst + 0] = (byte)(r * 255 / 31);
                        rgba[dst + 1] = (byte)(g * 255 / 31);
                        rgba[dst + 2] = (byte)(b * 255 / 31);
                        rgba[dst + 3] = 255;
                        break;
                    }
                    case ImageFormat.R5g5b5a1:
                    {
                        int src = srcRow + (x * 2);
                        ushort packed = (ushort)(source[src] | (source[src + 1] << 8));
                        int r = (packed >> 11) & 0x1F;
                        int g = (packed >> 6) & 0x1F;
                        int b = (packed >> 1) & 0x1F;
                        int a = packed & 0x1;
                        rgba[dst + 0] = (byte)(r * 255 / 31);
                        rgba[dst + 1] = (byte)(g * 255 / 31);
                        rgba[dst + 2] = (byte)(b * 255 / 31);
                        rgba[dst + 3] = (byte)(a == 0 ? 0 : 255);
                        break;
                    }
                    default:
                        throw new NotSupportedException($"Unsupported DDS format: {image.Format}");
                }
            }
        }

        return rgba;
    }

    private static bool TryLoadBitfieldBmp(string filePath, out byte[] rgba, out uint width, out uint height)
    {
        rgba = [];
        width = 0;
        height = 0;

        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 70 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            return false;
        }

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        int dibHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4));
        int bmpWidth = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        int bmpHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30, 4));
        if (bmpWidth <= 0
            || bmpHeight == 0
            || bitsPerPixel != 32
            || compression != 3
            || dibHeaderSize < 56
            || bytes.Length < pixelOffset)
        {
            return false;
        }

        uint redMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(54, 4));
        uint greenMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(58, 4));
        uint blueMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(62, 4));
        uint alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(66, 4));
        if (redMask == 0 || greenMask == 0 || blueMask == 0 || alphaMask == 0)
        {
            return false;
        }

        int absHeight = Math.Abs(bmpHeight);
        int rowStride = ((bmpWidth * bitsPerPixel + 31) / 32) * 4;
        if (pixelOffset + (rowStride * absHeight) > bytes.Length)
        {
            return false;
        }

        width = (uint)bmpWidth;
        height = (uint)absHeight;
        rgba = new byte[bmpWidth * absHeight * 4];
        bool topDown = bmpHeight < 0;

        for (int y = 0; y < absHeight; y++)
        {
            int srcY = topDown ? y : (absHeight - 1 - y);
            int srcRow = pixelOffset + (srcY * rowStride);
            int dstRow = y * bmpWidth * 4;
            for (int x = 0; x < bmpWidth; x++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(srcRow + (x * 4), 4));
                int dst = dstRow + (x * 4);
                rgba[dst + 0] = ExtractBitfieldByte(packed, redMask);
                rgba[dst + 1] = ExtractBitfieldByte(packed, greenMask);
                rgba[dst + 2] = ExtractBitfieldByte(packed, blueMask);
                rgba[dst + 3] = ExtractBitfieldByte(packed, alphaMask);
            }
        }

        return true;
    }

    private static byte ExtractBitfieldByte(uint packed, uint mask)
    {
        if (mask == 0)
        {
            return byte.MaxValue;
        }

        int shift = 0;
        uint shiftedMask = mask;
        while ((shiftedMask & 1) == 0)
        {
            shiftedMask >>= 1;
            shift++;
        }

        uint value = (packed & mask) >> shift;
        uint maxValue = shiftedMask;
        if (maxValue == 0)
        {
            return 0;
        }

        return (byte)((value * 255u + (maxValue / 2u)) / maxValue);
    }

}

