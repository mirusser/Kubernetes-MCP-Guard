using System.Text.Json;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class SetDeploymentImageBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators");

    [Fact]
    public async Task BuildAsync_ValidNamespaceNameContainerAndPinnedImage_ReturnsSuccess()
    {
        var builder = new SetDeploymentImageBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task BuildAsync_LatestTagImage_ReturnsPolicyBlockedFailure()
    {
        var builder = new SetDeploymentImageBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx",
                [KubernetesAdapterConventions.EvidenceArguments.Container] = "app",
                [KubernetesAdapterConventions.EvidenceArguments.Image] = "nginx:latest"
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_MissingContainer_ReturnsMissingArgumentsFailure()
    {
        var builder = new SetDeploymentImageBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx",
                [KubernetesAdapterConventions.EvidenceArguments.Image] = "nginx:1.27-alpine"
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
        var builder = new SetDeploymentImageBuilder(evidenceService);

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_ValidArguments_PayloadContainsImageParameter()
    {
        var builder = new SetDeploymentImageBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var payload = ReadPayload(result);
        Assert.Equal("nginx:1.27-alpine", payload.Parameters[KubernetesAdapterConventions.PlanParameters.Image]);
    }

    private static Dictionary<string, object?> ValidArguments() =>
        new(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
            [KubernetesAdapterConventions.EvidenceArguments.Name] = "nginx",
            [KubernetesAdapterConventions.EvidenceArguments.Container] = "app",
            [KubernetesAdapterConventions.EvidenceArguments.Image] = "nginx:1.27-alpine"
        };

    private static KubernetesPlanPayload ReadPayload(PlanBuildResult result) =>
        result.Envelope!.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions)!;
}
