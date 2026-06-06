namespace InfraGate.McpGateway.Auth.Dpop;

internal interface IDpopProofReplayStore
{
    Task<bool> TryAddAsync(
        string issuer,
        string presenter,
        string jti,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}
