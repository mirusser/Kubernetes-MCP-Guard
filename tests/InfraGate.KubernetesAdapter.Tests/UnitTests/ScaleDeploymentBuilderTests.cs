using System.Text.Json;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class ScaleDeploymentBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators");

    [Fact]
    public async Task BuildAsync_ValidNamespaceNameAndReplicas_ReturnsSuccessWithReplicasParameter()
    {
        var builder = new ScaleDeploymentBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var payload = ReadPayload(result);
        Assert.Equal("3", payload.Parameters[KubernetesAdapterConventions.PlanParameters.Replicas]);
    }

    [Fact]
    public async Task BuildAsync_MissingReplicas_ReturnsMissingArgumentsFailure()
    {
        var builder = new ScaleDeploymentBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx"
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.MissingArguments, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_NullDryRun_ReturnsDryRunFailedFailure()
    {
        var evidenceService = new FakeEvidenceService { DryRunResult = null };
        var builder = new ScaleDeploymentBuilder(evidenceService);

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_NullDiffs_ReturnsDiffEvidenceFailedFailure()
    {
        var evidenceService = new FakeEvidenceService { DiffsResult = null };
        var builder = new ScaleDeploymentBuilder(evidenceService);

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_ValidArguments_PayloadContainsNamespaceNameAndReplicas()
    {
        var builder = new ScaleDeploymentBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var payload = ReadPayload(result);
        Assert.Equal("demo", payload.Namespace);
        Assert.Equal("nginx", payload.Parameters[KubernetesAdapterConventions.PlanParameters.Name]);
        Assert.Equal("3", payload.Parameters[KubernetesAdapterConventions.PlanParameters.Replicas]);
    }

    private static Dictionary<string, object?> ValidArguments() =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
            [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx",
            [KubernetesAdapterConventions.EvidenceArguments.Replicas] = 3
        };

    private static KubernetesPlanPayload ReadPayload(PlanBuildResult result) =>
        result.Envelope!.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions)!;
}
