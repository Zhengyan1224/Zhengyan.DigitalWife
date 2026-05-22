namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class MainThreadDispatcher
{
    private readonly Queue<Action> _queue = [];
    private readonly object _sync = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sync)
        {
            _queue.Enqueue(action);
        }
    }

    public void Pump()
    {
        while (true)
        {
            Action? action;
            lock (_sync)
            {
                action = _queue.Count == 0 ? null : _queue.Dequeue();
            }

            if (action is null)
            {
                return;
            }

            action();
        }
    }
}
