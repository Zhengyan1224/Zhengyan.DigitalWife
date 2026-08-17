using Pfim;
using StbImageSharp;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal readonly record struct AndroidDecodedTexture(byte[] Rgba, int Width, int Height, bool HasSoftAlpha);

internal static class AndroidTextureDecoder
{
    public static AndroidDecodedTexture Decode(string path)
    {
        if (LooksLikeDds(path))
        {
            using IImage image = Pfimage.FromFile(path);
            byte[] rgba = ConvertPfimToRgba(image);
            return new AndroidDecodedTexture(rgba, image.Width, image.Height, HasSoftAlpha(rgba));
        }

        ImageResult imageResult = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        return new AndroidDecodedTexture(
            imageResult.Data,
            imageResult.Width,
            imageResult.Height,
            HasSoftAlpha(imageResult.Data));
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

    private static bool HasSoftAlpha(ReadOnlySpan<byte> rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] is > 0 and < byte.MaxValue)
            {
                return true;
            }
        }
        return false;
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
