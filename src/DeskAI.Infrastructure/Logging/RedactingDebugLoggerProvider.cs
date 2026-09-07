using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DeskAI.Infrastructure.Logging;

public sealed class RedactingDebugLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RedactingDebugLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class RedactingDebugLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            Debug.WriteLine($"[{logLevel}] {categoryName}: {SensitiveDataRedactor.Redact(message)}");
            if (exception is not null)
            {
                Debug.WriteLine(SensitiveDataRedactor.Redact(exception.GetType().Name + ": " + exception.Message));
            }
        }
    }
}
