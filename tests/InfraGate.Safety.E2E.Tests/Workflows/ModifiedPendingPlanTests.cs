using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class ModifiedPendingPlanTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApproveChallenge_AfterPendingFileMutation_IsRefusedAndAudited()
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
                    [McpGatewayConventions.ToolArguments.Name] = "nginx-demo",
                    ["requesterSubject"] = "safety-e2e-user"
                },
                CancellationToken.None);
            var planId = SafetyE2EFixture.ParsePlanId(requestText);
            var approvals = fixture.GetApprovalService();
            var challengeResult = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);
            Assert.False(challengeResult.IsApproved);
            var challengeId = ExtractChallengeId(challengeResult.Message);
            var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);

            await File.AppendAllTextAsync(pendingPath, Environment.NewLine, CancellationToken.None);

            var result = await approvals.ApproveChallengeAsync(challengeId, CancellationToken.None);
            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("pending plan changed", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.ApprovalStore.GetGrantPath(planId)));
            Assert.NotEqual(ApprovalConventions.ChallengeStatuses.Approved, challenge?.Status);

            var auditEvents = await fixture.ReadAuditEventsAsync();
            Assert.Contains(auditEvents, evt =>
                evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.ApprovalChallengeRejected &&
                evt.GetProperty("payload").TryGetProperty("planId", out var planIdProp) &&
                planIdProp.GetString() == planId &&
                evt.GetProperty("payload").TryGetProperty("id", out var challengeIdProp) &&
                challengeIdProp.GetString() == challengeId);
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
