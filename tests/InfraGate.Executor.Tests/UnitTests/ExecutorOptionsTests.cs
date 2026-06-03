namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorOptionsTests
{
    [Fact]
    public void Validate_MissingGatewayBaseUrl_Throws()
    {
        var options = new ExecutorOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new ExecutorOptions { GatewayBaseUrl = "http://localhost:3001/mcp" };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(ExecutorConventions.MinConcurrencyCap - 1)]
    [InlineData(ExecutorConventions.MaxConcurrencyCap + 1)]
    public void Validate_ConcurrencyCapOutOfRange_Throws(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            ConcurrencyCap = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(ExecutorConventions.MinConcurrencyCap)]
    [InlineData(ExecutorConventions.MaxConcurrencyCap)]
    public void Validate_ConcurrencyCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            ConcurrencyCap = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(ExecutorConventions.MinWatchTimeoutSeconds - 1)]
    [InlineData(ExecutorConventions.MaxWatchTimeoutSeconds + 1)]
    public void Validate_WatchTimeoutSecondsOutOfRange_Throws(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WatchTimeoutSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(ExecutorConventions.MinWatchTimeoutSeconds)]
    [InlineData(ExecutorConventions.MaxWatchTimeoutSeconds)]
    public void Validate_WatchTimeoutSecondsAtBoundary_DoesNotThrow(int value)
    {
        var options = new ExecutorOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            WatchTimeoutSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Defaults_AreWithinValidRange()
    {
        var options = new ExecutorOptions { GatewayBaseUrl = "http://localhost:3001/mcp" };

        Assert.InRange(options.ConcurrencyCap,
            ExecutorConventions.MinConcurrencyCap,
            ExecutorConventions.MaxConcurrencyCap);
        Assert.InRange(options.WatchTimeoutSeconds,
            ExecutorConventions.MinWatchTimeoutSeconds,
            ExecutorConventions.MaxWatchTimeoutSeconds);
    }
}
