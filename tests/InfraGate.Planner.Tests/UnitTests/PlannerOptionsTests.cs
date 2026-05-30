using InfraGate.Planner;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerOptionsTests
{
    [Fact]
    public void Validate_MissingGatewayBaseUrl_Throws()
    {
        var options = new PlannerOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_MissingAnthropicApiKey_Throws()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "anthropic",
            LlmApiKey = "",
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_OpenRouterProvider_MissingApiKey_Throws()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmApiKey = "",
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_OpenRouterProvider_WithApiKey_DoesNotThrow()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmApiKey = "test-key",
        };

        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds - 1)]
    [InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds + 1)]
    public void Validate_AnomalyWallClockCapOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            AnomalyWallClockCapSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinBatchWallClockCapSeconds - 1)]
    [InlineData(PlannerConventions.MaxBatchWallClockCapSeconds + 1)]
    public void Validate_BatchWallClockCapOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            BatchWallClockCapSeconds = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinMaxToolIterations - 1)]
    [InlineData(PlannerConventions.MaxMaxToolIterations + 1)]
    public void Validate_MaxToolIterationsOutOfRange_Throws(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            MaxToolIterations = value,
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Theory]
    [InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds)]
    [InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds)]
    public void Validate_AnomalyWallClockCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            AnomalyWallClockCapSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(PlannerConventions.MinBatchWallClockCapSeconds)]
    [InlineData(PlannerConventions.MaxBatchWallClockCapSeconds)]
    public void Validate_BatchWallClockCapAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            BatchWallClockCapSeconds = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(PlannerConventions.MinMaxToolIterations)]
    [InlineData(PlannerConventions.MaxMaxToolIterations)]
    public void Validate_MaxToolIterationsAtBoundary_DoesNotThrow(int value)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
            MaxToolIterations = value,
        };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Defaults_AreWithinValidRange()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmApiKey = "test-key",
        };

        Assert.InRange(options.AnomalyWallClockCapSeconds,
            PlannerConventions.MinAnomalyWallClockCapSeconds,
            PlannerConventions.MaxAnomalyWallClockCapSeconds);
        Assert.InRange(options.BatchWallClockCapSeconds,
            PlannerConventions.MinBatchWallClockCapSeconds,
            PlannerConventions.MaxBatchWallClockCapSeconds);
        Assert.InRange(options.MaxToolIterations,
            PlannerConventions.MinMaxToolIterations,
            PlannerConventions.MaxMaxToolIterations);
    }

    [Fact]
    public void Validate_NonAiProviderWithApiKey_DoesNotThrow()
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "other-provider",
            LlmApiKey = "test-key",
        };

        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }
}
