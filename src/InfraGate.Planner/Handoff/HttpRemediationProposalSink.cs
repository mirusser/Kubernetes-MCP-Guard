using System.Diagnostics.Metrics;
using InfraGate.Planner.Diagnostics;

namespace InfraGate.Planner.Handoff;

internal sealed class HttpRemediationProposalSink : IRemediationProposalSink
{
    private readonly HttpClient httpClient;
    private readonly string executorHandoffUrl;
    private readonly ILogger<HttpRemediationProposalSink> logger;
    private readonly Counter<long> httpFailedCounter;
    private readonly Counter<long> httpBackpressureCounter;

    public HttpRemediationProposalSink(
        HttpClient httpClient,
        string executorHandoffUrl,
        ILogger<HttpRemediationProposalSink> logger,
        Meter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrEmpty(executorHandoffUrl);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.executorHandoffUrl = executorHandoffUrl;
        this.logger = logger;
        this.httpFailedCounter = PlannerMetrics.CreateHandoffHttpFailedCounter(meter);
        this.httpBackpressureCounter = PlannerMetrics.CreateHandoffHttpBackpressureCounter(meter);
    }

    public async Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Proposals.Count == 0)
        {
            return;
        }

        using var response = await httpClient
            .PostAsJsonAsync(executorHandoffUrl, batch, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            PlannerLogEvents.LogHandoffHttpBackpressure(logger);
            httpBackpressureCounter.Add(1);
            return;
        }

        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            PlannerLogEvents.LogHandoffHttpFailed(logger, (int)response.StatusCode);
            httpFailedCounter.Add(1);
        }
    }
}
