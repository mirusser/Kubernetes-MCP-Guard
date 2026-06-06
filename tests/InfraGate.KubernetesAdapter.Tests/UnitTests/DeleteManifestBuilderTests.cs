using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class DeleteManifestBuilderTests
{
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators");

    [Fact]
    public async Task BuildAsync_ValidNamespaceAndManifest_ReturnsSuccessWithDeleteOperation()
    {
        var builder = new DeleteManifestBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            ValidArguments(),
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(KubernetesAdapterConventions.PlanOperations.Delete, result.Envelope!.Operation);
    }

    [Fact]
    public async Task BuildAsync_MissingManifest_ReturnsMissingArgumentsFailure()
    {
        var builder = new DeleteManifestBuilder(new FakeEvidenceService());

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
        var builder = new DeleteManifestBuilder(evidenceService);

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
        var builder = new DeleteManifestBuilder(evidenceService);

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
            [KubernetesAdapterConventions.EvidenceArguments.Manifest] = ValidDeploymentManifest
        };

    private const string ValidDeploymentManifest = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: nginx
          namespace: demo
        spec:
          replicas: 1
          selector:
            matchLabels:
              app: nginx
          template:
            metadata:
              labels:
                app: nginx
            spec:
              containers:
              - name: app
                image: nginx:1.27-alpine
        """;
}
