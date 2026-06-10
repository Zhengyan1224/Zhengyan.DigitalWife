namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class MainThreadDispatcher
{
    private readonly Queue<PendingAction> _queue = [];
    private readonly object _sync = new();
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

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
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            action();
            return Task.CompletedTask;
        }

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _queue.Enqueue(new PendingAction(action, completion));
        }

        return completion.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            return Task.FromResult(action());
        }

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _queue.Enqueue(new PendingAction(
                () =>
                {
                    T result = action();
                    completion.TrySetResult(result);
                },
                null,
                exception => completion.TrySetException(exception)));
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
                pending.OnError?.Invoke(ex);
            }
        }
    }

    private sealed record PendingAction(Action Action, TaskCompletionSource<bool>? Completion, Action<Exception>? OnError = null);
}
