using Microsoft.Extensions.Logging;

namespace Infrastructure.Domino;

public sealed class GameFileLoggerProvider(string outputPath = "output.log") : ILoggerProvider
{
    private readonly object synchronization = new();
    private bool gameStarted;

    public void BeginGame()
    {
        lock (synchronization)
        {
            File.WriteAllText(outputPath, string.Empty);
            gameStarted = true;
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new GameFileLogger(this, categoryName == typeof(LoggingGameEventSink).FullName);

    public void Dispose()
    {
    }

    private void WriteMessage(string message)
    {
        lock (synchronization)
        {
            if (gameStarted)
            {
                File.AppendAllText(outputPath, message + Environment.NewLine);
            }
        }
    }

    private sealed class GameFileLogger(GameFileLoggerProvider provider, bool isGameCategory) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => isGameCategory && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && eventId.Id is 1001 or 1002)
            {
                provider.WriteMessage(formatter(state, exception));
            }
        }
    }
}