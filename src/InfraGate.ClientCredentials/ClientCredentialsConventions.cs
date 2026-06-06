namespace InfraGate.ClientCredentials;

public static class ClientCredentialsConventions
{
    public const string BearerPrefix = "Bearer ";
    public const int DefaultRefreshSkewSeconds = 30;

    public static class DPoP
    {
        public const string AuthorizationScheme = "DPoP";
        public const string ProofHeaderName = "DPoP";
        public const string ProofTyp = "dpop+jwt";
        public const string JwkHeaderName = "jwk";
    }
}
