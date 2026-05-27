using InfraGate.AuditOutbox;
using InfraGate.Approvals;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

public sealed class CanonicalizationTests
{
    private static readonly AuditOutboxRow BaseRow = new(
        EventName: "test.event",
        OccurredAtUtc: new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
        ActorSubject: "service:test",
        ActorClientId: "client-abc",
        Outcome: "success",
        Reason: "all-good",
        PayloadJsonText: """{"key": "value"}""",
        CorrelationColumns: new Dictionary<string, object?> { ["test_entity_id"] = "entity-1" });

    [Fact]
    public void BuildCanonicalInputObject_EquivalentRows_ProduceSameJson()
    {
        var row1 = BaseRow;
        var row2 = BaseRow with { };

        string json1 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row1));
        string json2 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row2));

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void BuildCanonicalInputObject_DifferentEventName_ProducesDifferentJson()
    {
        var row1 = BaseRow;
        var row2 = BaseRow with { EventName = "different.event" };

        string json1 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row1));
        string json2 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row2));

        Assert.NotEqual(json1, json2);
    }

    [Fact]
    public void BuildCanonicalInputObject_DifferentCorrelationValue_ProducesDifferentJson()
    {
        var row1 = BaseRow;
        var row2 = BaseRow with
        {
            CorrelationColumns = new Dictionary<string, object?> { ["test_entity_id"] = "entity-2" }
        };

        string json1 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row1));
        string json2 = ApprovalCanonicalJson.Serialize(AuditOutboxConventions.BuildCanonicalInputObject(row2));

        Assert.NotEqual(json1, json2);
    }

    [Fact]
    public void BuildCanonicalInputObject_ContainsAllUniversalColumnNames()
    {
        var dict = AuditOutboxConventions.BuildCanonicalInputObject(BaseRow);

        Assert.Contains(AuditOutboxConventions.ColumnNames.EventName, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.OccurredAtUtc, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.ActorSubject, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.ActorClientId, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.Outcome, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.Reason, dict.Keys);
        Assert.Contains(AuditOutboxConventions.ColumnNames.PayloadJsonText, dict.Keys);
    }

    [Fact]
    public void BuildCanonicalInputObject_ContainsCorrelationColumns()
    {
        var dict = AuditOutboxConventions.BuildCanonicalInputObject(BaseRow);

        Assert.Contains("test_entity_id", dict.Keys);
        Assert.Equal("entity-1", dict["test_entity_id"]);
    }

    [Fact]
    public void ApprovalCanonicalJson_ComputeSha256Hex_SameInput_ProducesSameHash()
    {
        string hash1 = ApprovalCanonicalJson.ComputeSha256Hex("deterministic-input");
        string hash2 = ApprovalCanonicalJson.ComputeSha256Hex("deterministic-input");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ApprovalCanonicalJson_ComputeSha256Hex_DifferentInputs_ProduceDifferentHashes()
    {
        string hash1 = ApprovalCanonicalJson.ComputeSha256Hex("input-a");
        string hash2 = ApprovalCanonicalJson.ComputeSha256Hex("input-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void StreamLockKey_SameSchema_ReturnsSameKey()
    {
        int key1 = AuditOutboxConventions.StreamLockKey("approvals");
        int key2 = AuditOutboxConventions.StreamLockKey("approvals");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void StreamLockKey_DifferentSchemas_ReturnDifferentKeys()
    {
        int keyApprovals = AuditOutboxConventions.StreamLockKey("approvals");
        int keyObserver = AuditOutboxConventions.StreamLockKey("observer");
        int keyPlanner = AuditOutboxConventions.StreamLockKey("planner");

        Assert.NotEqual(keyApprovals, keyObserver);
        Assert.NotEqual(keyApprovals, keyPlanner);
        Assert.NotEqual(keyObserver, keyPlanner);
    }

    [Fact]
    public void Streams_Constants_AreNotEmpty()
    {
        Assert.NotEmpty(AuditOutboxConventions.Streams.Approvals);
        Assert.NotEmpty(AuditOutboxConventions.Streams.Observer);
        Assert.NotEmpty(AuditOutboxConventions.Streams.Planner);
    }

    [Fact]
    public void Streams_Constants_MatchExpectedSchemaNames()
    {
        Assert.Equal("approvals", AuditOutboxConventions.Streams.Approvals);
        Assert.Equal("observer", AuditOutboxConventions.Streams.Observer);
        Assert.Equal("planner", AuditOutboxConventions.Streams.Planner);
    }
}
