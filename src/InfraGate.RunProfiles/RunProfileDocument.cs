namespace InfraGate.RunProfiles;

internal sealed class RunProfileDocument(IReadOnlyList<RunProfile> profiles)
{
    public IReadOnlyList<RunProfile> Profiles { get; } = profiles;
    public ProfileDefaults? Defaults { get; init; }

    public RunProfile FindProfile(string name)
    {
        return Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Unknown Run Profile: {name}");
    }

    public RunProfile FindProfileWithDefaults(string name, ProfileDefaults? defaults)
    {
        RunProfile profile = FindProfile(name);
        if (defaults is null)
        {
            return profile;
        }

        return profile with
        {
            Gateway = MergeGateway(profile.Gateway, defaults.Gateway),
            IdentityProvider = MergeIdentityProvider(profile.IdentityProvider, defaults.IdentityProvider),
            ApprovalAuthority = MergeApprovalAuthority(profile.ApprovalAuthority, defaults.ApprovalAuthority),
            GenericApprovalCore = MergeGenericApprovalCore(profile.GenericApprovalCore, defaults.GenericApprovalCore),
            Host = MergeHost(profile.Host, defaults.Host),
            DownstreamAuth = MergeDownstreamAuth(profile.DownstreamAuth, defaults.DownstreamAuth)
        };
    }

    private static GatewayProfile? MergeGateway(GatewayProfile? profile, GatewayProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            AspnetcoreUrls = profile.AspnetcoreUrls ?? defaults.AspnetcoreUrls,
            DownstreamAssembly = profile.DownstreamAssembly ?? defaults.DownstreamAssembly,
            GuardAuditRoot = profile.GuardAuditRoot ?? defaults.GuardAuditRoot
        };
    }

    private static IdentityProviderProfile? MergeIdentityProvider(
        IdentityProviderProfile? profile,
        IdentityProviderProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            RealmImport = profile.RealmImport ?? defaults.RealmImport,
            Authority = profile.Authority ?? defaults.Authority,
            MetadataAddress = profile.MetadataAddress ?? defaults.MetadataAddress,
            Resource = profile.Resource ?? defaults.Resource,
            Scope = profile.Scope ?? defaults.Scope,
            RequireHttpsMetadata = profile.RequireHttpsMetadata ?? defaults.RequireHttpsMetadata
        };
    }

    private static ApprovalAuthorityProfile? MergeApprovalAuthority(
        ApprovalAuthorityProfile? profile,
        ApprovalAuthorityProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            BaseUrl = profile.BaseUrl ?? defaults.BaseUrl,
            OauthClientId = profile.OauthClientId ?? defaults.OauthClientId,
            OauthCallbackPath = profile.OauthCallbackPath ?? defaults.OauthCallbackPath,
            OauthAuthorizationEndpoint = profile.OauthAuthorizationEndpoint ?? defaults.OauthAuthorizationEndpoint,
            OauthTokenEndpoint = profile.OauthTokenEndpoint ?? defaults.OauthTokenEndpoint
        };
    }

    private static HostProfile? MergeHost(HostProfile? profile, HostProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            BindAddress = profile.BindAddress ?? defaults.BindAddress,
            BindPort = profile.BindPort ?? defaults.BindPort,
            GatewayImage = profile.GatewayImage ?? defaults.GatewayImage,
            ConfigHostPath = profile.ConfigHostPath ?? defaults.ConfigHostPath,
            KubeconfigHostPath = profile.KubeconfigHostPath ?? defaults.KubeconfigHostPath,
            ApprovalHostPath = profile.ApprovalHostPath ?? defaults.ApprovalHostPath,
            GuardAuditHostPath = profile.GuardAuditHostPath ?? defaults.GuardAuditHostPath,
            DataProtectionHostPath = profile.DataProtectionHostPath ?? defaults.DataProtectionHostPath
        };
    }

    private static DownstreamAuthProfile? MergeDownstreamAuth(
        DownstreamAuthProfile? profile,
        DownstreamAuthProfile? defaults)
    {
        if (profile is null) return defaults;
        if (defaults is null) return profile;
        return profile with
        {
            Required = profile.Required ?? defaults.Required,
            Authority = profile.Authority ?? defaults.Authority,
            MetadataAddress = profile.MetadataAddress ?? defaults.MetadataAddress,
            RequireHttpsMetadata = profile.RequireHttpsMetadata ?? defaults.RequireHttpsMetadata,
            Audience = profile.Audience ?? defaults.Audience,
            Scope = profile.Scope ?? defaults.Scope,
            GatewayClientId = profile.GatewayClientId ?? defaults.GatewayClientId,
            GatewayClientSecret = profile.GatewayClientSecret ?? defaults.GatewayClientSecret
        };
    }

    private static GenericApprovalCoreProfile? MergeGenericApprovalCore(
        GenericApprovalCoreProfile? profile,
        GenericApprovalCoreProfile? defaults)
    {
        if (profile is null) return defaults;
        if (defaults is null) return profile;
        return profile with
        {
            PostgresConnectionString = profile.PostgresConnectionString ?? defaults.PostgresConnectionString,
            RunMigrationsOnStartup = profile.RunMigrationsOnStartup ?? defaults.RunMigrationsOnStartup
        };
    }
}
