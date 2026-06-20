using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.McpGateway;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class DryRunFailureTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task RequestApplyManifest_FailingStrictDryRun_DoesNotCreatePendingPlanAndAudits()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        // A negative replica count passes the McpServer's typed YAML parser (the field
        // is a known int32) but is rejected by the Kubernetes API server during
        // dryRun=All admission validation. Unknown fields cannot be used here because
        // the typed parser rejects them locally before dry-run is reached.
        var manifest = $$"""
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: safety-e2e-negative-replicas
                         namespace: {{fixture.Namespace}}
                       spec:
                         replicas: -1
                         selector:
                           matchLabels:
                             app: safety-e2e-negative-replicas
                         template:
                           metadata:
                             labels:
                               app: safety-e2e-negative-replicas
                           spec:
                             containers:
                             - name: app
                               image: nginx:1.27-alpine
                       """;

        var pendingDirectory = Path.Combine(fixture.ApprovalRoot, ApprovalConventions.Storage.PendingDirectory);
        var pendingBefore = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;

        await using var client = await fixture.CreateHttpMcpClientAsync();
        var response = await client.CallToolAsync(
            "request_apply_manifest",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Manifest] = manifest
            });

        var pendingAfter = Directory.Exists(pendingDirectory)
            ? Directory.GetFiles(pendingDirectory).Length
            : 0;
        Assert.Equal(pendingBefore, pendingAfter);
        Assert.DoesNotContain("PlanId:", response, StringComparison.Ordinal);
        Assert.Contains("dry-run", response, StringComparison.OrdinalIgnoreCase);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.DryRunFailed);
    }

    [Fact]
    public async Task ApplyApprovedPlan_PreApplyDryRunFailsAfterApproval_IsRefusedAndAudited()
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
        var pendingPath = fixture.ApprovalStore.GetPendingPath(planId);

        // Rewrite the planned target name to a deployment that does not exist, then
        // refresh the digest-bound envelope fields and approve that mutated plan.
        // Result: grant validation succeeds, and the pre-apply dry-run patches a
        // non-existent target -> 404 -> dry-run fails. Drop freshness checks that
        // depend on the original target so the test isolates the dry-run failure.
        // String-based replace is too brittle because ApprovalStore writes pending plans
        // with WriteIndented = true (key/value separated by ": ", not ":").
        var pendingJson = await File.ReadAllTextAsync(pendingPath, CancellationToken.None);
        var root = JsonNode.Parse(pendingJson)
            ?? throw new InvalidOperationException("Pending plan JSON was empty.");
        root["payload"]!["parameters"]!["name"] = "deployment-that-does-not-exist";

        var rewriteOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        var tamperedEnvelope = JsonSerializer.Deserialize<PlanEnvelope>(root.ToJsonString(), rewriteOptions)
            ?? throw new InvalidOperationException("Failed to deserialize modified pending plan.");
        var tamperedPayload = tamperedEnvelope.Payload.Deserialize<KubernetesPlanPayload>(rewriteOptions)
            ?? throw new InvalidOperationException("Failed to deserialize modified Kubernetes payload.");
        var dryRunOnlyFreshnessPolicy = new FreshnessPolicy(
        [
            new FreshnessCheck(
                KubernetesAdapterConventions.FreshnessCheckTypes.PreExecuteDryRun,
                new Dictionary<string, string>(StringComparer.Ordinal))
        ]);
        var refreshedEnvelope = KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                tamperedEnvelope.Id,
                tamperedEnvelope.Operation,
                tamperedEnvelope.CreatedAtUtc,
                tamperedEnvelope.Requester,
                tamperedPayload,
                tamperedEnvelope.ReviewSurfaceContext,
                dryRunOnlyFreshnessPolicy));
        root = JsonSerializer.SerializeToNode(refreshedEnvelope, rewriteOptions)
            ?? throw new InvalidOperationException("Failed to serialize refreshed pending plan.");

        var rewritten = root.ToJsonString(rewriteOptions);
        await File.WriteAllTextAsync(pendingPath, rewritten, CancellationToken.None);

        var approvalRequired = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });
        var challengeId = SafetyE2EFixture.ParseChallengeId(approvalRequired);
        await fixture.ApproveChallengeInBrowserAsync(challengeId, client.Subject);

        var applyText = await client.CallToolAsync(
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains("dry-run", applyText, StringComparison.OrdinalIgnoreCase);

        var auditEvents = await fixture.ReadAuditEventsAsync();
        Assert.Contains(auditEvents, evt =>
            evt.GetProperty("eventName").GetString() == ApprovalConventions.AuditEvents.DryRunFailed &&
            evt.GetProperty("payload").TryGetProperty("planId", out var planIdProp) &&
            planIdProp.GetString() == planId);
    }
}
