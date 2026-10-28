// Native playback boundaries for exercising the real AndroidAudioHost on desktop.
namespace Android.Media;

public enum AudioContentType { Music }
public enum AudioUsageKind { Game }

public sealed class AudioAttributes
{
    public sealed class Builder
    {
        public Builder SetContentType(AudioContentType value) => this;
        public Builder SetUsage(AudioUsageKind value) => this;
        public AudioAttributes Build() => new();
    }
}

public sealed class MediaPlayer : IDisposable
{
    public static List<MediaPlayer> Instances { get; } = [];
    public static bool FailNextPrepare { get; set; }
    public static bool FailNextStart { get; set; }
    public MediaPlayer() => Instances.Add(this);
    public bool Looping { get; set; }
    public bool IsPlaying { get; private set; }
    public int CurrentPosition { get; private set; }
    public int Duration => 1000;
    public float Volume { get; private set; }
    public string? Path { get; private set; }
    public int SeekCount { get; private set; }
    public int ReleaseCount { get; private set; }
    public int DisposeCount { get; private set; }
    public event EventHandler? Completion;
    public event EventHandler<ErrorEventArgs>? Error;
    public void SetAudioAttributes(AudioAttributes attributes) { }
    public void SetDataSource(string path) => Path = path;
    public void SetVolume(float left, float right) => Volume = left;
    public void Prepare()
    {
        if (FailNextPrepare)
        {
            FailNextPrepare = false;
            throw new InvalidOperationException("Decoder preparation failed.");
        }
    }
    public void SeekTo(int position) { CurrentPosition = position; SeekCount++; }
    public void Start()
    {
        if (FailNextStart)
        {
            FailNextStart = false;
            throw new InvalidOperationException("Native playback start failed.");
        }
        IsPlaying = true;
    }
    public void Pause() => IsPlaying = false;
    public void Stop() => IsPlaying = false;
    public void Release() { IsPlaying = false; ReleaseCount++; }
    public void Dispose() => DisposeCount++;
    public void Complete() { IsPlaying = false; Completion?.Invoke(this, EventArgs.Empty); }
    public bool Fail()
    {
        ErrorEventArgs args = new();
        Error?.Invoke(this, args);
        return args.Handled;
    }
    public sealed class ErrorEventArgs : EventArgs
    {
        public bool Handled { get; set; }
    }
}
