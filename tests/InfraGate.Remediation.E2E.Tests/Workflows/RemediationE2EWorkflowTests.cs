namespace InfraGate.Remediation.E2E.Tests.Workflows;

/// <summary>
/// End-to-end tests covering the full Observer → Planner → Approval → Executor remediation loop.
/// These tests require live services and a developer-provided Kubernetes cluster.
/// They are opt-in via the <see cref="RemediationE2EFixture.EnableEnvVar"/> environment variable.
/// </summary>
[Trait("Category", "RemediationE2E")]
[Collection(RemediationE2ECollection.Name)]
public sealed class RemediationE2EWorkflowTests(RemediationE2EFixture fixture)
{
    private const string TargetNamespace = "mcp-nginx-demo";
    private const string TargetDeployment = "nginx-demo";

    // Mirrors ProposePlanHandler's server-side AllowedOperations allowlist: whatever the LLM
    // decides, the plan the Gateway actually creates can only be one of these three.
    private static readonly string[] AllowedOperationTypes =
    [
        "restart_deployment",
        "scale_deployment",
        "set_deployment_image",
    ];

    private static readonly TimeSpan ApprovalEmailTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DeploymentChangedTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DeploymentPollInterval = TimeSpan.FromSeconds(5);

    [Fact]
    public void RemediationE2E_DisabledByDefault_DoesNotRequireExternalDependencies()
    {
        if (Environment.GetEnvironmentVariable(RemediationE2EFixture.EnableEnvVar) == "1")
        {
            return;
        }

        Assert.False(fixture.IsEnabled);
    }

    [Fact]
    public async Task PlannerIsHealthy_WhenEnabled()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        bool healthy = await fixture.PlannerHealthAsync(CancellationToken.None);
        Assert.True(healthy);
    }

    [Fact]
    public async Task ExecutorIsHealthy_WhenEnabled()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        bool healthy = await fixture.ExecutorHealthAsync(CancellationToken.None);
        Assert.True(healthy);
    }

    /// <summary>
    /// Full remediation loop smoke test: verifies that an anomaly batch delivered to the Planner's
    /// handoff endpoint produces a <c>propose_plan</c> call on the gateway and a downstream
    /// <c>RemediationProposalBatch</c> delivery to the Executor.
    ///
    /// Prerequisites (set when <see cref="RemediationE2EFixture.EnableEnvVar"/> = "1"):
    /// <list type="bullet">
    ///   <item>Observer, Planner, Executor, and Gateway services must be running.</item>
    ///   <item>A Kubernetes cluster must be accessible by the Gateway.</item>
    ///   <item>The <c>mcp-nginx-demo</c> namespace must contain an unhealthy deployment.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task RemediationLoop_AnomalyBatchDelivered_ProposalReachesExecutor()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellationToken = cts.Token;

        // Both services must be reachable before the loop test proceeds.
        bool plannerHealthy = await fixture.PlannerHealthAsync(cancellationToken);
        bool executorHealthy = await fixture.ExecutorHealthAsync(cancellationToken);

        Assert.True(plannerHealthy, "Planner service must be healthy for this E2E test.");
        Assert.True(executorHealthy, "Executor service must be healthy for this E2E test.");

        DeploymentSnapshot before = await fixture.GetDeploymentSnapshotAsync(
            TargetNamespace, TargetDeployment, cancellationToken);

        // Trigger a real observation cycle: the Observer runs the same ObservationCycleRunner
        // workflow as its background timer, including the real A2A handoff to the Planner, which
        // calls the LLM, decides an operation, and calls propose_plan on the Gateway for real.
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        await fixture.ObserveNowAsync(cancellationToken);

        ApprovalEmail email;
        using (MailpitClient mailpitClient = fixture.MailpitClient)
        {
            email = await mailpitClient.FindLatestApprovalEmailAsync(
                observedAt, ApprovalEmailTimeout, cancellationToken);
        }

        Assert.Contains(
            AllowedOperationTypes,
            operationType => email.OperationSummary.StartsWith(operationType, StringComparison.Ordinal));
        Assert.Contains(TargetNamespace, email.OperationSummary, StringComparison.Ordinal);

        using (OperatorApprovalClient approvalClient = fixture.OperatorApprovalClient)
        {
            await approvalClient.ApproveAsync(
                email.ApprovalUrl,
                email.AccessCode,
                fixture.OperatorUsername,
                fixture.OperatorPassword,
                cancellationToken);
        }

        // The Executor applies the approved plan asynchronously; poll the cluster directly for
        // the outcome rather than asserting on a specific field, since the LLM (not the test)
        // chose which of the three allowed operations to run.
        DeploymentSnapshot after = await PollUntilChangedAsync(before, cancellationToken);

        Assert.True(
            after.Generation != before.Generation
                || after.Image != before.Image
                || after.Replicas != before.Replicas,
            $"Deployment '{TargetDeployment}' in namespace '{TargetNamespace}' did not change after approval " +
            $"(before: {before}, after: {after}).");
    }

    private async Task<DeploymentSnapshot> PollUntilChangedAsync(
        DeploymentSnapshot before,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(DeploymentChangedTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (true)
        {
            DeploymentSnapshot current = await fixture.GetDeploymentSnapshotAsync(
                TargetNamespace, TargetDeployment, linkedCts.Token);

            if (current.Generation != before.Generation
                || current.Image != before.Image
                || current.Replicas != before.Replicas)
            {
                return current;
            }

            try
            {
                await Task.Delay(DeploymentPollInterval, TimeProvider.System, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return current;
            }
        }
    }
}
