using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SnapshotFetcherTests
{
    private static CallToolResult OkResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    private static TestAgentMcpToolset DefaultToolset(Func<string, Task<CallToolResult>>? handler = null)
    {
        var tools = ObserverConventions.ToolNames.NamespaceSnapshotTools
            .Select(name => (AITool)AIFunctionFactory.Create(
                () => Task.FromResult("{}"),
                name: name,
                description: name))
            .ToList();

        return new TestAgentMcpToolset { ToolsToReturn = tools, CallToolHandler = handler };
    }

    [Fact]
    public async Task FetchAsync_ReturnsSnapshotWithAllAdvertisedTools()
    {
        var mcpClient = DefaultToolset();

        using var meter = new Meter("test-returns-snapshot");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.Equal(ObserverConventions.ToolNames.NamespaceSnapshotTools.Count, snapshot.ToolResults.Count);
        foreach (var name in ObserverConventions.ToolNames.NamespaceSnapshotTools)
        {
            Assert.True(snapshot.ToolResults.ContainsKey(name));
        }
        Assert.Equal(ObserverConventions.ToolNames.NamespaceSnapshotTools.Count, mcpClient.CallCount);
    }

    [Fact]
    public async Task FetchAsync_ToolNotAdvertised_AbsentFromResults()
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => Task.FromResult("{}"),
                name: ObserverConventions.ToolNames.GetK8sStatus,
                description: "status"),
        };
        var mcpClient = new TestAgentMcpToolset { ToolsToReturn = tools };

        using var meter = new Meter("test-not-advertised");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Single(snapshot.ToolResults);
        Assert.True(snapshot.ToolResults.ContainsKey(ObserverConventions.ToolNames.GetK8sStatus));
        Assert.False(snapshot.ToolResults.ContainsKey(ObserverConventions.ToolNames.GetK8sEvents));
        Assert.Equal(1, mcpClient.CallCount);
    }

    [Fact]
    public async Task FetchAsync_ToolReturnsError_StoresNullForThatTool()
    {
        var mcpClient = DefaultToolset(name =>
        {
            if (name == ObserverConventions.ToolNames.GetK8sEvents)
                return Task.FromResult(new CallToolResult { IsError = true });
            return Task.FromResult(OkResult("{}"));
        });

        using var meter = new Meter("test-tool-error");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.NotNull(snapshot.ToolResults[ObserverConventions.ToolNames.GetK8sStatus]);
        Assert.Null(snapshot.ToolResults[ObserverConventions.ToolNames.GetK8sEvents]);
    }

    [Fact]
    public async Task FetchAsync_PartialFailure_IncrementsSnapshotFetchErrorsCounter()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        using var listener = new MeterListener();
        var recorded = new List<Measurement<long>>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.SnapshotFetchErrorsCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            recorded.Add(new Measurement<long>(measurement, tags)));
        listener.Start();

        var mcpClient = DefaultToolset(name =>
        {
            if (name == ObserverConventions.ToolNames.GetK8sEvents)
                throw new InvalidOperationException("timeout");
            return Task.FromResult(OkResult("{}"));
        });

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);
        var toolTag = recorded[0].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.ToolNameTag);
        Assert.Equal(ObserverConventions.ToolNames.GetK8sEvents, toolTag.Value);
    }

    [Fact]
    public async Task FetchAsync_ToolReturnsEmptyText_StoresNullForThatTool()
    {
        var mcpClient = DefaultToolset(name =>
        {
            if (name == ObserverConventions.ToolNames.GetK8sEvents)
                return Task.FromResult(OkResult(""));
            return Task.FromResult(OkResult("{}"));
        });

        using var meter = new Meter("test-empty-text");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Null(snapshot.ToolResults[ObserverConventions.ToolNames.GetK8sEvents]);
        Assert.NotNull(snapshot.ToolResults[ObserverConventions.ToolNames.GetK8sStatus]);
    }

    [Fact]
    public async Task FetchAsync_CancelledTokenPropagatesToMcpClient()
    {
        var mcpClient = DefaultToolset();

        using var meter = new Meter("test-cancelled-token");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var snapshot = await fetcher.FetchAsync("test-ns", cts.Token);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.True(mcpClient.WasCancelled);
    }

    [Fact]
    public async Task FetchAsync_NullMeter_DoesNotThrow()
    {
        var mcpClient = DefaultToolset();

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter: null);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.NotEmpty(snapshot.ToolResults);
    }
}
