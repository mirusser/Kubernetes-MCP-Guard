namespace InfraGate.Remediation.E2E.Tests;

/// <summary>
/// Reads opt-in environment flags once per collection and exposes base URIs
/// for the live Observer, Planner, and Executor services.
/// </summary>
public sealed class RemediationE2EFixture : IAsyncLifetime
{
    /// <summary>Set to "1" to enable end-to-end tests that require running services.</summary>
    public const string EnableEnvVar = "INFRA_GATE_RUN_REMEDIATION_E2E";

    /// <summary>Override the live Planner base URL (default: http://127.0.0.1:3004).</summary>
    public const string PlannerBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_PLANNER_BASE_URL";

    /// <summary>Override the live Executor base URL (default: http://127.0.0.1:3005).</summary>
    public const string ExecutorBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_EXECUTOR_BASE_URL";

    public bool IsEnabled { get; private set; }

    public Uri PlannerBaseUri { get; private set; } = new("http://127.0.0.1:3004");

    public Uri ExecutorBaseUri { get; private set; } = new("http://127.0.0.1:3005");

    public Task InitializeAsync()
    {
        IsEnabled = Environment.GetEnvironmentVariable(EnableEnvVar) == "1";

        string? plannerUrl = Environment.GetEnvironmentVariable(PlannerBaseUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(plannerUrl))
        {
            PlannerBaseUri = new Uri(plannerUrl, UriKind.Absolute);
        }

        string? executorUrl = Environment.GetEnvironmentVariable(ExecutorBaseUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(executorUrl))
        {
            ExecutorBaseUri = new Uri(executorUrl, UriKind.Absolute);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<bool> PlannerHealthAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = PlannerBaseUri };
        using var response = await client.GetAsync("/health", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ExecutorHealthAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = ExecutorBaseUri };
        using var response = await client.GetAsync("/health", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
