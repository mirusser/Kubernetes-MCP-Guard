using k8s;

namespace InfraGate.Remediation.E2E.Tests;

/// <summary>
/// Reads opt-in environment flags once per collection and exposes base URIs
/// for the live Observer, Planner, Executor, and Gateway services, plus a
/// Mailpit client and a Kubernetes client for asserting on real, structural
/// outcomes of the agentic remediation loop.
/// </summary>
public sealed class RemediationE2EFixture : IAsyncLifetime
{
    /// <summary>Set to "1" to enable end-to-end tests that require running services.</summary>
    public const string EnableEnvVar = "INFRA_GATE_RUN_REMEDIATION_E2E";

    /// <summary>Override the live Observer base URL (default: http://127.0.0.1:3003).</summary>
    public const string ObserverBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_OBSERVER_BASE_URL";

    /// <summary>Override the live Planner base URL (default: http://127.0.0.1:3004).</summary>
    public const string PlannerBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_PLANNER_BASE_URL";

    /// <summary>Override the live Executor base URL (default: http://127.0.0.1:3005).</summary>
    public const string ExecutorBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_EXECUTOR_BASE_URL";

    /// <summary>Override the live Gateway base URL (default: http://127.0.0.1:3001).</summary>
    public const string GatewayBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_GATEWAY_BASE_URL";

    /// <summary>Override the live Mailpit base URL (default: http://127.0.0.1:8025).</summary>
    public const string MailpitBaseUrlEnvVar = "INFRA_GATE_REMEDIATION_E2E_MAILPIT_BASE_URL";

    /// <summary>Override the Keycloak username used to approve plans (default: demo-operator).</summary>
    public const string OperatorUsernameEnvVar = "INFRA_GATE_REMEDIATION_E2E_OPERATOR_USERNAME";

    /// <summary>Override the Keycloak password used to approve plans (default: operator).</summary>
    public const string OperatorPasswordEnvVar = "INFRA_GATE_REMEDIATION_E2E_OPERATOR_PASSWORD";

    /// <summary>The kubeconfig used to build the Kubernetes client (default: ~/.kube/config).</summary>
    public const string KubeconfigEnvVar = "KUBECONFIG";

    private const string DefaultOperatorUsername = "demo-operator";
    private const string DefaultOperatorPassword = "operator";

    public bool IsEnabled { get; private set; }

    public Uri ObserverBaseUri { get; private set; } = new("http://127.0.0.1:3003");

    public Uri PlannerBaseUri { get; private set; } = new("http://127.0.0.1:3004");

    public Uri ExecutorBaseUri { get; private set; } = new("http://127.0.0.1:3005");

    public Uri GatewayBaseUri { get; private set; } = new("http://127.0.0.1:3001");

    public Uri MailpitBaseUri { get; private set; } = new("http://127.0.0.1:8025");

    public string OperatorUsername { get; private set; } = DefaultOperatorUsername;

    public string OperatorPassword { get; private set; } = DefaultOperatorPassword;

    private IKubernetes? kubernetesClient;

    public Task InitializeAsync()
    {
        IsEnabled = Environment.GetEnvironmentVariable(EnableEnvVar) == "1";

        ObserverBaseUri = ResolveUri(ObserverBaseUrlEnvVar, ObserverBaseUri);
        PlannerBaseUri = ResolveUri(PlannerBaseUrlEnvVar, PlannerBaseUri);
        ExecutorBaseUri = ResolveUri(ExecutorBaseUrlEnvVar, ExecutorBaseUri);
        GatewayBaseUri = ResolveUri(GatewayBaseUrlEnvVar, GatewayBaseUri);
        MailpitBaseUri = ResolveUri(MailpitBaseUrlEnvVar, MailpitBaseUri);

        OperatorUsername = Environment.GetEnvironmentVariable(OperatorUsernameEnvVar) is { Length: > 0 } username
            ? username
            : DefaultOperatorUsername;
        OperatorPassword = Environment.GetEnvironmentVariable(OperatorPasswordEnvVar) is { Length: > 0 } password
            ? password
            : DefaultOperatorPassword;

        if (IsEnabled)
        {
            string? kubeconfigPath = Environment.GetEnvironmentVariable(KubeconfigEnvVar);
            kubernetesClient = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(
                string.IsNullOrWhiteSpace(kubeconfigPath) ? null : kubeconfigPath));
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        kubernetesClient?.Dispose();
        return Task.CompletedTask;
    }

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

    /// <summary>
    /// Triggers a real observation cycle on the live Observer, which runs the same
    /// ObservationCycleRunner workflow as the background timer, including the real
    /// A2A handoff to the Planner.
    /// </summary>
    public async Task ObserveNowAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = ObserverBaseUri };
        using var response = await client.PostAsync("/observe-now", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public MailpitClient MailpitClient => new(MailpitBaseUri);

    public OperatorApprovalClient OperatorApprovalClient => new(GatewayBaseUri);

    public async Task<DeploymentSnapshot> GetDeploymentSnapshotAsync(
        string namespaceName,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        IKubernetes client = kubernetesClient
            ?? throw new InvalidOperationException("Fixture is not enabled; no Kubernetes client available.");

        var deployment = await client.AppsV1
            .ReadNamespacedDeploymentAsync(deploymentName, namespaceName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new DeploymentSnapshot(
            deployment.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image,
            deployment.Spec?.Replicas,
            deployment.Metadata?.Generation);
    }

    private static Uri ResolveUri(string envVar, Uri fallback)
    {
        string? configured = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(configured) ? fallback : new Uri(configured, UriKind.Absolute);
    }
}

/// <summary>
/// The observable facts a remediation could plausibly change: image (set_deployment_image),
/// replica count (scale_deployment), or generation (bumped by any spec mutation, including
/// restart_deployment's pod-template annotation touch).
/// </summary>
public sealed record class DeploymentSnapshot(string? Image, int? Replicas, long? Generation);
