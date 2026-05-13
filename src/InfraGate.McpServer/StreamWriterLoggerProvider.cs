using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

internal sealed class StreamWriterLoggerProvider : ILoggerProvider
{
    private readonly Lock writeLock = new();
    private readonly StreamWriter writer;

    public StreamWriterLoggerProvider(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) =>
        new StreamWriterLogger(categoryName, writer, writeLock);

    public void Dispose()
    {
        writer.Dispose();
    }
}

internal sealed class StreamWriterLogger : ILogger
{
    private readonly string categoryName;
    private readonly StreamWriter writer;
    private readonly Lock writeLock;

    public StreamWriterLogger(string categoryName, StreamWriter writer, Lock writeLock)
    {
        this.categoryName = categoryName;
        this.writer = writer;
        this.writeLock = writeLock;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);

        lock (writeLock)
        {
            writer.WriteLine($"[{DateTime.UtcNow:O}] [{logLevel,-11}] {categoryName}: {message}");
            if (exception is not null)
            {
                writer.WriteLine(exception.ToString());
            }
        }
    }
}
