namespace InfraGate.Observer.State;

internal readonly record struct DedupKey(AnomalyKind Kind, string ResourceKind, string Namespace, string Name);
