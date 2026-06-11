using System.Runtime.CompilerServices;
using System.Text;

namespace Zhengyan.DigitalWife.Audio;

public enum AudioEncoding
{
    Float32,
    Pcm16
}

public sealed record AudioFormat(int SampleRate, int Channels, AudioEncoding Encoding = AudioEncoding.Float32)
{
    public int BytesPerSample => Encoding switch
    {
        AudioEncoding.Float32 => sizeof(float),
        AudioEncoding.Pcm16 => sizeof(short),
        _ => throw new ArgumentOutOfRangeException(nameof(Encoding), Encoding, null)
    };

    public int BytesPerFrame => BytesPerSample * Channels;
}

public sealed record AudioChunk(ReadOnlyMemory<float> Samples, AudioFormat Format, TimeSpan Offset, bool IsFinal = false)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / Format.SampleRate / Format.Channels);
}

public sealed class AudioData
{
    public AudioData(float[] samples, AudioFormat format)
    {
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    public float[] Samples { get; }

    public AudioFormat Format { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / Format.SampleRate / Format.Channels);

    public AudioData ToMono()
    {
        if (Format.Channels == 1)
        {
            return this;
        }

        var frames = Samples.Length / Format.Channels;
        var mono = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < Format.Channels; channel++)
            {
                sum += Samples[(frame * Format.Channels) + channel];
            }

            mono[frame] = sum / Format.Channels;
        }

        return new AudioData(mono, new AudioFormat(Format.SampleRate, 1, AudioEncoding.Float32));
    }

    public AudioData Resample(int targetSampleRate)
    {
        if (targetSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        if (Format.SampleRate == targetSampleRate)
        {
            return this;
        }

        var channels = Format.Channels;
        var sourceFrames = Samples.Length / channels;
        var targetFrames = Math.Max(1, (int)Math.Round(sourceFrames * (targetSampleRate / (double)Format.SampleRate)));
        var resampled = new float[targetFrames * channels];
        var ratio = Format.SampleRate / (double)targetSampleRate;

        for (var channel = 0; channel < channels; channel++)
        {
            for (var targetFrame = 0; targetFrame < targetFrames; targetFrame++)
            {
                var sourcePosition = targetFrame * ratio;
                var sourceIndex = (int)Math.Floor(sourcePosition);
                var nextIndex = Math.Min(sourceIndex + 1, sourceFrames - 1);
                var fraction = (float)(sourcePosition - sourceIndex);

                var left = Samples[(sourceIndex * channels) + channel];
                var right = Samples[(nextIndex * channels) + channel];

                resampled[(targetFrame * channels) + channel] = left + ((right - left) * fraction);
            }
        }

        return new AudioData(resampled, new AudioFormat(targetSampleRate, channels, AudioEncoding.Float32));
    }

    public async IAsyncEnumerable<AudioChunk> ToChunks(
        int chunkSampleCount = 4096,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (chunkSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSampleCount));
        }

        for (var offset = 0; offset < Samples.Length; offset += chunkSampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(chunkSampleCount, Samples.Length - offset);
            var timeOffset = TimeSpan.FromSeconds((double)offset / Format.SampleRate / Format.Channels);
            var chunk = new float[count];
            Array.Copy(Samples, offset, chunk, 0, count);
            yield return new AudioChunk(chunk, Format, timeOffset, offset + count >= Samples.Length);
            await Task.Yield();
        }
    }
}

public class AudioCaptureOptions
{
    public int? DeviceIndex { get; init; }

    public int SampleRate { get; init; } = 16_000;

    public int Channels { get; init; } = 1;

    public uint FramesPerBuffer { get; init; } = 512;

    public string? WaveFilePath { get; init; }
}

public class VoiceActivityCaptureOptions : AudioCaptureOptions
{
    public TimeSpan PreRoll { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMilliseconds(800);

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromMilliseconds(900);

    public float SilenceThreshold { get; init; } = 0.015f;
}

public static class WaveFile
{
    public static async Task WriteAsync(
        string path,
        AudioData audio,
        AudioEncoding encoding = AudioEncoding.Pcm16,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(audio);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await WriteAsync(stream, audio, encoding, cancellationToken);
    }

    public static async Task WriteAsync(
        Stream stream,
        AudioData audio,
        AudioEncoding encoding = AudioEncoding.Pcm16,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(audio);
        if (!stream.CanWrite)
        {
            throw new InvalidOperationException("The target stream is not writable.");
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        var bytesPerSample = encoding == AudioEncoding.Pcm16 ? sizeof(short) : sizeof(float);
        var dataSize = audio.Samples.Length * bytesPerSample;
        var audioFormatCode = encoding == AudioEncoding.Pcm16 ? (short)1 : (short)3;
        var byteRate = audio.Format.SampleRate * audio.Format.Channels * bytesPerSample;
        var blockAlign = (short)(audio.Format.Channels * bytesPerSample);
        var bitsPerSample = (short)(bytesPerSample * 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write(audioFormatCode);
        writer.Write((short)audio.Format.Channels);
        writer.Write(audio.Format.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        if (encoding == AudioEncoding.Pcm16)
        {
            foreach (var sample in audio.Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clamped = Math.Clamp(sample, -1f, 1f);
                writer.Write((short)Math.Round(clamped * short.MaxValue));
            }
        }
        else
        {
            foreach (var sample in audio.Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(sample);
            }
        }

        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<AudioData> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadAsync(stream, cancellationToken);
    }

    public static async Task<AudioData> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new InvalidOperationException("The source stream is not readable.");
        }

        if (!stream.CanSeek)
        {
            await using MemoryStream buffered = new();
            await stream.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            return await ReadAsync(buffered, cancellationToken);
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var riff = new string(reader.ReadChars(4));
        if (!string.Equals(riff, "RIFF", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid wave file header.");
        }

        _ = reader.ReadInt32();
        var wave = new string(reader.ReadChars(4));
        if (!string.Equals(wave, "WAVE", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid wave file format.");
        }

        short audioFormat = 0;
        short channels = 0;
        var sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    _ = reader.ReadInt32();
                    _ = reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    if (chunkSize > 16)
                    {
                        reader.ReadBytes(chunkSize - 16);
                    }
                    break;

                case "data":
                    data = reader.ReadBytes(chunkSize);
                    break;

                default:
                    reader.ReadBytes(chunkSize);
                    break;
            }

            if (data is not null && sampleRate > 0)
            {
                break;
            }
        }

        if (data is null || sampleRate <= 0 || channels <= 0)
        {
            throw new InvalidDataException("Wave file is missing required fmt/data chunks.");
        }

        var samples = (audioFormat, bitsPerSample) switch
        {
            (1, 16) => ReadPcm16(data),
            (3, 32) => ReadFloat32(data),
            _ => throw new NotSupportedException($"Unsupported wave encoding format={audioFormat}, bits={bitsPerSample}.")
        };

        return new AudioData(samples, new AudioFormat(sampleRate, channels, AudioEncoding.Float32));
    }

    private static float[] ReadPcm16(byte[] data)
    {
        var sampleCount = data.Length / sizeof(short);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToInt16(data, i * sizeof(short)) / (float)short.MaxValue;
        }

        return samples;
    }

    private static float[] ReadFloat32(byte[] data)
    {
        var sampleCount = data.Length / sizeof(float);
        var samples = new float[sampleCount];
        Buffer.BlockCopy(data, 0, samples, 0, data.Length);
        return samples;
    }
}

