using InfraGate.RuntimeSafety;

namespace InfraGate.DevIssuer;

internal sealed record DevIssuerOptions(
    string Issuer,
    string Resource,
    string Scope,
    string Subject,
    string? InternalEndpointBase = null,
    string ApprovalClientId = DevIssuerConventions.DefaultApprovalClientId,
    string ApprovalRedirectUri = DevIssuerConventions.DefaultApprovalRedirectUri,
    RuntimeMode RuntimeMode = RuntimeMode.Development)
{
    public const string DefaultUrl = DevIssuerConventions.DefaultUrl;

    public static DevIssuerOptions FromEnvironment()
    {
        RuntimeMode runtimeMode = RuntimeModeResolver.FromEnvironment();

        return new DevIssuerOptions(
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Issuer) ??
            DevIssuerConventions.DefaultUrl,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Resource) ??
            DevIssuerConventions.DefaultResource,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Scope) ??
            DevIssuerConventions.DefaultScope,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.Subject) ??
            DevIssuerConventions.DefaultSubject,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.InternalEndpointBase),
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.ApprovalClientId) ??
            DevIssuerConventions.DefaultApprovalClientId,
            Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.ApprovalRedirectUri) ??
            DevIssuerConventions.DefaultApprovalRedirectUri,
            runtimeMode);
    }

    public void ValidateProductionSafety()
    {
        if (RuntimeMode != RuntimeMode.Production)
        {
            return;
        }

        throw new InvalidOperationException(
            "InfraGate.DevIssuer is development-only and cannot run in Production mode. Configure a real OIDC provider instead.");
    }
}
