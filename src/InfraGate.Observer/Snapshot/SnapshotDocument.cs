namespace InfraGate.Observer.Snapshot;

internal sealed record class SnapshotDocument(
    string Namespace,
    string? StatusJson,
    string? EventsJson,
    string? PodsJson,
    string? DeploymentsJson,
    string? ServicesJson,
    string? EndpointsJson,
    DateTimeOffset CapturedAt);
