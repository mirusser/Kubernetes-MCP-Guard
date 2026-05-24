using System.Net.Http.Json;
using System.Text.Json;

namespace InfraGate.Observer.E2E.Tests;

public sealed class ObserverE2EFixture : IAsyncLifetime
{
    public const string EnableEnvVar = "INFRA_GATE_RUN_OBSERVER_E2E";
    public const string RealLlmEnvVar = "INFRA_GATE_OBSERVER_REAL_LLM";
    public const string ObserverBaseUrlEnvVar = "INFRA_GATE_OBSERVER_E2E_BASE_URL";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsEnabled { get; private set; }

    public bool IsRealLlmEnabled { get; private set; }

    public Uri ObserverBaseUri { get; private set; } = new("http://127.0.0.1:3003");

    public Task InitializeAsync()
    {
        IsEnabled = Environment.GetEnvironmentVariable(EnableEnvVar) == "1";
        IsRealLlmEnabled = Environment.GetEnvironmentVariable(RealLlmEnvVar) == "1";

        string? configuredBaseUrl = Environment.GetEnvironmentVariable(ObserverBaseUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            ObserverBaseUri = new Uri(configuredBaseUrl, UriKind.Absolute);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<bool> HealthAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = ObserverBaseUri
        };

        using var response = await client.GetAsync("/health", cancellationToken)
            .ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<AnomalyReport>> ObserveNowAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = ObserverBaseUri
        };

        using var response = await client.PostAsync("/observe-now", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var reports = await response.Content
            .ReadFromJsonAsync<List<AnomalyReport>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return reports ?? [];
    }
}
