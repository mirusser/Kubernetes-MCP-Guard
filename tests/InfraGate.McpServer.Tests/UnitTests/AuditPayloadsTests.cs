using System.Text.Json;
using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class AuditPayloadsTests
{
    // Mirrors ApprovalStore.jsonOptions exactly. The whole point of these tests is
    // to lock the wire shape produced by this exact serialiser configuration.
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IEnumerable<object[]> PlanPayloads()
    {
        var digest = new ApprovalDigest("sha-256", "test", "deadbeef");
        yield return new object[]
        {
            new PlanRequestedPayload("plan-1", "apply", "ns", "deadbeef", digest, digest),
            new[] { "planId", "operation", "namespace", "hash", "intentDigest", "reviewDigest" }
        };
        yield return new object[]
        {
            new ApprovalGrantIssuedPayload("plan-1", "grant-1", "challenge-1", "user", "user", digest, digest, DateTimeOffset.UnixEpoch),
            new[] { "planId", "grantId", "sourceChallengeId", "requesterSubject", "approverSubject", "intentDigest", "reviewDigest", "expiresAtUtc" }
        };
        yield return new object[]
        {
            new PlanApprovedPayload("plan-1", "deadbeef", "gateway_oob", "user@example.com", "challenge-1"),
            new[] { "planId", "hash", "source", "approverSubject", "challengeId" }
        };
        yield return new object[]
        {
            new PlanAppliedPayload("plan-1", "apply", "ns", "deadbeef"),
            new[] { "planId", "operation", "namespace", "hash" }
        };
        yield return new object[]
        {
            new ApplyDeniedPayload("plan-1", "Refused: …"),
            new[] { "planId", "message" }
        };
        yield return new object[]
        {
            new ApplyFailedPayload("plan-1", "apply", "API operation failed"),
            new[] { "planId", "operation", "message" }
        };
        yield return new object[]
        {
            new ApplyDriftDetectedPayload("plan-1", "apply", "ns", "drifted"),
            new[] { "planId", "operation", "namespace", "message" }
        };
        yield return new object[]
        {
            new ApprovalHashMismatchPayload("plan-1", "expected", "actual"),
            new[] { "planId", "approvedHash", "actualHash" }
        };
        yield return new object[]
        {
            new DryRunFailedPayload("apply", "plan-1", "apply", "ns", ["apps/v1 Deployment ns/nginx-demo"], "schema"),
            new[] { "phase", "planId", "operation", "namespace", "objects", "message" }
        };
        yield return new object[]
        {
            new DiffFailedPayload("plan-1", "apply", "ns", ["apps/v1 Deployment ns/nginx-demo"], "diff"),
            new[] { "planId", "operation", "namespace", "objects", "message" }
        };
    }

    public static IEnumerable<object[]> ChallengePayloads()
    {
        var expiresAt = DateTimeOffset.UnixEpoch;
        yield return new object[]
        {
            new ApprovalChallengeCreatedPayload("ch-1", "plan-1", "deadbeef", "user", "test", expiresAt),
            new[] { "id", "planId", "planHash", "requesterSubject", "requesterAuthenticationType", "expiresAtUtc" }
        };
        yield return new object[]
        {
            new ApprovalChallengeApprovedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", expiresAt),
            new[] { "id", "planId", "planHash", "requesterSubject", "approverSubject", "decidedAt" }
        };
        yield return new object[]
        {
            new ApprovalChallengeDeniedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", expiresAt),
            new[] { "id", "planId", "planHash", "requesterSubject", "approverSubject", "decidedAt" }
        };
        yield return new object[]
        {
            new ApprovalChallengeExpiredPayload("ch-1", "plan-1", "deadbeef", "user", expiresAt),
            new[] { "id", "planId", "planHash", "requesterSubject", "expiresAtUtc" }
        };
        yield return new object[]
        {
            new ApprovalChallengeRejectedPayload("ch-1", "plan-1", "deadbeef", "user", "approver", "subject mismatch"),
            new[] { "id", "planId", "planHash", "requesterSubject", "approverSubject", "reason" }
        };
    }

    [Theory]
    [MemberData(nameof(PlanPayloads))]
    public void Serialize_PlanAuditPayload_ProducesExpectedFieldSet(IPlanAuditPayload payload, string[] expectedFields)
    {
        AssertFieldSet(payload, expectedFields);
    }

    [Theory]
    [MemberData(nameof(ChallengePayloads))]
    public void Serialize_ChallengeAuditPayload_ProducesExpectedFieldSet(IChallengeAuditPayload payload, string[] expectedFields)
    {
        AssertFieldSet(payload, expectedFields);
    }

    [Theory]
    [MemberData(nameof(PlanPayloads))]
    public void Serialize_PlanAuditPayload_EmitsPlanIdField(IPlanAuditPayload payload, string[] expectedFields)
    {
        _ = expectedFields;

        var root = SerializeToObject(payload);

        Assert.True(root.ContainsKey("planId"), $"Expected serialised {payload.GetType().Name} to contain 'planId'.");
        Assert.False(root.ContainsKey("id"), $"Expected serialised {payload.GetType().Name} NOT to contain 'id' (use 'planId').");
    }

    [Theory]
    [MemberData(nameof(ChallengePayloads))]
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
