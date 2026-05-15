using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class ExpiredApprovalTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApproveChallenge_AfterExpiry_IsRefusedAndAudited()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        fixture.SetAuthenticatedSubject("safety-e2e-user");
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
            var planId = SafetyE2EFixture.ParsePlanId(requestText);
            var approvals = fixture.GetApprovalService();

            // Create a challenge for the pending plan, then force it into the past.
            var firstResult = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);
            Assert.False(firstResult.IsApproved);
            var challengeId = ExtractChallengeId(firstResult.Message);

            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None)
                ?? throw new InvalidOperationException("Challenge not found after creation.");
            var expired = challenge with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
            await fixture.ChallengeStore.SaveAsync(expired, CancellationToken.None);

            var approveResult = await approvals.ApproveChallengeAsync(challengeId, CancellationToken.None);
            var afterChallenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(approveResult.Succeeded);
            Assert.Contains("expired", approveResult.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ApprovalConventions.ChallengeStatuses.Expired, afterChallenge?.Status);
            Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));

            var auditEvents = await fixture.ReadAuditEventsAsync();
            Assert.Contains(auditEvents, evt =>
                evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeExpired);
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
