using Microsoft.Extensions.Logging;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal sealed class AndroidRuntimeLoggerFactory : ILoggerFactory
{
    public static AndroidRuntimeLoggerFactory Instance { get; } = new();
    private const string LogTag = "ZhengyanGamePlayer";

    public ILogger CreateLogger(string categoryName) => new AndroidLogger(categoryName);
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
    public void Dispose() { }

    private sealed class AndroidLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message = $"{categoryName}: {formatter(state, exception)}";
            if (exception is not null) message += Environment.NewLine + exception;
            // Keep exception details within logcat's per-entry limit.
            const int chunkSize = 900;
            for (int offset = 0; offset < message.Length; offset += chunkSize)
            {
                string chunk = message.Substring(offset, Math.Min(chunkSize, message.Length - offset));
                if (logLevel >= LogLevel.Error) global::Android.Util.Log.Error(LogTag, chunk);
                else if (logLevel == LogLevel.Warning) global::Android.Util.Log.Warn(LogTag, chunk);
                else global::Android.Util.Log.Info(LogTag, chunk);
            }
        }
    }
}
