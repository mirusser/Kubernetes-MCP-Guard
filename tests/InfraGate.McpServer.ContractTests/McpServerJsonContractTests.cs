using System.Text.Json;
using AdapterEvidence = InfraGate.KubernetesAdapter.Evidence;
using McpModels = InfraGate.McpServer.Models;

namespace InfraGate.McpServer.ContractTests;

/// <summary>
/// Verifies that McpServer DTOs serialized to JSON can be deserialized as KubernetesAdapter types.
/// This guards against property name drift between the two assemblies.
/// </summary>
public sealed class McpServerJsonContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void KubernetesApplyEvidence_SerializedByMcpServer_DeserializesAsAdapterType()
    {
        var mcpObjectRef = new McpModels.KubernetesObjectRef("apps/v1", "Deployment", "production", "web-api");
        var mcpFinding = new McpModels.KubernetesPlanPolicyFinding(
            "High", "NO_RESOURCE_LIMITS", "apps/v1/Deployment/production/web-api", "Missing resource limits");
        var mcpDryRunObj = new McpModels.KubernetesPlanDryRunObject(
            "apps/v1/Deployment/production/web-api", """{"status":"ok"}""");
        var mcpDryRun = new McpModels.KubernetesPlanDryRun(
            "Success",
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            [mcpDryRunObj],
            ["resource quota warning"],
            "Dry-run completed");
        var mcpEvidence = new McpModels.KubernetesApplyEvidence(mcpDryRun, [mcpFinding], false, null);

        string json = JsonSerializer.Serialize(mcpEvidence, JsonOptions);
        var adapterEvidence = JsonSerializer.Deserialize<AdapterEvidence.KubernetesApplyEvidence>(json, JsonOptions)!;

        Assert.Equal(mcpDryRun.Status, adapterEvidence.DryRun.Status);
        Assert.Equal(mcpDryRun.CheckedAtUtc, adapterEvidence.DryRun.CheckedAtUtc);
        Assert.Equal(mcpDryRun.Message, adapterEvidence.DryRun.Message);
        Assert.Single(adapterEvidence.DryRun.Warnings);
        Assert.Equal(mcpDryRun.Warnings[0], adapterEvidence.DryRun.Warnings[0]);
        Assert.Single(adapterEvidence.DryRun.Objects);
        Assert.Equal(mcpDryRunObj.Object, adapterEvidence.DryRun.Objects[0].Object);
        Assert.Equal(mcpDryRunObj.ResponseJson, adapterEvidence.DryRun.Objects[0].ResponseJson);
        Assert.Single(adapterEvidence.PolicyFindings);
        Assert.Equal(mcpFinding.Severity, adapterEvidence.PolicyFindings[0].Severity);
        Assert.Equal(mcpFinding.Code, adapterEvidence.PolicyFindings[0].Code);
        Assert.Equal(mcpFinding.ObjectRef, adapterEvidence.PolicyFindings[0].ObjectRef);
        Assert.Equal(mcpFinding.Message, adapterEvidence.PolicyFindings[0].Message);
        Assert.False(adapterEvidence.PolicyBlocked);
        Assert.Null(adapterEvidence.PolicyRefusal);
    }

    [Fact]
    public void KubernetesPlanDiff_SerializedByMcpServer_DeserializesAsAdapterType()
    {
        var mcpObjectRef = new McpModels.KubernetesObjectRef("apps/v1", "Deployment", "production", "web-api");
        var mcpDiff = new McpModels.KubernetesPlanDiff(
            mcpObjectRef,
            "update",
            "Image changed from nginx:1.24 to nginx:1.25",
            "--- a/spec\n+++ b/spec\n@@ -1 +1 @@\n-image: nginx:1.24\n+image: nginx:1.25",
            """{"image":"nginx:1.24"}""",
            """{"image":"nginx:1.25"}""",
            [],
            [],
            ["spec.containers[0].image"]);

        string json = JsonSerializer.Serialize(mcpDiff, JsonOptions);
        var adapterDiff = JsonSerializer.Deserialize<AdapterEvidence.KubernetesPlanDiff>(json, JsonOptions)!;

        Assert.Equal(mcpObjectRef.ApiVersion, adapterDiff.Object.ApiVersion);
        Assert.Equal(mcpObjectRef.Kind, adapterDiff.Object.Kind);
        Assert.Equal(mcpObjectRef.Namespace, adapterDiff.Object.Namespace);
        Assert.Equal(mcpObjectRef.Name, adapterDiff.Object.Name);
        Assert.Equal(mcpDiff.ChangeType, adapterDiff.ChangeType);
        Assert.Equal(mcpDiff.Summary, adapterDiff.Summary);
        Assert.Equal(mcpDiff.UnifiedDiff, adapterDiff.UnifiedDiff);
        Assert.Equal(mcpDiff.LiveObjectJson, adapterDiff.LiveObjectJson);
        Assert.Equal(mcpDiff.ProposedObjectJson, adapterDiff.ProposedObjectJson);
        Assert.Empty(adapterDiff.AddedPaths);
        Assert.Empty(adapterDiff.RemovedPaths);
        Assert.Single(adapterDiff.ChangedPaths);
        Assert.Equal(mcpDiff.ChangedPaths[0], adapterDiff.ChangedPaths[0]);
    }

    [Fact]
    public void KubernetesApplyEvidence_WithEmptyPolicyFindings_RoundtripsCorrectly()
    {
        var mcpDryRunObj = new McpModels.KubernetesPlanDryRunObject("v1/ConfigMap/default/app-config", "{}");
        var mcpDryRun = new McpModels.KubernetesPlanDryRun(
            "Success", DateTimeOffset.UtcNow, [mcpDryRunObj], [], "OK");
        var mcpEvidence = new McpModels.KubernetesApplyEvidence(mcpDryRun, [], false, null);

        string json = JsonSerializer.Serialize(mcpEvidence, JsonOptions);
        var adapterEvidence = JsonSerializer.Deserialize<AdapterEvidence.KubernetesApplyEvidence>(json, JsonOptions)!;

        Assert.Empty(adapterEvidence.PolicyFindings);
        Assert.False(adapterEvidence.PolicyBlocked);
        Assert.Null(adapterEvidence.PolicyRefusal);
    }
}
