using System.Diagnostics.Metrics;
using InfraGate.Observer.Diagnostics;

namespace InfraGate.Observer.Handoff;

internal sealed class HttpAnomalyHandoffSink : IAnomalyHandoffSink
{
    private readonly HttpClient httpClient;
    private readonly string plannerHandoffUrl;
    private readonly ILogger<HttpAnomalyHandoffSink> logger;
    private readonly Counter<long> httpFailedCounter;
    private readonly Counter<long> httpBackpressureCounter;

    public HttpAnomalyHandoffSink(
        HttpClient httpClient,
        string plannerHandoffUrl,
        ILogger<HttpAnomalyHandoffSink> logger,
        Meter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(plannerHandoffUrl);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.plannerHandoffUrl = plannerHandoffUrl;
        this.logger = logger;
        this.httpFailedCounter = ObserverMetrics.CreateHandoffHttpFailedCounter(meter);
        this.httpBackpressureCounter = ObserverMetrics.CreateHandoffHttpBackpressureCounter(meter);
    }

    public async Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Reports.Count == 0)
        {
            return;
        }

        using var response = await httpClient
            .PostAsJsonAsync(plannerHandoffUrl, batch, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            ObserverLogEvents.LogHandoffHttpBackpressure(logger);
            httpBackpressureCounter.Add(1);
            return;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            ObserverLogEvents.LogHandoffHttpFailed(logger, (int)response.StatusCode);
            httpFailedCounter.Add(1);
        }
    }
}
