using System.Text.Json;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SnapshotDocumentSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void RoundTrip_FullSnapshot_DeserializesIdentically()
    {
        var original = new SnapshotDocument(
            "test-ns",
            "{\"healthy\":true}",
            "{\"events\":[]}",
            "{\"pods\":[...]}",
            "{\"deployments\":[...]}",
            "{\"services\":[...]}",
            "{\"endpoints\":[...]}",
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Namespace, roundTripped.Namespace);
        Assert.Equal(original.StatusJson, roundTripped.StatusJson);
        Assert.Equal(original.EventsJson, roundTripped.EventsJson);
        Assert.Equal(original.PodsJson, roundTripped.PodsJson);
    }

    [Fact]
    public void RoundTrip_PartialSnapshot_NullFieldsSurvive()
    {
        var original = new SnapshotDocument(
            "test-ns",
            "{}",
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<SnapshotDocument>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped.StatusJson);
        Assert.Null(roundTripped.EventsJson);
        Assert.Null(roundTripped.PodsJson);
    }
}
