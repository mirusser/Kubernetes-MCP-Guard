using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

// Proves demo bullet #6 from .agents/Plans/minimum-for-demo.md: a challenge created
// by user A cannot be approved by user B (same-subject enforcement in
// GatewayApprovalService.ApproveChallengeAsync). The endpoint test exercises the
// browser approval POST with antiforgery and a simulated approval OAuth subject;
// the service-level test remains as a narrower defense-in-depth probe.
[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class WrongUserApprovalTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApproveChallengeEndpoint_ByDifferentSubject_IsRefused()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        const string otherSubject = "safety-e2e-other";

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var originalChallengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);

        var pending = await fixture.ApprovalStore.GetPendingPlanAsync(planId, CancellationToken.None);
        var pendingHash = await ApprovalStore.ComputeSha256Async(
            fixture.ApprovalStore.GetPendingPath(planId),
            CancellationToken.None);

        // Create a second plan with otherSubject as requester so the approval page
        // renders correctly (passes the requester-envelope consistency check) and we
        // can extract a valid antiforgery token.
        var otherPayload = CreateRestartPayload();
        var otherPlan = KubernetesApprovalAdapter.CreateEnvelope(
            ApprovalStore.NewPlanId(),
            "restart",
            DateTimeOffset.UtcNow,
            new PlanRequester(otherSubject, "test"),
            otherPayload);
        var otherPlanResult = await fixture.ApprovalStore.CreatePlanAsync(otherPlan, fixture.Namespace, CancellationToken.None);
        var otherChallenge = await fixture.ChallengeStore.CreateAsync(
            otherPlanResult.Envelope.Id,
            otherPlanResult.Hash,
            otherSubject,
            requesterAuthenticationType: "test",
            ttl: TimeSpan.FromMinutes(5),
            intentDigest: otherPlanResult.Envelope.IntentDigest,
            reviewDigest: otherPlanResult.Envelope.ReviewDigest,
            cancellationToken: CancellationToken.None);

        using var browser = await fixture.CreateAuthenticatedApprovalBrowserAsync(otherChallenge.Id, otherSubject);
        var tokenPage = await browser.GetAsync($"/approvals/{otherChallenge.Id}");
        tokenPage.EnsureSuccessStatusCode();
        var tokenPageText = await tokenPage.Content.ReadAsStringAsync();
        SafetyE2EFixture.AddResponseCookies(browser, tokenPage);

        var result = await SafetyE2EFixture.PostApprovalAsync(
            browser,
            originalChallengeId,
            SafetyE2EFixture.ParseAntiforgeryToken(tokenPageText));
        var originalChallenge = await fixture.ChallengeStore.GetAsync(originalChallengeId, CancellationToken.None);

        Assert.Contains("Approval Failed", result, StringComparison.Ordinal);
        Assert.Contains("same authenticated subject", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));
        Assert.NotEqual(ApprovalConventions.ChallengeStatuses.Approved, originalChallenge?.Status);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeRejected &&
            evt.GetProperty("payload").TryGetProperty("id", out var challengeIdProp) &&
            challengeIdProp.GetString() == originalChallengeId &&
            evt.GetProperty("payload").TryGetProperty("approverSubject", out var approverSubjectProp) &&
            approverSubjectProp.GetString() == otherSubject);
    }

    [Fact]
    public async Task ApproveChallenge_ByDifferentSubject_IsRefused()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        const string requesterSubject = "safety-e2e-requester";
        const string otherSubject = "safety-e2e-other";

        fixture.SetAuthenticatedSubject(requesterSubject);
        string challengeId;
        string planId;
        try
        {
            var requestText = await fixture.DownstreamClient.CallToolAsync(
                "request_restart_deployment",
                new Dictionary<string, object?>
                {
                    [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                    [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo",
                    ["requesterSubject"] = requesterSubject
                },
                CancellationToken.None);
            planId = SafetyE2EFixture.ParsePlanId(requestText);
            var firstResult = await fixture.GetApprovalService().EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);
            Assert.False(firstResult.IsApproved);
            challengeId = ExtractChallengeId(firstResult.Message);
        }
        finally
        {
            fixture.ClearAuthenticatedSubject();
        }

        fixture.SetAuthenticatedSubject(otherSubject);
        try
        {
            var result = await fixture.GetApprovalService().ApproveChallengeAsync(challengeId, CancellationToken.None);
            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("same authenticated subject", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));
            Assert.NotEqual(ApprovalConventions.ChallengeStatuses.Approved, challenge?.Status);
        }
        finally
        {
            fixture.ClearAuthenticatedSubject();
        }
    }

    [Fact]
    public async Task ApproveChallengeBrowser_BrowserSessionAsDifferentSubject_IsRefused()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        const string otherSubject = "demo2";

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var originalChallengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);

        var otherPayload = CreateRestartPayload();
        var otherPlan = KubernetesApprovalAdapter.CreateEnvelope(
            ApprovalStore.NewPlanId(),
            "restart",
            DateTimeOffset.UtcNow,
            new PlanRequester(otherSubject, "test"),
            otherPayload);
        var otherPlanResult = await fixture.ApprovalStore.CreatePlanAsync(otherPlan, fixture.Namespace, CancellationToken.None);
        var otherChallenge = await fixture.ChallengeStore.CreateAsync(
            otherPlanResult.Envelope.Id,
            otherPlanResult.Hash,
            otherSubject,
            requesterAuthenticationType: "test",
            ttl: TimeSpan.FromMinutes(5),
            intentDigest: otherPlanResult.Envelope.IntentDigest,
            reviewDigest: otherPlanResult.Envelope.ReviewDigest,
            cancellationToken: CancellationToken.None);

        using var browser = await fixture.CreateAuthenticatedApprovalBrowserAsync(otherChallenge.Id, otherSubject);
        var tokenPage = await browser.GetAsync($"/approvals/{otherChallenge.Id}");
        tokenPage.EnsureSuccessStatusCode();
        var tokenPageText = await tokenPage.Content.ReadAsStringAsync();
        SafetyE2EFixture.AddResponseCookies(browser, tokenPage);

        var result = await SafetyE2EFixture.PostApprovalAsync(
            browser,
            originalChallengeId,
            SafetyE2EFixture.ParseAntiforgeryToken(tokenPageText));
        var originalChallenge = await fixture.ChallengeStore.GetAsync(originalChallengeId, CancellationToken.None);

        Assert.Contains("Approval Failed", result, StringComparison.Ordinal);
        Assert.Contains("same authenticated subject", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));
        Assert.NotEqual(ApprovalConventions.ChallengeStatuses.Approved, originalChallenge?.Status);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeRejected &&
            evt.GetProperty("payload").TryGetProperty("id", out var challengeIdProp) &&
            challengeIdProp.GetString() == originalChallengeId);
    }

    [Fact]
    public async Task ApproveChallenge_RealJwtAsDemo2_ApprovalIdentityDerivedFromRealKeycloakToken_IsRefused()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var challengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);

        var demo2Token = await fixture.AcquireTokenAsync("demo2", "demo2");
        fixture.SetAuthenticatedFromJwt(demo2Token);
        try
        {
            var result = await fixture.GetApprovalService().ApproveChallengeAsync(challengeId, CancellationToken.None);
            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("same authenticated subject", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));
            Assert.NotEqual(ApprovalConventions.ChallengeStatuses.Approved, challenge?.Status);
        }
        finally
        {
            fixture.ClearAuthenticatedSubject();
        }
    }

    private static string ExtractChallengeId(string message) =>
        message
            .Split(Environment.NewLine)
            .Single(line => line.StartsWith("Approval URL:", StringComparison.Ordinal))
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Last();

    private static KubernetesPlanPayload CreateRestartPayload()
    {
        var objects = new[] { new K8sObjectRef("apps/v1", "Deployment", "mcp-nginx-demo", "nginx-demo") };

        return new KubernetesPlanPayload(
            "mcp-nginx-demo",
            "Restart nginx-demo deployment.",
            new Dictionary<string, string>
            {
                ["name"] = "nginx-demo",
                ["restartedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
            },
            objects)
        {
            DryRun = new K8sPlanDryRun(
                "succeeded",
                DateTimeOffset.UtcNow,
                objects.Select(obj => new K8sPlanDryRunObject(
                    $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}",
                    "{}")).ToArray(),
                [],
                "Server-side dry-run succeeded."),
            Diffs = objects.Select(obj => new K8sPlanDiff(
                obj,
                ApprovalConventions.DiffChangeTypes.Update,
                $"{obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name} will be restarted.",
                "--- live\n+++ proposed\n spec:\n+  restartAt: ...\n",
                "{}",
                "{}",
                [],
                [],
                ["spec.restartAt"])).ToArray()
        };
    }
}
