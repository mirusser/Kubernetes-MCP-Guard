namespace InfraGate.Observer.Snapshot;

internal sealed record class SnapshotDocument(
    string Namespace,
    IReadOnlyDictionary<string, string?> ToolResults,
    DateTimeOffset CapturedAt);
