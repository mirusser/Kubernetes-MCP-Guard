using System.Text.Json.Nodes;

namespace InfraGate.Observer.Snapshot;

internal sealed record class SnapshotDocument(
    string Namespace,
    IReadOnlyDictionary<string, JsonNode?> ToolResults,
    DateTimeOffset CapturedAt);
