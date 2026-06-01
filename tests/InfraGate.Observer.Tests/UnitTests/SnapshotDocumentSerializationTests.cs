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
            new Dictionary<string, string?>
            {
                ["get_k8s_status"] = "{\"healthy\":true}",
                ["get_k8s_events"] = "{\"events\":[]}",
            },
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Namespace, roundTripped.Namespace);
        Assert.Equal("{\"healthy\":true}", roundTripped.ToolResults["get_k8s_status"]);
        Assert.Equal("{\"events\":[]}", roundTripped.ToolResults["get_k8s_events"]);
    }

    [Fact]
    public void RoundTrip_PartialSnapshot_NullValuesSurvive()
    {
        var original = new SnapshotDocument(
            "test-ns",
            new Dictionary<string, string?>
            {
                ["get_k8s_status"] = "{}",
                ["get_k8s_events"] = null,
            },
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal("{}", roundTripped.ToolResults["get_k8s_status"]);
        Assert.Null(roundTripped.ToolResults["get_k8s_events"]);
    }
}
