using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SnapshotFetcherTests
{
    private static CallToolResult OkResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    [Fact]
    public async Task FetchAsync_PartialFailure_IncrementsSnapshotFetchErrorsCounter()
    {
        using var meter = new Meter(ObserverMetrics.MeterName, ObserverMetrics.MeterVersion);
        using var listener = new MeterListener();
        var recorded = new List<Measurement<long>>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == ObserverMetrics.SnapshotFetchErrorsCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                recorded.Add(new Measurement<long>(measurement, tags));
            });
        listener.Start();

        var mcpClient = new TestAgentMcpToolset
        {
            CallToolHandler = name =>
            {
                if (name == ObserverConventions.ToolNames.GetK8sPods)
                {
                    throw new InvalidOperationException("timeout");
                }
                return Task.FromResult(OkResult("{}"));
            }
        };

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);
        var toolTag = recorded[0].Tags.ToArray().FirstOrDefault(t => t.Key == ObserverMetrics.ToolNameTag);
        Assert.NotEqual(default, toolTag);
        Assert.Equal(ObserverConventions.ToolNames.GetK8sPods, toolTag.Value);
    }

    [Fact]
    public async Task FetchAsync_ReturnsSnapshotDocument()
    {
        var mcpClient = new TestAgentMcpToolset();

        using var meter = new Meter("test-meter-returns-snapshot");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.NotNull(snapshot.StatusJson);
        Assert.NotNull(snapshot.EventsJson);
        Assert.NotNull(snapshot.PodsJson);
        Assert.NotNull(snapshot.DeploymentsJson);
        Assert.NotNull(snapshot.ServicesJson);
        Assert.NotNull(snapshot.EndpointsJson);

        Assert.Equal(6, mcpClient.CallCount);
    }

    [Fact]
    public async Task FetchAsync_PartialFailure_ReturnsPartialSnapshot()
    {
        var mcpClient = new TestAgentMcpToolset
        {
            CallToolHandler = name =>
            {
                if (name == ObserverConventions.ToolNames.GetK8sPods)
                {
                    throw new InvalidOperationException("timeout");
                }
                return Task.FromResult(OkResult("{}"));
            }
        };

        using var meter = new Meter("test-meter-partial-snapshot");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.NotNull(snapshot.StatusJson);
        Assert.Null(snapshot.PodsJson);
        Assert.NotNull(snapshot.EventsJson);

        Assert.Equal(6, mcpClient.CallCount);
    }

    [Fact]
    public async Task FetchAsync_CancelledTokenPropagatesToMcpClient()
    {
        var mcpClient = new TestAgentMcpToolset();

        using var meter = new Meter("test-meter-cancelled-token");
        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, meter);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var snapshot = await fetcher.FetchAsync("test-ns", cts.Token);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.Equal(6, mcpClient.CallCount);
        Assert.True(mcpClient.WasCancelled);
    }
}
