using System.Text.Json.Nodes;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SnapshotDocumentSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    [Fact]
    public void RoundTrip_FullSnapshot_DeserializesIdentically()
    {
        var original = new SnapshotDocument(
            "test-ns",
            new Dictionary<string, JsonNode?>
            {
                ["get_k8s_status"] = JsonNode.Parse("{\"healthy\":true}"),
                ["get_k8s_events"] = JsonNode.Parse("{\"events\":[]}"),
            },
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Namespace, roundTripped.Namespace);
        var statusNode = roundTripped.ToolResults["get_k8s_status"];
        Assert.NotNull(statusNode);
        Assert.True(statusNode["healthy"]?.GetValue<bool>() == true);
        Assert.NotNull(roundTripped.ToolResults["get_k8s_events"]);
    }

    [Fact]
    public void RoundTrip_PartialSnapshot_NullValuesSurvive()
    {
        var original = new SnapshotDocument(
            "test-ns",
            new Dictionary<string, JsonNode?>
            {
                ["get_k8s_status"] = JsonNode.Parse("{}"),
                ["get_k8s_events"] = null,
            },
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped.ToolResults["get_k8s_status"]);
        Assert.Null(roundTripped.ToolResults["get_k8s_events"]);
    }
}
