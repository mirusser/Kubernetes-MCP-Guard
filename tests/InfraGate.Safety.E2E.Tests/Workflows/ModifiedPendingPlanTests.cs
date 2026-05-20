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
            var challengeResult = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, CancellationToken.None);
            Assert.False(challengeResult.IsApproved);
            var challengeId = challengeResult.ChallengeId!;
            var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);

            await File.AppendAllTextAsync(pendingPath, Environment.NewLine, CancellationToken.None);

            var result = await approvals.ApproveChallengeAsync(challengeId, CancellationToken.None);
            var challenge = await fixture.ChallengeStore.GetAsync(challengeId, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApprovalConventions.ResultReasonCodes.PendingPlanChanged, result.ReasonCode);
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
}
