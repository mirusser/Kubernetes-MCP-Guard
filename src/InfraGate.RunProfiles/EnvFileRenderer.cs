using System.Text;

namespace InfraGate.RunProfiles;

internal static class EnvFileRenderer
{
    public static string Render(string configFileName, RunProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(configFileName);
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new StringBuilder();
        builder.AppendLine(
            $"{RunProfileConventions.GeneratedFile.HeaderLinePrefix}{configFileName}{RunProfileConventions.GeneratedFile.ProfileMarker}{profile.Name}");
        builder.AppendLine(
            $"{RunProfileConventions.GeneratedFile.DoNotEditLinePrefix}{profile.Name}");

        AppendRuntime(builder, profile);
        AppendGateway(builder, profile);
        AppendIdentityProvider(builder, profile);
        AppendApprovalAuthority(builder, profile);
        AppendGenericApprovalCore(builder, profile);
        AppendDownstreamAuth(builder, profile);
        AppendKubernetesAdapter(builder, profile);
        AppendHost(builder, profile);
        AppendObserver(builder, profile);

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

    private static void AppendDownstreamAuth(StringBuilder builder, RunProfile profile)
    {
        if (profile.DownstreamAuth is null)
        {
            return;
        }

        DownstreamAuthProfile da = profile.DownstreamAuth;
        bool hasAnyValue =
            !string.IsNullOrEmpty(da.Required) ||
            !string.IsNullOrEmpty(da.Authority) ||
            !string.IsNullOrEmpty(da.MetadataAddress) ||
            !string.IsNullOrEmpty(da.RequireHttpsMetadata) ||
            !string.IsNullOrEmpty(da.Audience) ||
            !string.IsNullOrEmpty(da.Scope) ||
            !string.IsNullOrEmpty(da.GatewayClientId) ||
            !string.IsNullOrEmpty(da.GatewayClientSecret);

        if (!hasAnyValue)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Downstream Auth");
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthRequired, da.Required);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthAuthority, da.Authority);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthMetadataAddress, da.MetadataAddress);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthRequireHttpsMetadata, da.RequireHttpsMetadata);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthAudience, da.Audience);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthScope, da.Scope);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthGatewayClientId, da.GatewayClientId);
        AppendIfSet(builder, RunProfileConventions.Env.DownstreamAuthGatewayClientSecret, da.GatewayClientSecret);
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
            !string.IsNullOrEmpty(host.ConfigHostPath) ||
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
        if (!string.IsNullOrEmpty(host.ConfigHostPath))
        {
            builder.AppendLine($"{RunProfileConventions.Env.ConfigPath}={RunProfileConventions.RuntimeConfig.ContainerPath}");
            builder.AppendLine($"{RunProfileConventions.Env.ConfigHostPath}={host.ConfigHostPath}");
        }

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

    private static void AppendObserver(StringBuilder builder, RunProfile profile)
    {
        if (profile.Observer is null)
        {
            return;
        }

        ObserverProfile observer = profile.Observer;
        bool hasAnyValue =
            !string.IsNullOrEmpty(observer.AspnetcoreUrls) ||
            !string.IsNullOrEmpty(observer.GatewayBaseUrl) ||
            !string.IsNullOrEmpty(observer.TokenEndpoint) ||
            !string.IsNullOrEmpty(observer.ClientId) ||
            !string.IsNullOrEmpty(observer.ClientSecret) ||
            !string.IsNullOrEmpty(observer.Scope) ||
            !string.IsNullOrEmpty(observer.LlmProvider) ||
            !string.IsNullOrEmpty(observer.LlmModel) ||
            !string.IsNullOrEmpty(observer.LlmApiKey) ||
            !string.IsNullOrEmpty(observer.CycleCadenceSeconds) ||
            !string.IsNullOrEmpty(observer.CycleWallClockCapSeconds) ||
            !string.IsNullOrEmpty(observer.MaxToolIterations) ||
            !string.IsNullOrEmpty(observer.FileSinkRoot) ||
            !string.IsNullOrEmpty(observer.ObserverHostPath);

        if (!hasAnyValue)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Observer");
        AppendIfSet(builder, RunProfileConventions.Env.ObserverAspnetcoreUrls, observer.AspnetcoreUrls);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverGatewayBaseUrl, observer.GatewayBaseUrl);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverTokenEndpoint, observer.TokenEndpoint);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverClientId, observer.ClientId);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverClientSecret, observer.ClientSecret);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverScope, observer.Scope);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverLlmProvider, observer.LlmProvider);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverLlmModel, observer.LlmModel);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverLlmApiKey, observer.LlmApiKey);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverCycleIntervalSeconds, observer.CycleCadenceSeconds);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverCycleWallClockCapSeconds, observer.CycleWallClockCapSeconds);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverMaxToolIterations, observer.MaxToolIterations);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverFileSinkRoot, observer.FileSinkRoot);
        AppendIfSet(builder, RunProfileConventions.Env.ObserverHostPath, observer.ObserverHostPath);
    }
}
