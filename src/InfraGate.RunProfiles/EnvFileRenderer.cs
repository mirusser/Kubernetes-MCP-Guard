using System.Text;

namespace InfraGate.RunProfiles;

internal static class EnvFileRenderer
{
    public static string Render(string configFileName, RunProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(configFileName);
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new StringBuilder();
        builder.AppendLine($"# Generated from {configFileName} profile: {profile.Name}");
        builder.AppendLine($"# Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate {profile.Name}");

        AppendRuntime(builder, profile);
        AppendGateway(builder, profile);
        AppendIdentityProvider(builder, profile);
        AppendApprovalAuthority(builder, profile);
        AppendGenericApprovalCore(builder, profile);
        AppendKubernetesAdapter(builder, profile);
        AppendHost(builder, profile);

        return builder.ToString();
    }

    private static void AppendRuntime(StringBuilder builder, RunProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RuntimeMode))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Runtime");
        builder.AppendLine($"{RunProfileConventions.Env.InfraGateEnvironment}={profile.RuntimeMode}");
    }

    private static void AppendGateway(StringBuilder builder, RunProfile profile)
    {
        if (profile.Gateway is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Gateway");
        AppendIfSet(builder, RunProfileConventions.Env.AspnetcoreUrls, profile.Gateway.AspnetcoreUrls);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAssembly, profile.Gateway.DownstreamAssembly);
        AppendIfSet(builder, RunProfileConventions.Env.GuardAuditRoot, profile.Gateway.GuardAuditRoot);
    }

    private static void AppendIdentityProvider(StringBuilder builder, RunProfile profile)
    {
        if (profile.IdentityProvider is null)
        {
            return;
        }

        IdentityProviderProfile idp = profile.IdentityProvider;
        bool hasAnyValue =
            !string.IsNullOrEmpty(idp.Authority) ||
            !string.IsNullOrEmpty(idp.MetadataAddress) ||
            !string.IsNullOrEmpty(idp.Resource) ||
            !string.IsNullOrEmpty(idp.Scope) ||
            !string.IsNullOrEmpty(idp.RequireHttpsMetadata);

        if (!hasAnyValue)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Identity Provider");
        AppendIfSet(builder, RunProfileConventions.Env.OauthAuthority, idp.Authority);
        AppendIfSet(builder, RunProfileConventions.Env.OauthMetadataAddress, idp.MetadataAddress);
        AppendIfSet(builder, RunProfileConventions.Env.OauthResource, idp.Resource);
        AppendIfSet(builder, RunProfileConventions.Env.OauthScope, idp.Scope);
        AppendIfSet(builder, RunProfileConventions.Env.OauthRequireHttpsMetadata, idp.RequireHttpsMetadata);
    }

    private static void AppendApprovalAuthority(StringBuilder builder, RunProfile profile)
    {
        if (profile.ApprovalAuthority is null)
        {
            return;
        }

        ApprovalAuthorityProfile aa = profile.ApprovalAuthority;
        bool hasAnyValue =
            !string.IsNullOrEmpty(aa.BaseUrl) ||
            !string.IsNullOrEmpty(aa.OauthClientId) ||
            !string.IsNullOrEmpty(aa.OauthCallbackPath) ||
            !string.IsNullOrEmpty(aa.OauthAuthorizationEndpoint) ||
            !string.IsNullOrEmpty(aa.OauthTokenEndpoint);

        if (!hasAnyValue)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Approval Authority");
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalBaseUrl, aa.BaseUrl);
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalOauthClientId, aa.OauthClientId);
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalOauthCallbackPath, aa.OauthCallbackPath);
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalOauthAuthorizationEndpoint, aa.OauthAuthorizationEndpoint);
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalOauthTokenEndpoint, aa.OauthTokenEndpoint);
    }

    private static void AppendGenericApprovalCore(StringBuilder builder, RunProfile profile)
    {
        if (profile.GenericApprovalCore is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Generic Approval Core");
        builder.AppendLine($"{RunProfileConventions.Env.ApprovalRoot}={profile.GenericApprovalCore.ApprovalRoot}");
    }

    private static void AppendKubernetesAdapter(StringBuilder builder, RunProfile profile)
    {
        DomainAdapterProfile? adapter = profile.DomainAdapters.SingleOrDefault(adapter =>
            string.Equals(adapter.Type, RunProfileConventions.DomainAdapterTypes.Kubernetes, StringComparison.Ordinal));
        if (adapter?.Kubernetes is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Kubernetes Adapter");
        builder.AppendLine($"{RunProfileConventions.Env.KubeConfig}={adapter.Kubernetes.KubeConfig}");
        builder.AppendLine(
            $"{RunProfileConventions.Env.AllowedNamespaces}={string.Join(',', adapter.Kubernetes.AllowedNamespaces)}");
    }

    private static void AppendHost(StringBuilder builder, RunProfile profile)
    {
        if (profile.Host is null)
        {
            return;
        }

        HostProfile host = profile.Host;
        bool hasAnyValue =
            !string.IsNullOrEmpty(host.BindAddress) ||
            !string.IsNullOrEmpty(host.BindPort) ||
            !string.IsNullOrEmpty(host.GatewayImage) ||
            !string.IsNullOrEmpty(host.KubeconfigHostPath) ||
            !string.IsNullOrEmpty(host.ApprovalHostPath) ||
            !string.IsNullOrEmpty(host.GuardAuditHostPath) ||
            !string.IsNullOrEmpty(host.DataProtectionHostPath);

        if (!hasAnyValue)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Host");
        AppendIfSet(builder, RunProfileConventions.Env.BindAddress, host.BindAddress);
        AppendIfSet(builder, RunProfileConventions.Env.BindPort, host.BindPort);
        AppendIfSet(builder, RunProfileConventions.Env.GatewayImage, host.GatewayImage);
        AppendIfSet(builder, RunProfileConventions.Env.KubeconfigHostPath, host.KubeconfigHostPath);
        AppendIfSet(builder, RunProfileConventions.Env.ApprovalHostPath, host.ApprovalHostPath);
        AppendIfSet(builder, RunProfileConventions.Env.GuardAuditHostPath, host.GuardAuditHostPath);
        AppendIfSet(builder, RunProfileConventions.Env.DataProtectionHostPath, host.DataProtectionHostPath);
    }

    private static void AppendIfSet(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.AppendLine($"{key}={value}");
        }
    }
}
