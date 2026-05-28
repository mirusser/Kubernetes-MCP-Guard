using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SnapshotFetcherTests
{
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

        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sStatus, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sEvents, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sPods, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("timeout"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sDeployments, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sServices, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sEndpoints, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));

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
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.NotNull(snapshot.StatusJson);
        Assert.NotNull(snapshot.EventsJson);
        Assert.NotNull(snapshot.PodsJson);
        Assert.NotNull(snapshot.DeploymentsJson);
        Assert.NotNull(snapshot.ServicesJson);
        Assert.NotNull(snapshot.EndpointsJson);

        await mcpClient.Received(6).GetToolResultAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_PartialFailure_ReturnsPartialSnapshot()
    {
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sStatus, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sEvents, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sPods, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("timeout"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sDeployments, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sServices, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));
        mcpClient.GetToolResultAsync(ObserverConventions.ToolNames.GetK8sEndpoints, Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance);
        var snapshot = await fetcher.FetchAsync("test-ns", CancellationToken.None);

        Assert.Equal("test-ns", snapshot.Namespace);
        Assert.NotNull(snapshot.StatusJson);
        Assert.Null(snapshot.PodsJson);
        Assert.NotNull(snapshot.EventsJson);

        await mcpClient.Received(6).GetToolResultAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_CancelledTokenPropagatesToMcpClient()
    {
        var mcpClient = Substitute.For<IObserverMcpClient>();
        mcpClient.GetToolResultAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("{}"));

        var fetcher = new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var snapshot = await fetcher.FetchAsync("test-ns", cts.Token);

        Assert.Equal("test-ns", snapshot.Namespace);
        await mcpClient.Received(6).GetToolResultAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Is<CancellationToken>(t => t.IsCancellationRequested));
    }
}
