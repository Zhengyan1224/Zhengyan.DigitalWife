using Pfim;
using StbImageSharp;
using System.Buffers.Binary;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidBitmapFactory = Android.Graphics.BitmapFactory;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal enum AndroidTextureAlphaMode
{
    Opaque = 1,
    Blend = 2,
    ColorMask = 3,
    BlendMaskColor = 4
}

internal readonly record struct AndroidDecodedTexture(
    byte[] Rgba,
    int Width,
    int Height,
    AndroidTextureAlphaMode AlphaMode)
{
    // Only blended materials need late sorting and disabled depth writes.
    // Color-mask textures are alpha-tested and must stay in the opaque pass.
    public bool HasSoftAlpha => AlphaMode is AndroidTextureAlphaMode.Blend or AndroidTextureAlphaMode.BlendMaskColor;
}

internal static class AndroidTextureDecoder
{
    public static AndroidDecodedTexture Decode(string path, int maxDimension = 0)
    {
        if (LooksLikeDds(path))
        {
            using IImage image = Pfimage.FromFile(path);
            byte[] rgba = ConvertPfimToRgba(image);
            return new AndroidDecodedTexture(rgba, image.Width, image.Height, DetermineAlphaMode(rgba));
        }

        if (Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            && TryLoadBitfieldBmp(path, out byte[]? bitfieldRgba, out int bitfieldWidth, out int bitfieldHeight))
        {
            return new AndroidDecodedTexture(
                bitfieldRgba,
                bitfieldWidth,
                bitfieldHeight,
                DetermineAlphaMode(bitfieldRgba));
        }

        if (maxDimension > 0
            && Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && TryDecodeBitmap(path, maxDimension, out AndroidDecodedTexture bitmapTexture))
        {
            return bitmapTexture;
        }

        ImageResult imageResult = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        return new AndroidDecodedTexture(
            imageResult.Data,
            imageResult.Width,
            imageResult.Height,
            DetermineAlphaMode(imageResult.Data));
    }

    private static bool TryDecodeBitmap(string path, int maxDimension, out AndroidDecodedTexture texture)
    {
        texture = default;
        AndroidBitmapFactory.Options bounds = new() { InJustDecodeBounds = true };
        using (AndroidBitmap? ignored = AndroidBitmapFactory.DecodeFile(path, bounds))
        {
        }

        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return false;
        }

        int sample = 1;
        while (Math.Max(bounds.OutWidth / (sample * 2), bounds.OutHeight / (sample * 2)) >= maxDimension)
        {
            sample *= 2;
        }

        AndroidBitmapFactory.Options options = new()
        {
            InSampleSize = sample,
            InPreferredConfig = AndroidBitmap.Config.Argb8888
        };
        using AndroidBitmap? bitmap = AndroidBitmapFactory.DecodeFile(path, options);
        if (bitmap is null)
        {
            return false;
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        int[] argb = new int[checked(width * height)];
        bitmap.GetPixels(argb, 0, width, 0, 0, width, height);
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int i = 0; i < argb.Length; i++)
        {
            int color = argb[i];
            int offset = i * 4;
            rgba[offset] = (byte)(color >> 16);
            rgba[offset + 1] = (byte)(color >> 8);
            rgba[offset + 2] = (byte)color;
            rgba[offset + 3] = (byte)(color >> 24);
        }

        texture = new AndroidDecodedTexture(rgba, width, height, AndroidTextureAlphaMode.Opaque);
        return true;
    }

    private static bool LooksLikeDds(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return stream.Read(header) == 4
            && header[0] == (byte)'D'
            && header[1] == (byte)'D'
            && header[2] == (byte)'S'
            && header[3] == (byte)' ';
    }

    private static AndroidTextureAlphaMode DetermineAlphaMode(ReadOnlySpan<byte> rgba)
    {
        bool hasTransparentPixels = false;
        int nonZeroAlphaPixels = 0;
        int softAlphaPixels = 0;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            byte alpha = rgba[i];
            // Texture conversion and block compression commonly turn 0/255 into
            // values a few steps inside the range. Treat those endpoint values
            // as fully transparent/opaque so ordinary PMX materials keep depth
            // writes enabled instead of being misclassified as blended.
            if (alpha <= 4)
            {
                hasTransparentPixels = true;
                continue;
            }
            nonZeroAlphaPixels++;
            if (alpha < 251) softAlphaPixels++;
        }
        if (softAlphaPixels == 0)
        {
            return hasTransparentPixels ? AndroidTextureAlphaMode.ColorMask : AndroidTextureAlphaMode.Opaque;
        }
        return softAlphaPixels / (float)Math.Max(nonZeroAlphaPixels, 1) >= 0.25f
            ? AndroidTextureAlphaMode.BlendMaskColor
            : AndroidTextureAlphaMode.Blend;
    }

    private static byte[] ConvertPfimToRgba(IImage image)
    {
        byte[] rgba = new byte[image.Width * image.Height * 4];
        for (int y = 0; y < image.Height; y++)
        {
            int sourceRow = y * image.Stride;
            int destinationRow = y * image.Width * 4;
            for (int x = 0; x < image.Width; x++)
            {
                int destination = destinationRow + x * 4;
                switch (image.Format)
                {
                    case ImageFormat.Rgba32:
                    {
                        int source = sourceRow + x * 4;
                        rgba[destination] = image.Data[source + 2];
                        rgba[destination + 1] = image.Data[source + 1];
                        rgba[destination + 2] = image.Data[source];
                        rgba[destination + 3] = image.Data[source + 3];
                        break;
                    }
                    case ImageFormat.Rgb24:
                    {
                        int source = sourceRow + x * 3;
                        rgba[destination] = image.Data[source + 2];
                        rgba[destination + 1] = image.Data[source + 1];
                        rgba[destination + 2] = image.Data[source];
                        rgba[destination + 3] = byte.MaxValue;
                        break;
                    }
                    case ImageFormat.Rgb8:
                    {
                        byte value = image.Data[sourceRow + x];
                        rgba[destination] = value;
                        rgba[destination + 1] = value;
                        rgba[destination + 2] = value;
                        rgba[destination + 3] = byte.MaxValue;
                        break;
                    }
                    case ImageFormat.R5g6b5:
                        ConvertPacked16(image.Data, sourceRow + x * 2, destination, rgba, 11, 5, 0, false);
                        break;
                    case ImageFormat.R5g5b5:
                        ConvertPacked16(image.Data, sourceRow + x * 2, destination, rgba, 10, 5, 0, false);
                        break;
                    case ImageFormat.R5g5b5a1:
                        ConvertPacked16(image.Data, sourceRow + x * 2, destination, rgba, 11, 6, 1, true);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported DDS format: {image.Format}");
                }
            }
        }
        return rgba;
    }

    private static bool TryLoadBitfieldBmp(
        string path,
        out byte[] rgba,
        out int width,
        out int height)
    {
        rgba = [];
        width = 0;
        height = 0;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        if (bytes.Length < 70 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            return false;
        }

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4));
        int bmpWidth = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        int bmpHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30, 4));
        if (bmpWidth <= 0 || bmpHeight == 0 || bitsPerPixel != 32 || compression != 3
            || headerSize < 56 || pixelOffset < 0 || bytes.Length < pixelOffset)
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
        int rowStride = checked(bmpWidth * 4);
        if (pixelOffset > bytes.Length - checked(rowStride * absHeight))
        {
            return false;
        }

        width = bmpWidth;
        height = absHeight;
        rgba = new byte[checked(width * height * 4)];
        bool topDown = bmpHeight < 0;
        for (int y = 0; y < height; y++)
        {
            int sourceY = topDown ? y : height - 1 - y;
            int sourceRow = pixelOffset + sourceY * rowStride;
            int destinationRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sourceRow + x * 4, 4));
                int destination = destinationRow + x * 4;
                rgba[destination] = ExtractBitfieldByte(packed, redMask);
                rgba[destination + 1] = ExtractBitfieldByte(packed, greenMask);
                rgba[destination + 2] = ExtractBitfieldByte(packed, blueMask);
                rgba[destination + 3] = ExtractBitfieldByte(packed, alphaMask);
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
        uint normalizedMask = mask;
        while ((normalizedMask & 1) == 0)
        {
            normalizedMask >>= 1;
            shift++;
        }

        uint value = (packed & mask) >> shift;
        return (byte)((value * 255u + normalizedMask / 2u) / normalizedMask);
    }

    private static void ConvertPacked16(
        byte[] source,
        int sourceOffset,
        int destinationOffset,
        byte[] destination,
        int redShift,
        int greenShift,
        int blueShift,
        bool oneBitAlpha)
    {
        ushort packed = (ushort)(source[sourceOffset] | source[sourceOffset + 1] << 8);
        int greenBits = redShift - greenShift;
        int blueBits = greenShift - blueShift;
        int red = packed >> redShift & 0x1F;
        int greenMask = (1 << greenBits) - 1;
        int blueMask = (1 << blueBits) - 1;
        int green = packed >> greenShift & greenMask;
        int blue = packed >> blueShift & blueMask;
        destination[destinationOffset] = (byte)(red * 255 / 31);
        destination[destinationOffset + 1] = (byte)(green * 255 / greenMask);
        destination[destinationOffset + 2] = (byte)(blue * 255 / blueMask);
        destination[destinationOffset + 3] = oneBitAlpha && (packed & 1) == 0 ? (byte)0 : byte.MaxValue;
    }
}
