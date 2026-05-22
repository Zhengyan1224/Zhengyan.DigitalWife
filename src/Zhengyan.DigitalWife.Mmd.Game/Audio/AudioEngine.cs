using System.Numerics;
using System.Runtime.InteropServices;
using NVorbis;
using Silk.NET.OpenAL;

namespace Zhengyan.DigitalWife.Mmd.Game.Audio;

public sealed unsafe class AudioEngine : IDisposable
{
    private readonly AL _al;
    private readonly AudioContext _context;
    private readonly List<AudioSource> _oneShotSources = [];
    private bool _disposed;

    public AudioEngine()
    {
        if (!IsOpenAlAvailable())
        {
            throw new PlatformNotSupportedException("OpenAL native library was not found.");
        }

        _context = new AudioContext();
        _al = AL.GetApi();

        SetListenerPosition(Vector3.Zero);
    }

    public void SetListenerPosition(Vector3 position)
    {
        _al.SetListenerProperty(ListenerVector3.Position, position);
    }

    public void SetListenerGain(float gain)
    {
        _al.SetListenerProperty(ListenerFloat.Gain, Math.Clamp(gain, 0.0f, 4.0f));
    }

    public AudioClip LoadClip(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Audio file not found: {fullPath}", fullPath);
        }

        AudioData audioData = LoadAudioData(fullPath);

        uint bufferId = _al.GenBuffer();
        GCHandle handle = GCHandle.Alloc(audioData.PcmData, GCHandleType.Pinned);
        try
        {
            _al.BufferData(
                bufferId,
                audioData.Format,
                handle.AddrOfPinnedObject().ToPointer(),
                audioData.PcmData.Length,
                audioData.SampleRate);
        }
        finally
        {
            handle.Free();
        }

        return new AudioClip(_al, bufferId, Path.GetFileName(fullPath), audioData.Channels, audioData.SampleRate, audioData.Duration);
    }

    public AudioClip CreateClip(
        string? name,
        ReadOnlySpan<float> samples,
        int sampleRate,
        int channels)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        short[] pcm16Samples = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            pcm16Samples[i] = (short)MathF.Round(Math.Clamp(samples[i], -1.0f, 1.0f) * short.MaxValue);
        }

        byte[] pcm16 = new byte[pcm16Samples.Length * sizeof(short)];
        Buffer.BlockCopy(pcm16Samples, 0, pcm16, 0, pcm16.Length);
        return CreateClip(name, pcm16, sampleRate, channels);
    }

    public AudioClip CreateClip(
        string? name,
        ReadOnlySpan<byte> pcm16,
        int sampleRate,
        int channels)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        uint bufferId = _al.GenBuffer();
        fixed (byte* pcmPtr = pcm16)
        {
            _al.BufferData(
                bufferId,
                GetBufferFormat(channels),
                pcmPtr,
                pcm16.Length,
                sampleRate);
        }

        int bytesPerFrame = sizeof(short) * channels;
        double seconds = pcm16.Length / (double)(sampleRate * bytesPerFrame);
        return new AudioClip(_al, bufferId, name, channels, sampleRate, TimeSpan.FromSeconds(seconds));
    }

    public AudioSource CreateSource(AudioClip? clip = null)
    {
        uint sourceId = _al.GenSource();
        return new AudioSource(_al, sourceId, clip);
    }

    public AudioSource PlayOneShot(AudioClip clip, float volume = 1.0f, Vector3? position = null)
    {
        AudioSource source = CreateSource(clip);
        source.Volume = volume;
        if (position is Vector3 value)
        {
            source.Position = value;
        }

        source.Play();
        _oneShotSources.Add(source);
        return source;
    }

    public void Update()
    {
        for (int i = _oneShotSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = _oneShotSources[i];
            if (source.State == SourceState.Stopped)
            {
                source.Dispose();
                _oneShotSources.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (AudioSource source in _oneShotSources)
        {
            source.Dispose();
        }

        _oneShotSources.Clear();
        _al.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AudioData LoadAudioData(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => LoadWave(path),
            ".ogg" => LoadOgg(path),
            _ => throw new NotSupportedException($"Unsupported audio format: {Path.GetExtension(path)}. Supported formats are .wav and .ogg.")
        };
    }

    private static AudioData LoadWave(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("Invalid WAV file header.");
        }

        _ = reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Invalid WAV file type.");
        }

        short audioFormat = 0;
        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            string chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    _ = reader.ReadInt32();
                    _ = reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    reader.BaseStream.Position += Math.Max(0, chunkSize - 16);
                    break;
                case "data":
                    data = reader.ReadBytes(chunkSize);
                    break;
                default:
                    reader.BaseStream.Position += chunkSize;
                    break;
            }
        }

        if (data is null || channels <= 0 || sampleRate <= 0)
        {
            throw new InvalidDataException("Incomplete WAV file.");
        }

        byte[] pcm16 = audioFormat switch
        {
            1 when bitsPerSample == 8 => ConvertPcm8ToPcm16(data),
            1 when bitsPerSample == 16 => data,
            3 when bitsPerSample == 32 => ConvertFloat32ToPcm16(data),
            _ => throw new NotSupportedException($"Unsupported WAV encoding. Format={audioFormat}, BitsPerSample={bitsPerSample}.")
        };

        int bytesPerSample = sizeof(short) * channels;
        double seconds = pcm16.Length / (double)(sampleRate * bytesPerSample);
        return new AudioData(GetBufferFormat(channels), channels, sampleRate, pcm16, TimeSpan.FromSeconds(seconds));
    }

    private static AudioData LoadOgg(string path)
    {
        using VorbisReader reader = new(path);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;

        List<short> samples = [];
        float[] buffer = new float[reader.SampleRate * reader.Channels];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                float sample = Math.Clamp(buffer[i], -1.0f, 1.0f);
                samples.Add((short)MathF.Round(sample * short.MaxValue));
            }
        }

        byte[] pcm16 = new byte[samples.Count * sizeof(short)];
        Buffer.BlockCopy(samples.ToArray(), 0, pcm16, 0, pcm16.Length);
        return new AudioData(GetBufferFormat(channels), channels, sampleRate, pcm16, reader.TotalTime);
    }

    private static byte[] ConvertPcm8ToPcm16(byte[] pcm8)
    {
        byte[] pcm16 = new byte[pcm8.Length * sizeof(short)];
        for (int i = 0; i < pcm8.Length; i++)
        {
            short sample = (short)((pcm8[i] - 128) << 8);
            BitConverter.TryWriteBytes(pcm16.AsSpan(i * sizeof(short), sizeof(short)), sample);
        }

        return pcm16;
    }

    private static byte[] ConvertFloat32ToPcm16(byte[] pcmFloat)
    {
        byte[] pcm16 = new byte[(pcmFloat.Length / sizeof(float)) * sizeof(short)];
        for (int i = 0, j = 0; i < pcmFloat.Length; i += sizeof(float), j += sizeof(short))
        {
            float sample = BitConverter.ToSingle(pcmFloat, i);
            short value = (short)MathF.Round(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
            BitConverter.TryWriteBytes(pcm16.AsSpan(j, sizeof(short)), value);
        }

        return pcm16;
    }

    private static BufferFormat GetBufferFormat(int channels)
    {
        return channels switch
        {
            1 => BufferFormat.Mono16,
            2 => BufferFormat.Stereo16,
            _ => throw new NotSupportedException($"Unsupported channel count: {channels}.")
        };
    }

    private static bool IsOpenAlAvailable()
    {
        string[] libraryNames = OperatingSystem.IsWindows()
            ? ["openal32", "OpenAL32", "openal32.dll", "OpenAL32.dll"]
            : OperatingSystem.IsMacOS()
                ? ["OpenAL", "libopenal.dylib"]
                : ["openal", "libopenal.so.1", "libopenal.so"];

        foreach (string libraryName in libraryNames)
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, libraryName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }

        foreach (string libraryName in libraryNames)
        {
            if (NativeLibrary.TryLoad(libraryName, out nint handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }

        return false;
    }

    private readonly record struct AudioData(
        BufferFormat Format,
        int Channels,
        int SampleRate,
        byte[] PcmData,
        TimeSpan Duration);
}

