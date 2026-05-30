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

        // Both services must be reachable before the loop test proceeds.
        bool plannerHealthy = await fixture.PlannerHealthAsync(CancellationToken.None);
        bool executorHealthy = await fixture.ExecutorHealthAsync(CancellationToken.None);

        Assert.True(plannerHealthy, "Planner service must be healthy for this E2E test.");
        Assert.True(executorHealthy, "Executor service must be healthy for this E2E test.");

        // Full loop smoke test body goes here once E2E infrastructure is available.
        // See the roadmap Task 9.7 for the complete acceptance criteria.
    }
}
