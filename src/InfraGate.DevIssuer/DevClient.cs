namespace InfraGate.DevIssuer;

internal sealed record DevClient(
    string ClientId,
    string? ClientName,
    IReadOnlyCollection<string> RedirectUris);
