namespace InfraGate.ClientCredentials;

public interface IClientCredentialsTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);

    Task<string> RefreshTokenAsync(CancellationToken cancellationToken);
}
