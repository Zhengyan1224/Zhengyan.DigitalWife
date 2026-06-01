namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class MainThreadDispatcher
{
    private readonly Queue<PendingAction> _queue = [];
    private readonly object _sync = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sync)
        {
            _queue.Enqueue(new PendingAction(action, null));
        }
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _queue.Enqueue(new PendingAction(action, completion));
        }

        return completion.Task;
    }

    public void Pump()
    {
        while (true)
        {
            PendingAction? pending;
            lock (_sync)
            {
                pending = _queue.Count == 0 ? null : _queue.Dequeue();
            }

            if (pending is null)
            {
                return;
            }

            try
            {
                pending.Action();
                pending.Completion?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                pending.Completion?.TrySetException(ex);
            }
        }
    }

    private sealed record PendingAction(Action Action, TaskCompletionSource<bool>? Completion);
}
