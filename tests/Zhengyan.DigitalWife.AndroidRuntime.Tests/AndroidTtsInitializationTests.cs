using Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidTtsInitializationTests
{
    public static void TestRetry()
    {
        AndroidTtsInitialization state = new(TimeSpan.FromSeconds(5));
        TaskCompletionSource<bool> native = NewCompletion();
        int starts = 0;
        Task Start() { starts++; return native.Task; }
        Task first = state.EnsureReadyAsync(Start);
        Task concurrent = state.EnsureReadyAsync(Start);
        Check(starts == 1 && !state.IsReady, "Concurrent speech requests started separate engines.");
        native.SetException(new InvalidOperationException("Engine unavailable"));
        Expect<InvalidOperationException>(first);
        Expect<InvalidOperationException>(concurrent);
        Check(!state.IsReady, "Failed initialization was treated as ready.");

        native = NewCompletion();
        Task retry = state.EnsureReadyAsync(Start);
        Check(starts == 2 && !retry.IsCompleted, "A failed engine initialization was cached permanently.");
        native.SetResult(true);
        retry.GetAwaiter().GetResult();
        state.EnsureReadyAsync(Start).GetAwaiter().GetResult();
        Check(state.IsReady && starts == 2, "A ready engine should be reused.");
        state.Invalidate();
        Check(!state.IsReady, "Disconnected engine remained ready.");
        state.EnsureReadyAsync(Start).GetAwaiter().GetResult();
        Check(starts == 3, "A disconnected engine was not initialized again.");
    }

    public static void TestCancellation()
    {
        AndroidTtsInitialization state = new(TimeSpan.FromSeconds(5));
        TaskCompletionSource<bool> native = NewCompletion();
        using CancellationTokenSource canceled = new();
        int starts = 0;
        Task Start() { starts++; return native.Task; }
        Task first = state.EnsureReadyAsync(Start, canceled.Token);
        Task second = state.EnsureReadyAsync(Start);
        canceled.Cancel();
        Expect<OperationCanceledException>(first);
        Check(starts == 1 && !second.IsCompleted, "Canceling one caller interrupted another caller's initialization.");
        native.SetResult(true);
        second.GetAwaiter().GetResult();
        Check(state.IsReady, "Uncanceled caller did not finish initialization.");
    }

    public static void TestTimeout()
    {
        AndroidTtsInitialization state = new(TimeSpan.FromMilliseconds(50));
        TaskCompletionSource<bool> stale = NewCompletion();
        Expect<TimeoutException>(state.EnsureReadyAsync(() => stale.Task));
        Check(!state.IsReady, "Timed-out engine was treated as ready.");
        TaskCompletionSource<bool> current = NewCompletion();
        Task retry = state.EnsureReadyAsync(() => current.Task);
        stale.SetResult(true);
        Check(!retry.IsCompleted && !state.IsReady, "A late callback completed the replacement initialization.");
        current.SetResult(true);
        retry.GetAwaiter().GetResult();
        Check(state.IsReady, "Initialization did not recover from timeout.");
    }

    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Expect<T>(Task task) where T : Exception
    {
        try { task.GetAwaiter().GetResult(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
