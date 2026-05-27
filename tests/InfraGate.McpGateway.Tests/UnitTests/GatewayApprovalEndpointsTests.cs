using InfraGate.ApprovalUi;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.Audit;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayApprovalEndpointsTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildApprovalPageData_CanDecide_MapsAllFields()
    {
        var challenge = CreateChallenge();
        var plan = CreatePlan();
        var model = new ApprovalPageModel(true, null, challenge, plan);

        var result = GatewayApprovalEndpoints.BuildApprovalPageData(model, "tok-123");

        Assert.True(result.CanDecide);
        Assert.Null(result.Error);
        Assert.NotNull(result.Challenge);
        Assert.Equal(challenge.Id, result.Challenge.ChallengeId);
        Assert.Equal(challenge.PlanId, result.Challenge.PlanId);
        Assert.Equal(challenge.RequesterSubject, result.Challenge.RequesterSubject);
        Assert.Equal(challenge.RequesterAuthenticationType, result.Challenge.RequesterAuthenticationType);
        Assert.Equal(challenge.CreatedAtUtc, result.Challenge.CreatedAtUtc);
        Assert.Equal(challenge.ExpiresAtUtc, result.Challenge.ExpiresAtUtc);
        Assert.Equal(challenge.Status, result.Challenge.Status);
        Assert.Same(plan, result.PlanReview);
    }

    [Fact]
    public void BuildApprovalPageData_CanDecide_BuildsActionUrls()
    {
        var challenge = CreateChallenge();
        var model = new ApprovalPageModel(true, null, challenge, CreatePlan());

        var result = GatewayApprovalEndpoints.BuildApprovalPageData(model, "tok-123");

        Assert.Equal($"/approvals/{challenge.Id}/approve", result.Actions.ApproveUrl);
        Assert.Equal($"/approvals/{challenge.Id}/deny", result.Actions.DenyUrl);
        Assert.Equal($"/approvals/{challenge.Id}/cancel", result.Actions.CancelUrl);
        Assert.Equal(McpGatewayConventions.Approvals.RequestVerificationToken, result.Actions.AntiforgeryFieldName);
        Assert.Equal("tok-123", result.Actions.AntiforgeryToken);
    }

    [Fact]
    public void BuildApprovalPageData_CannotDecide_SetsErrorAndNullChallenge()
    {
        var model = new ApprovalPageModel(false, "Challenge expired.", null, null);

        var result = GatewayApprovalEndpoints.BuildApprovalPageData(model, null);

        Assert.False(result.CanDecide);
        Assert.Equal("Challenge expired.", result.Error);
        Assert.Null(result.Challenge);
        Assert.Null(result.PlanReview);
    }

    [Fact]
    public void BuildApprovalPageData_CannotDecide_UsesEmptyIdsInUrls()
    {
        var model = new ApprovalPageModel(false, "Error", null, null);

        var result = GatewayApprovalEndpoints.BuildApprovalPageData(model, null);

        Assert.Equal("/approvals//approve", result.Actions.ApproveUrl);
        Assert.Equal("/approvals//deny", result.Actions.DenyUrl);
        Assert.Equal("/approvals//cancel", result.Actions.CancelUrl);
    }

    [Fact]
    public void BuildDecisionPageData_Succeeded_MapsCorrectly()
    {
        var result = GatewayApprovalEndpoints.BuildDecisionPageData(
            new ApprovalDecisionResult(true, "Plan approved."));

        Assert.True(result.IsSuccess);
        Assert.Equal("Plan approved.", result.Message);
    }

    [Fact]
    public void BuildDecisionPageData_Failed_MapsCorrectly()
    {
        var result = GatewayApprovalEndpoints.BuildDecisionPageData(
            new ApprovalDecisionResult(false, "Hash mismatch."));

        Assert.False(result.IsSuccess);
        Assert.Equal("Hash mismatch.", result.Message);
    }

    [Fact]
    public void BuildCodePageData_MapsFormFields()
    {
        var result = GatewayApprovalEndpoints.BuildCodePageData(
            "tok-123",
            submittedCode: "ABC12345",
            error: "Approval code is invalid.");

        Assert.Equal(McpGatewayConventions.Approvals.CodeRoute, result.ActionUrl);
        Assert.Equal(McpGatewayConventions.Approvals.CodeFormField, result.CodeFieldName);
        Assert.Equal(McpGatewayConventions.Approvals.RequestVerificationToken, result.AntiforgeryFieldName);
        Assert.Equal("tok-123", result.AntiforgeryToken);
        Assert.Equal("ABC12345", result.SubmittedCode);
        Assert.Equal("Approval code is invalid.", result.Error);
    }

    private static ApprovalChallenge CreateChallenge()
    {
        return new ApprovalChallenge(
            Id: "chall-abc123",
            PlanId: "plan-xyz789",
            PendingPlanHash: "abcdef0123456789",
            RequesterSubject: "user@example.com",
            RequesterAuthenticationType: "OAuth",
            CreatedAtUtc: FixedTime,
            ExpiresAtUtc: FixedTime.AddHours(1),
            Status: "pending",
            ApproverSubject: null,
            DecidedAtUtc: null,
            IntentDigest: CreateDigest("intent"),
            ReviewDigest: CreateDigest("review"));
    }

    private static ApprovalDigest CreateDigest(string value) =>
        new(ApprovalConventions.Digests.Sha256, "test.canonicalization.v1", value);

    private static IPlanReview CreatePlan(bool canBeApproved = true)
    {
        var payload = new KubernetesPlanPayload(
            namespaceName: "mcp-ns",
            description: "Scale deployment demo to 3 replicas.",
            parameters: new Dictionary<string, string> { ["scale"] = "3" },
            objects: [new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")])
        {
            Manifest = "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: demo\n  namespace: mcp-ns\nspec:\n  replicas: 3",
            DryRun = new KubernetesPlanDryRun(
                "succeeded",
                FixedTime,
                [new KubernetesPlanDryRunObject("apps/v1/Deployment/mcp-ns/demo", "{\"kind\":\"Deployment\"}")],
                ["299 - admission warning"],
                "dry-run completed"),
            Diffs =
            [
                new KubernetesPlanDiff(
                    new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
                    "Update",
                    "scaled replicas to 3",
                    "+  replicas: 3\n-  replicas: 1",
                    "{\"spec\":{\"replicas\":1}}",
                    "{\"spec\":{\"replicas\":3}}",
                    ["/spec/replicas"],
                    [],
                    ["/spec/replicas"])
            ],
            PolicyFindings = canBeApproved
                ? []
                : [new KubernetesPlanPolicyFinding("Deny", "POL-001", "deployment/demo", "Not allowed.")],
        };
        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            "plan-xyz789",
            "apply_manifest",
            FixedTime,
            new PlanRequester("user@example.com", "OAuth"),
            payload);

        return KubernetesApprovalAdapter.Materialize(envelope);
    }
}
