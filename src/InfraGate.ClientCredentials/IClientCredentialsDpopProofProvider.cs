namespace InfraGate.ClientCredentials;

public interface IClientCredentialsDpopProofProvider
{
    bool IsDPoPEnabled { get; }

    Task<string> CreateDpopProofAsync(
        string accessToken,
        HttpRequestMessage request,
        CancellationToken cancellationToken);
}
