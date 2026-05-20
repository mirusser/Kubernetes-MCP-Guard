using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway.Tests;

/// <summary>
/// Minimal ILogger&lt;T&gt; that collects all formatted log messages for assertion in tests.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> messages = [];

    /// <summary>All formatted log messages written during the test.</summary>
    public IReadOnlyList<string> Messages => messages;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        messages.Add(formatter(state, exception));
    }
}
