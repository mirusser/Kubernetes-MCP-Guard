using InfraGate.Observability;

namespace InfraGate.Observability.Tests;

public sealed class ObservabilityExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddInfraGateObservability_WriteToConsole_RegistersLoggerFactory(bool consoleToStandardError)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddInfraGateObservability(opt =>
        {
            opt.WriteToConsole = true;
            opt.ConsoleToStandardError = consoleToStandardError;
        });

        using var host = builder.Build();

        var loggerFactory = host.Services.GetService<ILoggerFactory>();
        Assert.NotNull(loggerFactory);
    }

    [Fact]
    public void AddInfraGateObservability_WithFilePath_WritesJsonLogToFile()
    {
        var logPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var builder = Host.CreateApplicationBuilder();

        builder.AddInfraGateObservability(opt =>
        {
            opt.WriteToConsole = false;
            opt.FilePath = logPath;
        });

        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<ObservabilityExtensionsTests>>();
        logger.LogInformation("Test message {Id}", 42);

        Assert.True(File.Exists(logPath), $"Log file not created at '{logPath}'");

        var content = File.ReadAllText(logPath);
        var firstLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).First();
        using var doc = JsonDocument.Parse(firstLine);
        Assert.Equal("Test message {Id}", doc.RootElement.GetProperty("@mt").GetString());

        try { File.Delete(logPath); } catch { }
    }
}
