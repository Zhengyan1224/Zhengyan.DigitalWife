namespace Zhengyan.DigitalWife.GamePlayer.Android;

/// <summary>Shares one initialization attempt; failures can be retried by the next request.</summary>
internal sealed class AndroidTtsInitialization(TimeSpan timeout)
{
    private readonly object _gate = new();
    private Task? _attempt;

    public bool IsReady
    {
        get { lock (_gate) return _attempt?.IsCompletedSuccessfully == true; }
    }

    public Task EnsureReadyAsync(Func<Task> initialize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task attempt;
        lock (_gate)
        {
            if (_attempt is null || _attempt.IsFaulted || _attempt.IsCanceled)
                _attempt = InitializeAsync(initialize);
            attempt = _attempt;
        }
        // Cancel the caller's wait, not the initialization shared by other requests.
        return attempt.WaitAsync(cancellationToken);
    }

    public void Invalidate()
    {
        lock (_gate) _attempt = null;
    }

    private async Task InitializeAsync(Func<Task> initialize)
    {
        await initialize().WaitAsync(timeout).ConfigureAwait(false);
    }
}
