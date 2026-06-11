using System.Numerics;
using System.Text;

namespace Zhengyan.DigitalWife.Mmd.Helpers;

internal static class BinaryReaderExtensions
{
    public static Encoding ShiftJIS { get; } = CodePagesEncodingProvider.Instance.GetEncoding("Shift-JIS")!;

    public static string ReadString(this BinaryReader binaryReader, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;

        return binaryReader.ReadString(binaryReader.ReadUInt32(), encoding);
    }

    public static string ReadString(this BinaryReader binaryReader, uint length, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;

        byte[] buffer = new byte[length];
        binaryReader.Read(buffer, 0, (int)length);

        return encoding.GetString(buffer).TrimEnd('\0');
    }

    public static string ReadCString(this BinaryReader binaryReader, int length, Encoding? encoding = null)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, null);
        }

        encoding ??= Encoding.UTF8;

        byte[] buffer = binaryReader.ReadBytes(length);
        if (buffer.Length != length)
        {
            throw new EndOfStreamException("Unable to read the requested fixed-length C string.");
        }

        int terminatorIndex = Array.IndexOf(buffer, (byte)0);
        int actualLength = terminatorIndex >= 0 ? terminatorIndex : buffer.Length;
        return actualLength == 0
            ? string.Empty
            : encoding.GetString(buffer, 0, actualLength);
    }

    public static int ReadIndex(this BinaryReader binaryReader, byte indexSize)
    {
        switch (indexSize)
        {
            case 1:
                {
                    byte index = binaryReader.ReadByte();
                    if (index == byte.MaxValue)
                    {
                        return -1;
                    }

                    return index;
                }
            case 2:
                {
                    ushort index = binaryReader.ReadUInt16();
                    if (index == ushort.MaxValue)
                    {
                        return -1;
                    }

                    return index;
                }
            case 4:
                {
                    uint index = binaryReader.ReadUInt32();
                    if (index == uint.MaxValue)
                    {
                        return -1;
                    }

                    return (int)index;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(indexSize), indexSize, null);
        }
    }

    public static ushort[] ReadUInt16s(this BinaryReader binaryReader, int count)
    {
        ushort[] array = new ushort[count];

        for (int i = 0; i < count; i++)
        {
            array[i] = binaryReader.ReadUInt16();
        }

        return array;
    }

    public static uint[] ReadUInt32s(this BinaryReader binaryReader, int count)
    {
        uint[] array = new uint[count];

        for (int i = 0; i < count; i++)
        {
            array[i] = binaryReader.ReadUInt32();
        }

        return array;
    }

    public static Vector2 ReadVector2(this BinaryReader binaryReader)
    {
        return new(binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }

    public static Vector3 ReadVector3(this BinaryReader binaryReader)
    {
        return new(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }

    public static Vector4 ReadVector4(this BinaryReader binaryReader)
    {
        return new(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }

    public static Quaternion ReadQuaternion(this BinaryReader binaryReader)
    {
        return new(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
}

