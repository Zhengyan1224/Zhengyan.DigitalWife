namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Advances one resource step per frame and preserves failures.</summary>
internal sealed class AndroidSceneLoading
{
    private readonly Queue<(string Message, Action Action)> _steps = new();
    private int _total;
    private int _completed;
    private int _delayFrames = 2;
    private bool _started;

    public bool IsReady { get; private set; }
    public Exception? Error { get; private set; }
    public string Message { get; private set; } = "Loading scene...";
    public float Progress => IsReady ? 1.0f : _total == 0 ? 0.0f : (float)_completed / _total;

    public void Enqueue(string message, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_started) throw new InvalidOperationException("Loading steps must be registered before loading starts.");
        _steps.Enqueue((message, action));
        _total++;
    }

    public void Advance()
    {
        if (IsReady || Error is not null) return;
        _started = true;
        if (_delayFrames-- > 0) return;
        if (!_steps.TryDequeue(out var step))
        {
            // The preceding frame presents 100% before the scene is revealed.
            Message = "Loading complete";
            IsReady = true;
            return;
        }

        Message = step.Message;
        try
        {
            step.Action();
            _completed++;
        }
        catch (Exception ex)
        {
            Error = ex;
            Message = $"Load failed: {step.Message}: {ex.Message}";
            _steps.Clear();
        }
    }
}
