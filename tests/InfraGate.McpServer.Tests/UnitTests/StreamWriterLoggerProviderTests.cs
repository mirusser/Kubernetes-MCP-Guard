using Microsoft.Extensions.Logging;
using InfraGate.McpServer;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class StreamWriterLoggerProviderTests : IDisposable
{
    private readonly string tempPath = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"), "test.log");

    [Fact]
    public void CreateLogger_ReturnsStreamWriterLogger()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);

        var logger = provider.CreateLogger("TestCategory");

        Assert.NotNull(logger);
    }

    [Fact]
    public void Constructor_CreatesDirectory_WhenDirectoryDoesNotExist()
    {
        string nonExistentDir = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(nonExistentDir, "test.log");

        using var provider = new StreamWriterLoggerProvider(logPath);

        Assert.True(Directory.Exists(nonExistentDir));
    }

    [Fact]
    public void Log_WritesTimestampAndLevelAndCategoryAndMessage()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        // Justification: CA1873 — test assertions depend on log output; logging is always enabled during tests.
        logger.LogInformation("Hello {Name}", "World");

        provider.Dispose();
        string line = File.ReadLines(tempPath).First();
        Assert.Contains("[Information] TestCategory: Hello World", line);
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z\]", line);
    }

    [Fact]
    public void Log_WritesExceptionToString_WhenExceptionNotNull()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");
        var exception = new InvalidOperationException("test exception");

        logger.LogError(exception, "An error occurred");

        provider.Dispose();
        string[] lines = File.ReadLines(tempPath).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"\[Error\s+\] TestCategory: An error occurred", lines[0]);
        Assert.Contains("test exception", lines[1]);
    }

    [Fact]
    public void Log_DoesNotAppendExceptionLine_WhenExceptionNull()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogWarning("A warning message");

        provider.Dispose();
        string[] lines = File.ReadLines(tempPath).ToArray();
        Assert.Single(lines);
        Assert.Matches(@"\[Warning\s+\] TestCategory: A warning message", lines[0]);
    }

    [Fact]
    public void Log_IsThreadSafe()
    {
        const int threadCount = 8;
        const int messagesPerThread = 5;
        using var provider = new StreamWriterLoggerProvider(tempPath);

        var threads = Enumerable.Range(0, threadCount)
            .Select(threadIndex => new Thread(() =>
            {
                var logger = provider.CreateLogger($"Category{threadIndex}");
                for (int i = 0; i < messagesPerThread; i++)
                {
                        // Justification: CA1873 — test assertions depend on log output; logging is always enabled during tests.
                    logger.LogInformation("Message {Index}", i);
                }
            }))
            .ToArray();

        foreach (Thread thread in threads)
        {
            thread.Start();
        }
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        provider.Dispose();
        string[] lines = File.ReadLines(tempPath).ToArray();
        Assert.Equal(threadCount * messagesPerThread, lines.Length);
    }

    [Fact]
    public void Log_RespectsIsEnabled_WhenLogLevelNone()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        logger.Log(LogLevel.None, default, "state", null, (s, _) => "should not appear");

        provider.Dispose();
        string content = File.ReadAllText(tempPath);
        Assert.Empty(content);
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        var scope = logger.BeginScope("test");

        Assert.Null(scope);
    }

    [Fact]
    public void Dispose_FlushesAndClosesWriter()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");
        logger.LogInformation("Before dispose");

        provider.Dispose();

        string content = File.ReadAllText(tempPath);
        Assert.Contains("Before dispose", content);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void IsEnabled_ReturnsTrueForAllLevelsExceptNone(LogLevel level)
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        Assert.True(logger.IsEnabled(level));
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenLogLevelNone()
    {
        using var provider = new StreamWriterLoggerProvider(tempPath);
        var logger = provider.CreateLogger("TestCategory");

        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(tempPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
