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

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var requestText = await client.CallToolAsync(
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });
        var planId = SafetyE2EFixture.ParsePlanId(requestText);
        fixture.SetAuthenticatedSubject(client.Subject);
        try
        {
            var approvals = fixture.GetApprovalService();

            // Create a challenge for the pending plan, then force it into the past.
            var firstResult = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);
            Assert.False(firstResult.IsApproved);
            var challengeId = firstResult.ChallengeId!;

            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None)
                ?? throw new InvalidOperationException("Challenge not found after creation.");
            var expired = challenge with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
            await fixture.ChallengeStore.SaveAsync(expired, CancellationToken.None);

            var approveResult = await approvals.ApproveChallengeAsync(challengeId, CancellationToken.None);
            var afterChallenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(approveResult.Succeeded);
            Assert.Equal(ApprovalConventions.ResultReasonCodes.ChallengeExpired, approveResult.ReasonCode);
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
}
