using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using Xunit;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class AuditPayloadsTests
{
    // Mirrors ApprovalStore.jsonOptions exactly. The whole point of these tests is
    // to lock the wire shape produced by this exact serialiser configuration.
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static TheoryData<IPlanAuditPayload, string[]> PlanPayloads()
    {
        var data = new TheoryData<IPlanAuditPayload, string[]>();
        var digest = new ApprovalDigest("sha-256", "test", "deadbeef");
        data.Add(
            new PlanRequestedPayload("plan-1", "apply", "ns", "deadbeef", digest, digest),
            new[] { "planId", "operation", "namespace", "hash", "intentDigest", "reviewDigest" });
        data.Add(
            new PreExecutionGrantValidatedPayload(
                "plan-1",
                "grant-1",
                "challenge-1",
                "user",
                "user",
                digest,
                digest,
                new ApprovalPolicy("same-subject"),
                new ExecutionReusePolicy("single-execution"),
                DateTimeOffset.UnixEpoch),
            new[] { "planId", "grantId", "sourceChallengeId", "requesterSubject", "approverSubject", "intentDigest", "reviewDigest", "approvalPolicy", "executionReusePolicy", "expiresAtUtc" });
        data.Add(
            new PreExecutionCheckedPayload(
                "plan-1",
                "apply",
                "kubernetes",
                JsonSerializer.SerializeToElement(new { namespaceName = "ns" }, AuditJsonOptions)),
            new[] { "planId", "operation", "adapterId", "adapterPayload" });
        data.Add(
            new ExecutionStartedPayload(
                "plan-1",
                "apply",
                "kubernetes",
                JsonSerializer.SerializeToElement(new { namespaceName = "ns" }, AuditJsonOptions)),
            new[] { "planId", "operation", "adapterId", "adapterPayload" });
        data.Add(
            new ApprovalGrantIssuedPayload("plan-1", "grant-1", "challenge-1", "user", "user", digest, digest, DateTimeOffset.UnixEpoch),
            new[] { "planId", "grantId", "sourceChallengeId", "requesterSubject", "approverSubject", "intentDigest", "reviewDigest", "expiresAtUtc" });
        data.Add(
            new PlanAppliedPayload("plan-1", "apply", "ns", "deadbeef"),
            new[] { "planId", "operation", "namespace", "hash" });
        data.Add(
            new ApplyDeniedPayload("plan-1", "Refused: …"),
            new[] { "planId", "message" });
        data.Add(
            new ApplyFailedPayload("plan-1", "apply", "API operation failed"),
            new[] { "planId", "operation", "message" });
        data.Add(
            new ApplyDriftDetectedPayload("plan-1", "apply", "ns", "drifted"),
            new[] { "planId", "operation", "namespace", "message" });
        data.Add(
            new DryRunFailedPayload("apply", "plan-1", "apply", "ns", ["apps/v1 Deployment ns/nginx-demo"], "schema"),
            new[] { "phase", "planId", "operation", "namespace", "objects", "message" });
        data.Add(
            new DiffFailedPayload("plan-1", "apply", "ns", ["apps/v1 Deployment ns/nginx-demo"], "diff"),
            new[] { "planId", "operation", "namespace", "objects", "message" });
        return data;
    }

    public static TheoryData<IChallengeAuditPayload, string[]> ChallengePayloads()
    {
        var data = new TheoryData<IChallengeAuditPayload, string[]>();
        var expiresAt = DateTimeOffset.UnixEpoch;
        data.Add(
            new ApprovalChallengeCreatedPayload("ch-1", "plan-1", "deadbeef", "user", "test", expiresAt),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "requesterAuthenticationType", "expiresAtUtc" });
        data.Add(
            new ApprovalChallengeApprovedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", expiresAt),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "approverSubject", "decidedAt" });
        data.Add(
            new ApprovalChallengeDeniedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", expiresAt),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "approverSubject", "decidedAt" });
        data.Add(
            new ApprovalChallengeExpiredPayload("ch-1", "plan-1", "deadbeef", "user", expiresAt),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "expiresAtUtc" });
        data.Add(
            new ApprovalChallengeRejectedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", "subject mismatch"),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "approverSubject", "reason" });
        data.Add(
            new ApprovalChallengeCanceledPayload("ch-1", "plan-1", "deadbeef", "user", "user", expiresAt),
            new[] { "id", "planId", "pendingPlanHash", "requesterSubject", "actorSubject", "decidedAt" });
        return data;
    }

    [Fact]
    public void Serialize_AdapterAuditPayload_EmitsNestedFlexibleJson()
    {
        var adapterPayload = JsonSerializer.SerializeToElement(
            new
            {
                namespaceName = "demo",
                objects = new[] { "apps/v1 Deployment demo/nginx" }
            },
            AuditJsonOptions);
        var root = SerializeToObject(new ExecutionStartedPayload(
            "plan-1",
            "apply",
            "kubernetes",
            adapterPayload));

        var nested = Assert.IsType<JsonObject>(root["adapterPayload"]);
        Assert.Equal("demo", nested["namespaceName"]?.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(PlanPayloads))] // NOSONAR — Interface type arg is not serializable; fine for local test execution
    public void Serialize_PlanAuditPayload_ProducesExpectedFieldSet(IPlanAuditPayload payload, string[] expectedFields)
    {
        AssertFieldSet(payload, expectedFields);
    }

    [Theory]
    [MemberData(nameof(ChallengePayloads))] // NOSONAR — Interface type arg is not serializable; fine for local test execution
    public void Serialize_ChallengeAuditPayload_ProducesExpectedFieldSet(IChallengeAuditPayload payload, string[] expectedFields)
    {
        AssertFieldSet(payload, expectedFields);
    }

    [Theory]
    [MemberData(nameof(PlanPayloads))] // NOSONAR — Interface type arg is not serializable; fine for local test execution
    public void Serialize_PlanAuditPayload_EmitsPlanIdField(IPlanAuditPayload payload, string[] expectedFields)
    {
        _ = expectedFields;

        var root = SerializeToObject(payload);

        Assert.True(root.ContainsKey("planId"), $"Expected serialised {payload.GetType().Name} to contain 'planId'.");
        Assert.False(root.ContainsKey("id"), $"Expected serialised {payload.GetType().Name} NOT to contain 'id' (use 'planId').");
    }

    [Theory]
    [MemberData(nameof(ChallengePayloads))] // NOSONAR — Interface type arg is not serializable; fine for local test execution
    public void Serialize_ChallengeAuditPayload_EmitsIdAndPlanIdFields(IChallengeAuditPayload payload, string[] expectedFields)
    {
        _ = expectedFields;

        var root = SerializeToObject(payload);

        Assert.True(root.ContainsKey("id"), $"Expected serialised {payload.GetType().Name} to contain 'id'.");
        Assert.True(root.ContainsKey("planId"), $"Expected serialised {payload.GetType().Name} to contain 'planId'.");
    }

    private static void AssertFieldSet(object payload, IEnumerable<string> expectedFields)
    {
        var root = SerializeToObject(payload);

        var actual = root.Select(kv => kv.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expected = expectedFields.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    private static JsonObject SerializeToObject(object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), AuditJsonOptions);

        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Serialised payload was not a JSON object.");
    }
}
