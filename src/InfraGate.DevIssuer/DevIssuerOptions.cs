namespace InfraGate.DevIssuer;

internal sealed record DevIssuerOptions(
    string Issuer,
    string Resource,
    string Scope,
    string Subject,
    string? InternalEndpointBase = null)
{
    public const string DefaultUrl = DevIssuerConventions.DefaultUrl;

    public static DevIssuerOptions FromEnvironment()
    {
        return new DevIssuerOptions(
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Issuer) ??
            DevIssuerConventions.DefaultUrl,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Resource) ??
            DevIssuerConventions.DefaultResource,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Scope) ??
            DevIssuerConventions.DefaultScope,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Subject) ??
            DevIssuerConventions.DefaultSubject,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.InternalEndpointBase));
    }
}
