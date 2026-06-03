namespace InfraGate.RunProfiles;

internal sealed record class AgentGuardrailsProfile(
    ModelVisibleContentProfile? ModelVisibleContent);

internal sealed record class ModelVisibleContentProfile(
    string? Enabled,
    string? SemanticClassifierEnabled,
    string? RequestTimeoutMilliseconds,
    string? MaximumInputCharacters,
    string? UnavailableBehavior);
