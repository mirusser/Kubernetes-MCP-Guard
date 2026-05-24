#pragma warning disable ASPDEPR004 // WebHostBuilder deprecated — TestServer for unit tests
#pragma warning disable ASPDEPR008 // TestServer(IWebHostBuilder) deprecated — necessary for endpoint tests
#pragma warning disable CA2008 // Task.Run without TaskScheduler — concurrent HTTP simulation
using System.Text.Json;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserveNowEndpointTests
{
    [Fact]
    public async Task PostObserveNow_WhenCycleCompletes_ReturnsReports()
    {
        var expectedReports = new[]
        {
            new AnomalyReport
            {
                AnomalyId = "abc123",
                CycleId = "cycle-1",
                DetectedAt = DateTimeOffset.UtcNow,
                Kind = AnomalyKind.PodUnhealthy,
                Target = new ResourceRef { ApiVersion = "v1", Kind = "Pod", Namespace = "default", Name = "bad-pod" },
                Severity = Severity.High,
                Status = AnomalyStatus.Active,
                Summary = "Pod is unhealthy",
                Evidence = Array.Empty<EvidenceItem>(),
                Suggested = null,
                Annotations = new Dictionary<string, string>(),
            },
        };

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CycleResult
            {
                CycleId = "cycle-1",
                Reports = expectedReports,
                IsTruncated = false,
                ToolCallsUsed = 2,
                SeverityDisagreements = 0,
                Duration = TimeSpan.FromSeconds(1),
            }));

        using var server = CreateServer(cycleRunner);
        using var client = server.CreateClient();
        using var response = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);

        Assert.Equal(200, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());
        Assert.Equal("abc123", root[0].GetProperty("anomalyId").GetString());
        Assert.Equal((int)AnomalyKind.PodUnhealthy, root[0].GetProperty("kind").GetInt32());
        Assert.Equal((int)Severity.High, root[0].GetProperty("severity").GetInt32());
    }

    [Fact]
    public async Task PostObserveNow_WhenCycleIsTruncated_ReturnsEmptyArray()
    {
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CycleResult
            {
                CycleId = "cycle-1",
                Reports = Array.Empty<AnomalyReport>(),
                IsTruncated = true,
                ToolCallsUsed = 2,
                SeverityDisagreements = 0,
                Duration = TimeSpan.FromSeconds(5),
            }));

        using var server = CreateServer(cycleRunner);
        using var client = server.CreateClient();
        using var response = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);

        Assert.Equal(200, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(0, root.GetArrayLength());
    }

    [Fact]
    public async Task PostObserveNow_WhenCycleThrows_ReturnsServerErrorWithBody()
    {
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CycleResult>(new InvalidOperationException("simulated failure")));

        using var server = CreateServer(cycleRunner);
        using var client = server.CreateClient();
        using var response = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);

        Assert.Equal(500, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal("observation failed", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PostObserveNow_WhenCycleTimesOut_ReturnsGatewayTimeoutWithBody()
    {
        var cycleFinished = new TaskCompletionSource<bool>();
        var neverCompletes = new TaskCompletionSource<CycleResult>();
        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ct = ci.Arg<CancellationToken>();
                cycleFinished.TrySetResult(true);
                ct.Register(() => neverCompletes.TrySetCanceled(ct));
                return neverCompletes.Task;
            });

        using var server = CreateServer(cycleRunner, timeoutSeconds: 1);
        using var client = server.CreateClient();
        using var response = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);

        Assert.True(cycleFinished.Task.IsCompleted);
        Assert.Equal(504, (int)response.StatusCode);

        using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal("observation timed out", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PostObserveNow_WhenCalledConcurrently_SerialisesRequests()
    {
        var entered = new TaskCompletionSource<bool>();
        var canComplete = new TaskCompletionSource<bool>();
        var invokeCount = 0;

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                int count = Interlocked.Increment(ref invokeCount);
                if (count == 1)
                {
                    entered.TrySetResult(true);
                }

                var result = new CycleResult
                {
                    Reports = Array.Empty<AnomalyReport>(),
                    CycleId = $"cycle-{count}",
                    IsTruncated = false,
                    ToolCallsUsed = 0,
                    SeverityDisagreements = 0,
                    Duration = TimeSpan.Zero,
                };

                if (count == 1)
                {
                    return canComplete.Task.ContinueWith(_ => result);
                }

                return Task.FromResult(result);
            });

        var sharedSerialisation = new CycleSerialisation();
        using var server = CreateServer(cycleRunner, sharedSerialisation);
        using var client = server.CreateClient();

        var firstRequest = Task.Run(async () =>
        {
            using var resp = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);
            return (int)resp.StatusCode;
        });

        await entered.Task;

        var secondRequest = Task.Run(async () =>
        {
            using var resp = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);
            return (int)resp.StatusCode;
        });
#pragma warning restore CA2008

        await Task.Delay(500);
        Assert.Equal(1, invokeCount);

        canComplete.TrySetResult(true);

        int firstStatus = await firstRequest;
        int secondStatus = await secondRequest;

        Assert.Equal(200, firstStatus);
        Assert.Equal(200, secondStatus);
        Assert.Equal(2, invokeCount);
    }

    [Fact]
    public async Task PostObserveNow_AfterError_SubsequentRequestAcquiresSemaphore()
    {
        var failed = new TaskCompletionSource<bool>();

        var cycleRunner = Substitute.For<IObservationCycleRunner>();
        cycleRunner.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (!failed.Task.IsCompleted)
                {
                    failed.TrySetResult(true);
                    return Task.FromException<CycleResult>(new InvalidOperationException("simulated failure"));
                }

                return Task.FromResult(new CycleResult
                {
                    Reports = Array.Empty<AnomalyReport>(),
                    CycleId = "cycle-2",
                    IsTruncated = false,
                    ToolCallsUsed = 0,
                    SeverityDisagreements = 0,
                    Duration = TimeSpan.Zero,
                });
            });

        using var server = CreateServer(cycleRunner);
        using var client = server.CreateClient();

        using var errorResp = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);
        Assert.True(failed.Task.IsCompleted);
        Assert.Equal(500, (int)errorResp.StatusCode);

        using var successResp = await client.PostAsync(ObserverConventions.ObserveNowEndpointPath, null);
        Assert.Equal(200, (int)successResp.StatusCode);
    }

    private static TestServer CreateServer(
        IObservationCycleRunner cycleRunner,
        CycleSerialisation? cycleSerialisation = null,
        int timeoutSeconds = ObserverConventions.ObserveNowTimeoutSeconds)
    {
        cycleSerialisation ??= new CycleSerialisation();

        return new TestServer(new WebHostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services =>
            {
                services.AddSingleton(cycleRunner);
                services.AddSingleton(cycleSerialisation);
                services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
                services.AddRouting();
                services.AddLogging();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapObserverObserveNowEndpoint(timeoutSeconds);
                });
            }));
    }
}
