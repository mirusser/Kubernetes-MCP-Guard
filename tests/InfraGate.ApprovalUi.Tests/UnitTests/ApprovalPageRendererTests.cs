using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraGate.ApprovalUi.Tests.UnitTests;

public sealed class ApprovalPageRendererTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenderApprovalPageAsync_CanDecide_ContainsPlanSummaryAndActions()
    {
        await using var renderer = CreateRenderer();
        var plan = CreatePlan();
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"plan-summary\"", html);
        Assert.Contains("data-field=\"plan-id\"", html);
        Assert.Contains("data-field=\"operation\"", html);
        Assert.Contains("data-field=\"intent-digest\"", html);
        Assert.Contains("data-section=\"targets\"", html);
        Assert.Contains("data-section=\"approval-actions\"", html);
        Assert.Contains("data-action=\"approve\"", html);
        Assert.Contains("data-action=\"deny\"", html);
        Assert.Contains("data-action=\"cancel\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("tok-123", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_CanDecideWithKubernetesPlan_ContainsEvidenceSections()
    {
        await using var renderer = CreateRenderer();
        var plan = CreateKubernetesPlan();
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"targets\"", html);
        Assert.Contains("data-section=\"submitted-manifest\"", html);
        Assert.Contains("data-section=\"policy-findings\"", html);
        Assert.Contains("data-section=\"dry-run-results\"", html);
        Assert.Contains("data-section=\"diff\"", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_CannotDecide_ShowsError()
    {
        await using var renderer = CreateRenderer();
        var data = new ApprovalPageData(
            false,
            "Challenge expired.",
            null,
            null,
            new ApprovalActionUrls("", "", "", "", null));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"approval-unavailable\"", html);
        Assert.Contains("data-field=\"error-message\"", html);
        Assert.Contains("Challenge expired.", html);
    }

    [Fact]
    public async Task RenderDecisionPageAsync_Succeeded_ShowsSuccess()
    {
        await using var renderer = CreateRenderer();

        var html = await renderer.RenderDecisionPageAsync(new DecisionPageData(true, "Plan approved."));

        Assert.Contains("data-section=\"decision-result\"", html);
        Assert.Contains("data-field=\"decision-message\"", html);
        Assert.Contains("Approval Recorded", html);
    }

    [Fact]
    public async Task RenderDecisionPageAsync_Failed_ShowsError()
    {
        await using var renderer = CreateRenderer();

        var html = await renderer.RenderDecisionPageAsync(new DecisionPageData(false, "Hash mismatch."));

        Assert.Contains("data-section=\"decision-result\"", html);
        Assert.Contains("data-field=\"decision-message\"", html);
        Assert.Contains("Approval Failed", html);
    }

    [Fact]
    public async Task RenderCodePageAsync_WithError_ShowsFormAndError()
    {
        await using var renderer = CreateRenderer();

        var html = await renderer.RenderCodePageAsync(new ApprovalCodePageData(
            "/approvals/code",
            "code",
            "__RequestVerificationToken",
            "tok-123",
            "BADCODE",
            "Approval code is invalid."));

        Assert.Contains("data-section=\"approval-code\"", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("action=\"/approvals/code\"", html);
        Assert.Contains("name=\"code\"", html);
        Assert.Contains("value=\"BADCODE\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("tok-123", html);
        Assert.Contains("data-field=\"code-error\"", html);
        Assert.Contains("Approval code is invalid.", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_CanDecideWithNullChallenge_DoesNotCrash()
    {
        await using var renderer = CreateRenderer();
        var data = new ApprovalPageData(
            true,
            null,
            null,
            null,
            new ApprovalActionUrls("", "", "", "", null));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.NotNull(html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_NullError_ShowsFallbackText()
    {
        await using var renderer = CreateRenderer();
        var data = new ApprovalPageData(
            false,
            null,
            null,
            null,
            new ApprovalActionUrls("", "", "", "", null));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"approval-unavailable\"", html);
        Assert.Contains("data-field=\"error-message\"", html);
        Assert.Contains("Approval challenge is unavailable.", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_KubernetesPlanWithDeny_DisablesApproveButton()
    {
        await using var renderer = CreateRenderer();
        var plan = CreateKubernetesPlan(canBeApproved: false);
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-action=\"approve\" disabled", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_KubernetesPlanWithEmptyDiffs_ShowsNoDiff()
    {
        await using var renderer = CreateRenderer();
        var plan = CreateKubernetesPlan(includeDiffs: false);
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("No diff was recorded for this plan.", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_NonKubernetesPlanWithTargets_ShowsTargets()
    {
        await using var renderer = CreateRenderer();
        var plan = new UnknownPlanWithTargets();
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"targets\"", html);
        Assert.Contains(plan.Description, html);
        Assert.Contains("apps/v1", html);
    }

    [Fact]
    public async Task RenderApprovalPageAsync_UnknownPlanType_ShowsUnsupportedEvidence()
    {
        await using var renderer = CreateRenderer();
        var plan = new UnknownPlan();
        var data = new ApprovalPageData(
            true,
            null,
            new ApprovalChallengeInfo(
                "ch-1", "plan-1", "user@example.com", "OAuth",
                FixedTime, FixedTime.AddHours(1), "pending"),
            plan,
            new ApprovalActionUrls(
                "/approvals/ch-1/approve",
                "/approvals/ch-1/deny",
                "/approvals/ch-1/cancel",
                "__RequestVerificationToken",
                "tok-123"));

        var html = await renderer.RenderApprovalPageAsync(data);

        Assert.Contains("data-section=\"targets\"", html);
        Assert.Contains(plan.Description, html);
    }

    private static ApprovalPageRenderer CreateRenderer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        return new ApprovalPageRenderer(provider, loggerFactory);
    }

    private static IPlanReview CreatePlan()
    {
        var payload = new KubernetesPlanPayload(
            "mcp-ns",
            "Scale deployment.",
            new Dictionary<string, string> { ["replicas"] = "3" },
            [new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")]);

        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            "plan-1",
            KubernetesAdapterConventions.PlanOperations.Scale,
            FixedTime,
            new PlanRequester("user@example.com", "OAuth"),
            payload);

        return KubernetesApprovalAdapter.Materialize(envelope);
    }

    private static IPlanReview CreateKubernetesPlan(bool canBeApproved = true, bool includeDiffs = true)
    {
        var payload = new KubernetesPlanPayload(
            "mcp-ns",
            "Apply deployment.",
            new Dictionary<string, string> { ["objectCount"] = "1" },
            [new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo")])
        {
            Manifest = "apiVersion: apps/v1\nkind: Deployment",
            DryRun = new KubernetesPlanDryRun(
                "succeeded",
                FixedTime,
                [new KubernetesPlanDryRunObject("apps/v1/Deployment/mcp-ns/demo", "{}")],
                [],
                "dry-run ok"),
            Diffs = includeDiffs
                ?
                [
                    new KubernetesPlanDiff(
                        new KubernetesObjectRef("apps/v1", "Deployment", "mcp-ns", "demo"),
                        "Update",
                        "scaled to 3",
                        "+  replicas: 3",
                        "{}",
                        "{}",
                        ["/spec/replicas"],
                        [],
                        ["/spec/replicas"])
                ]
                : [],
            PolicyFindings = canBeApproved
                ? []
                : [new KubernetesPlanPolicyFinding(KubernetesAdapterConventions.PolicySeverities.Deny, "POL-001", "deployment/demo", "Not allowed.")]
        };

        var envelope = KubernetesApprovalAdapter.CreateEnvelope(
            "plan-1",
            KubernetesAdapterConventions.PlanOperations.Scale,
            FixedTime,
            new PlanRequester("user@example.com", "OAuth"),
            payload);

        return KubernetesApprovalAdapter.Materialize(envelope);
    }

    private sealed record class UnknownPlan : IPlanReview
    {
        public PlanEnvelope Envelope => new()
        {
            Id = "unknown-1",
            Operation = "custom",
            IntentDigest = new ApprovalDigest("sha-256", "v1", "abc"),
            ReviewDigest = new ApprovalDigest("sha-256", "v1", "def")
        };
        public string Description => "Custom operation.";
        public IReadOnlyList<PlanReviewTarget> Targets => [];
        public bool HasReviewEvidence => true;
        public bool CanBeApproved => true;
    }

    private sealed record class UnknownPlanWithTargets : IPlanReview
    {
        public PlanEnvelope Envelope => new()
        {
            Id = "unknown-2",
            Operation = "custom",
            IntentDigest = new ApprovalDigest("sha-256", "v1", "abc"),
            ReviewDigest = new ApprovalDigest("sha-256", "v1", "def")
        };
        public string Description => "Custom operation with targets.";
        public IReadOnlyList<PlanReviewTarget> Targets =>
        [
            new PlanReviewTarget("Deployment", "demo", "mcp-ns", new Dictionary<string, string>
            {
                [KubernetesAdapterConventions.PlanAttributeKeys.ApiVersion] = "apps/v1"
            })
        ];
        public bool HasReviewEvidence => true;
        public bool CanBeApproved => true;
    }
}
