using System.Text.Json;
using InfraGate.Approvals.Plan;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class ApplyManifestBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PlanRequester TestRequester = new("test-subject", "oauth-jwt");
    private static readonly ApprovalPolicy TestApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators");

    [Fact]
    public async Task BuildAsync_ValidNamespaceAndManifest_ReturnsSuccessWithApplyPayload()
    {
        var evidenceService = new FakeEvidenceService();
        var builder = new ApplyManifestBuilder(evidenceService);

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = ValidDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(KubernetesAdapterConventions.PlanOperations.Apply, result.Envelope!.Operation);
        var payload = ReadPayload(result);
        Assert.Equal("demo", payload.Namespace);
        Assert.Equal(ValidDeploymentManifest, payload.Manifest);
        Assert.NotEmpty(payload.Diffs);
    }

    [Fact]
    public async Task BuildAsync_MissingNamespace_ReturnsMissingArgumentsFailure()
    {
        var builder = new ApplyManifestBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = ValidDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.MissingArguments, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_MissingManifest_ReturnsMissingArgumentsFailure()
    {
        var builder = new ApplyManifestBuilder(new FakeEvidenceService());

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
    public async Task BuildAsync_EvidencePolicyBlocked_ReturnsPolicyBlockedFailure()
    {
        var evidenceService = new FakeEvidenceService
        {
            ApplyEvidenceResult = new KubernetesApplyEvidence(FakeDryRun(), [], true, "blocked")
        };
        var builder = new ApplyManifestBuilder(evidenceService);

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = PrivilegedDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_EvidenceReturnsNullDryRun_ReturnsDryRunFailedFailure()
    {
        var evidenceService = new FakeEvidenceService
        {
            ApplyEvidenceResult = new KubernetesApplyEvidence(null!, [], false, null)
        };
        var builder = new ApplyManifestBuilder(evidenceService);

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = ValidDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DryRunFailed, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_NullDiffs_ReturnsDiffEvidenceEmptyFailure()
    {
        var evidenceService = new FakeEvidenceService { DiffsResult = null };
        var builder = new ApplyManifestBuilder(evidenceService);

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = ValidDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.DiffEvidenceEmpty, result.ReasonCode);
    }

    [Fact]
    public async Task BuildAsync_PrivilegedContainerManifest_ReturnsPolicyBlockedFailure()
    {
        var builder = new ApplyManifestBuilder(new FakeEvidenceService());

        var result = await builder.BuildAsync(
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.EvidenceArguments.Namespace] = "demo",
                [KubernetesAdapterConventions.EvidenceArguments.Manifest] = PrivilegedDeploymentManifest
            },
            TestRequester,
            TestApprovalPolicy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(KubernetesAdapterConventions.ResultReasonCodes.PolicyBlocked, result.ReasonCode);
    }

    private static KubernetesPlanPayload ReadPayload(PlanBuildResult result) =>
        result.Envelope!.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions)!;

    private static KubernetesPlanDryRun FakeDryRun() =>
        new(
            "succeeded",
            DateTimeOffset.UtcNow,
            [new KubernetesPlanDryRunObject("v1 Pod demo/nginx", "{}")],
            [],
            "Server-side dry-run succeeded.");

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

    private const string PrivilegedDeploymentManifest = """
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
                securityContext:
                  privileged: true
        """;
}
