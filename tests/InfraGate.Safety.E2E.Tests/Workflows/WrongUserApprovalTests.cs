using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

// Proves demo bullet #6 from .agents/Plans/minimum-for-demo.md: a challenge created
// by user A cannot be approved by user B (same-subject enforcement in
// GatewayApprovalService.ApproveChallengeAsync).
//
// This is the one workflow in the project that does NOT use a real Keycloak JWT
// for its primary assertion. It uses SafetyE2EFixture.SetAuthenticatedSubject to
// inject two different ClaimsPrincipal instances directly into IHttpContextAccessor.
// See the comment on SetAuthenticatedSubject in SafetyE2EFixture.cs for the full
// rationale; in short: the realm JSON only ships one user (`demo`) and shares with
// InfraGate.McpGateway.KeycloakTests, and the gateway's approval HTTP endpoints
// require antiforgery cookie + form-token handling that would dwarf the test logic.
//
// The same-subject check itself is the production code path; only the principal's
// origin (test-injected vs. JWT-derived) differs. SmokeTests covers real-JWT entry
// into the gateway separately.
[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class WrongUserApprovalTests(SafetyE2EFixture fixture)
{
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
                McpGatewayConventions.ToolNames.RequestRestartDeployment,
                new Dictionary<string, object?>
                {
                    [McpGatewayConventions.ToolArguments.Namespace] = fixture.Namespace,
                    [McpGatewayConventions.ToolArguments.Name] = "nginx-demo"
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
            Assert.False(File.Exists(fixture.ApprovalStore.GetApprovedPath(planId)));
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
}
