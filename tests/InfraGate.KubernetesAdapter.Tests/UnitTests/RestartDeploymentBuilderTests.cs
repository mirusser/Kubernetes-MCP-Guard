using System.Text.Json;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class RestartDeploymentBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators");

    [Fact]
    public async Task BuildAsync_ValidNamespaceAndName_ReturnsSuccessWithNameParameter()
    {
        var builder = new RestartDeploymentBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var payload = ReadPayload(result);
        Assert.Equal("nginx", payload.Parameters[KubernetesAdapterConventions.PlanParameters.Name]);
    }

    [Fact]
    public async Task BuildAsync_MissingName_ReturnsMissingArgumentsFailure()
    {
        var builder = new RestartDeploymentBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo"
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
        var builder = new RestartDeploymentBuilder(evidenceService);

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
        var builder = new RestartDeploymentBuilder(evidenceService);

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceFailed, result.ReasonCode);
    }

    private static Dictionary<string, object?> ValidArguments() =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
            [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx"
        };

    private static KubernetesPlanPayload ReadPayload(PlanBuildResult result) =>
        result.Envelope!.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions)!;
}
