using Microsoft.Extensions.Logging;

namespace InfraGate.Observer.Tests;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> entries = [];

    public IReadOnlyList<LogEntry> Entries => entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kvp in kvps)
            {
                properties[kvp.Key] = kvp.Value;
            }
        }

        entries.Add(new LogEntry(logLevel, message, properties, exception));
    }

    internal sealed record class LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);
}
