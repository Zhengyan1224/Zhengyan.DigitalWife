using System.Text;
using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

internal sealed class PortAudioWaveWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly AudioFormat _format;
    private int _sampleCount;
    private bool _disposed;

    public PortAudioWaveWriter(string path, AudioFormat format)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        _format = format;
        WriteHeaderPlaceholder();
    }

    public void Write(float[] samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var sample in samples)
        {
            _writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        }

        _sampleCount += samples.Length;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FinalizeHeader();
        _writer.Dispose();
        _stream.Dispose();
    }

    private void WriteHeaderPlaceholder()
    {
        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        _writer.Write(Encoding.ASCII.GetBytes("fmt "));
        _writer.Write(16);
        _writer.Write((short)1);
        _writer.Write((short)_format.Channels);
        _writer.Write(_format.SampleRate);
        _writer.Write(_format.SampleRate * _format.Channels * sizeof(short));
        _writer.Write((short)(_format.Channels * sizeof(short)));
        _writer.Write((short)(sizeof(short) * 8));
        _writer.Write(Encoding.ASCII.GetBytes("data"));
        _writer.Write(0);
    }

    private void FinalizeHeader()
    {
        var dataBytes = _sampleCount * sizeof(short);
        _stream.Position = 4;
        _writer.Write(36 + dataBytes);
        _stream.Position = 40;
        _writer.Write(dataBytes);
        _stream.Flush();
    }
}

