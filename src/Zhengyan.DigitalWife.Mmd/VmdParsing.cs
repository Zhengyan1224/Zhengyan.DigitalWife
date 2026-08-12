using System.Numerics;
using Zhengyan.DigitalWife.Mmd.Helpers;

namespace Zhengyan.DigitalWife.Mmd;

#region Enums
public enum ShadowType : byte
{
    Off,
    Mode1,
    Mode2,
}
#endregion

#region Classes
public class VmdHeader(BinaryReader binaryReader)
{
    public string Title { get; } = binaryReader.ReadCString(30, BinaryReaderExtensions.ShiftJIS);

    public string ModelName { get; } = binaryReader.ReadCString(20, BinaryReaderExtensions.ShiftJIS);
}

public class VmdMotion(BinaryReader binaryReader)
{
    public string BoneName { get; } = binaryReader.ReadCString(15, BinaryReaderExtensions.ShiftJIS);

    public uint Frame { get; } = binaryReader.ReadUInt32();

    public Vector3 Translate { get; } = binaryReader.ReadVector3();

    public Quaternion Quaternion { get; } = binaryReader.ReadQuaternion();

    public byte[] Interpolation { get; } = binaryReader.ReadBytes(64);
}

public class VmdMorph(BinaryReader binaryReader)
{
    public string BlendShapeName { get; } = binaryReader.ReadCString(15, BinaryReaderExtensions.ShiftJIS);

    public uint Frame { get; } = binaryReader.ReadUInt32();

    public float Weight { get; } = binaryReader.ReadSingle();
}

public class VmdCamera(BinaryReader binaryReader)
{
    public uint Frame { get; } = binaryReader.ReadUInt32();

    public float Distance { get; } = binaryReader.ReadSingle();

    public Vector3 Interest { get; } = binaryReader.ReadVector3();

    public Vector3 Rotate { get; } = binaryReader.ReadVector3();

    public byte[] Interpolation { get; } = binaryReader.ReadBytes(24);

    public uint ViewAngle { get; } = binaryReader.ReadUInt32();

    // VMD stores 0 for perspective enabled and 1 for perspective disabled.
    public bool IsPerspective { get; } = !binaryReader.ReadBoolean();
}

public class VmdLight(BinaryReader binaryReader)
{
    public uint Frame { get; } = binaryReader.ReadUInt32();

    public Vector3 Color { get; } = binaryReader.ReadVector3();

    public Vector3 Position { get; } = binaryReader.ReadVector3();
}

public class VmdShadow(BinaryReader binaryReader)
{
    public uint Frame { get; } = binaryReader.ReadUInt32();

    public ShadowType Mode { get; } = (ShadowType)binaryReader.ReadByte();

    public float Distance { get; } = binaryReader.ReadSingle();
}

public class VmdIk
{
    public class Info(BinaryReader binaryReader)
    {
        public string Name { get; } = binaryReader.ReadCString(20, BinaryReaderExtensions.ShiftJIS);

        public bool Enable { get; } = binaryReader.ReadBoolean();
    }

    public uint Frame { get; }

    public bool Show { get; }

    public Info[] Infos { get; }

    public VmdIk(BinaryReader binaryReader)
    {
        Frame = binaryReader.ReadUInt32();
        Show = binaryReader.ReadBoolean();

        Infos = new Info[binaryReader.ReadUInt32()];

        for (int i = 0; i < Infos.Length; i++)
        {
            Infos[i] = new Info(binaryReader);
        }
    }
}
#endregion

public class VmdParsing
{
    public VmdHeader Header { get; }

    public VmdMotion[] Motions { get; }

    public VmdMorph[] Morphs { get; }

    public VmdCamera[] Cameras { get; }

    public VmdLight[] Lights { get; }

    public VmdShadow[] Shadows { get; }

    public VmdIk[] Iks { get; }

    internal VmdParsing(VmdHeader header,
                        VmdMotion[] motions,
                        VmdMorph[] morphs,
                        VmdCamera[] cameras,
                        VmdLight[] lights,
                        VmdShadow[] shadows,
                        VmdIk[] iks)
    {
        Header = header;
        Motions = motions;
        Morphs = morphs;
        Cameras = cameras;
        Lights = lights;
        Shadows = shadows;
        Iks = iks;
    }

    public static VmdParsing? ParsingByFile(string path)
    {
        using BinaryReader binaryReader = new(File.OpenRead(path));

        VmdHeader header = ReadHeader(binaryReader);

        if (!header.Title.StartsWith("Vocaloid Motion Data", StringComparison.Ordinal))
        {
            return null;
        }

        return new VmdParsing(header,
                              ReadMotions(binaryReader),
                              ReadMorphs(binaryReader),
                              ReadCameras(binaryReader),
                              ReadLights(binaryReader),
                              ReadShadows(binaryReader),
                              ReadIks(binaryReader));
    }

    private static VmdHeader ReadHeader(BinaryReader binaryReader)
    {
        return new VmdHeader(binaryReader);
    }

    private static VmdMotion[] ReadMotions(BinaryReader binaryReader)
    {
        VmdMotion[] motions = new VmdMotion[binaryReader.ReadUInt32()];

        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = new VmdMotion(binaryReader);
        }

        return motions;
    }

    private static VmdMorph[] ReadMorphs(BinaryReader binaryReader)
    {
        VmdMorph[] morphs = new VmdMorph[binaryReader.ReadUInt32()];

        for (int i = 0; i < morphs.Length; i++)
        {
            morphs[i] = new VmdMorph(binaryReader);
        }

        return morphs;
    }

    private static VmdCamera[] ReadCameras(BinaryReader binaryReader)
    {
        if (!TryReadSectionCount(binaryReader, out uint count))
        {
            return [];
        }

        VmdCamera[] cameras = new VmdCamera[count];

        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i] = new VmdCamera(binaryReader);
        }

        return cameras;
    }

    private static VmdLight[] ReadLights(BinaryReader binaryReader)
    {
        if (!TryReadSectionCount(binaryReader, out uint count))
        {
            return [];
        }

        VmdLight[] lights = new VmdLight[count];

        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = new VmdLight(binaryReader);
        }

        return lights;
    }

    private static VmdShadow[] ReadShadows(BinaryReader binaryReader)
    {
        if (!TryReadSectionCount(binaryReader, out uint count))
        {
            return [];
        }

        VmdShadow[] shadows = new VmdShadow[count];

        for (int i = 0; i < shadows.Length; i++)
        {
            shadows[i] = new VmdShadow(binaryReader);
        }

        return shadows;
    }

    private static VmdIk[] ReadIks(BinaryReader binaryReader)
    {
        if (!TryReadSectionCount(binaryReader, out uint count))
        {
            return [];
        }

        VmdIk[] iks = new VmdIk[count];

        for (int i = 0; i < iks.Length; i++)
        {
            iks[i] = new VmdIk(binaryReader);
        }

        return iks;
    }

    private static bool TryReadSectionCount(BinaryReader binaryReader, out uint count)
    {
        Stream stream = binaryReader.BaseStream;
        if (!stream.CanSeek)
        {
            count = binaryReader.ReadUInt32();
            return true;
        }

        if (stream.Length - stream.Position < sizeof(uint))
        {
            count = 0;
            return false;
        }

        count = binaryReader.ReadUInt32();
        return true;
    }
}

