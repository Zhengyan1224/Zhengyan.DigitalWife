using Android.Media;
using Android.Util;
using System.Threading.Channels;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

public sealed class AndroidPcmAudioStream : IDisposable
{
    private const string LogTag = "ZhengyanGamePlayer";
    private readonly object _sync = new();
    private int _sampleRate;
    private readonly Channel<byte[]> _playback = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private AudioRecord? _record;
    private AudioTrack? _track;
    private CancellationTokenSource? _captureCts;
    private CancellationTokenSource? _playbackCts;
    private Task? _captureTask;
    private Task? _playbackTask;
    private bool _disposed;
    public AndroidPcmAudioStream(int sampleRate = 24000, int channels = 1)
    {
        _sampleRate = Math.Clamp(sampleRate, 8000, 48000);
        ChannelCount = channels == 2 ? 2 : 1;
    }
    public int SampleRate => _sampleRate;
    public int ChannelCount { get; private set; }
    public bool IsCapturing => _captureTask is { IsCompleted: false };
    public bool IsPlaying => _playbackTask is { IsCompleted: false };
    public event Action<ReadOnlyMemory<byte>>? PcmCaptured;

    public void Reconfigure(int sampleRate, int channels = 1)
    {
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AndroidPcmAudioStream));
            StopCapture();
            StopPlayback();
            while (_playback.Reader.TryRead(out _)) { }
            _sampleRate = Math.Clamp(sampleRate, 8000, 48000);
            ChannelCount = channels == 2 ? 2 : 1;
        }
    }
    public bool StartCapture()
    {
        lock (_sync)
        {
            if (_disposed || IsCapturing) return false;
            ChannelIn channelConfig = ChannelCount == 1 ? ChannelIn.Mono : ChannelIn.Stereo;
            int minimum = AudioRecord.GetMinBufferSize(_sampleRate, channelConfig, Encoding.Pcm16bit);
            if (minimum <= 0) return false;
            _record = new AudioRecord(AudioSource.Mic, _sampleRate, channelConfig, Encoding.Pcm16bit, Math.Max(minimum * 2, 2048));
            _captureCts = new CancellationTokenSource(); CancellationToken token = _captureCts.Token;
            _record.StartRecording();
            _captureTask = Task.Run(() => CaptureLoop(token), token);
            return true;
        }
    }
    public void StopCapture()
    {
        Task? task;
        lock (_sync) { _captureCts?.Cancel(); try { _record?.Stop(); } catch { } task = _captureTask; }
        try { task?.Wait(TimeSpan.FromSeconds(1)); } catch { }
    }
    public bool StartPlayback()
    {
        lock (_sync)
        {
            if (_disposed || IsPlaying) return false;
            ChannelOut channelConfig = ChannelCount == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
            int minimum = AudioTrack.GetMinBufferSize(_sampleRate, channelConfig, Encoding.Pcm16bit);
            if (minimum <= 0) return false;
            _track = new AudioTrack(new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.VoiceCommunication)!.SetContentType(AudioContentType.Speech)!.Build(), new AudioFormat.Builder()!.SetSampleRate(_sampleRate)!.SetEncoding(Encoding.Pcm16bit)!.SetChannelMask(channelConfig)!.Build(), Math.Max(minimum * 2, 2048), AudioTrackMode.Stream, AudioManager.AudioSessionIdGenerate);
            _playbackCts = new CancellationTokenSource(); _track.Play();
            _playbackTask = Task.Run(() => PlaybackLoop(_playbackCts.Token), _playbackCts.Token);
            return true;
        }
    }
    public void QueuePlayback(ReadOnlySpan<byte> pcm16)
    {
        if (pcm16.Length == 0 || _disposed) return;
        _playback.Writer.TryWrite(pcm16.ToArray());
    }
    public void StopPlayback()
    {
        Task? task;
        lock (_sync) { _playbackCts?.Cancel(); try { _track?.Pause(); _track?.Flush(); } catch { } task = _playbackTask; }
        try { task?.Wait(TimeSpan.FromSeconds(1)); } catch { }
    }
    public void StopAll() { StopCapture(); StopPlayback(); }
    public void Dispose() { if (_disposed) return; _disposed = true; StopAll(); try { _record?.Release(); _track?.Release(); } catch (Exception ex) { Log.Warn(LogTag, ex.Message); } _record = null; _track = null; _playback.Writer.TryComplete(); }
    private void CaptureLoop(CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        while (!token.IsCancellationRequested && _record is { } record)
        {
            int read = record.Read(buffer, 0, buffer.Length); if (read <= 0) continue;
            try { PcmCaptured?.Invoke(buffer.AsMemory(0, read)); } catch (Exception ex) { Log.Warn(LogTag, $"PCM capture callback failed: {ex.Message}"); }
        }
    }
    private async Task PlaybackLoop(CancellationToken token)
    {
        try { await foreach (byte[] data in _playback.Reader.ReadAllAsync(token).ConfigureAwait(false)) { if (_track is { } track && data.Length > 0) track.Write(data, 0, data.Length); } } catch (OperationCanceledException) { }
    }
}
