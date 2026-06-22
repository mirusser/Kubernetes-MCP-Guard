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
            DownstreamAuth = MergeDownstreamAuth(profile.DownstreamAuth, defaults.DownstreamAuth),
            OpenRouter = MergeOpenRouter(profile.OpenRouter, defaults.OpenRouter),
            Observer = MergeObserver(profile.Observer, defaults.Observer),
            Planner = MergePlanner(profile.Planner, defaults.Planner),
            Executor = MergeExecutor(profile.Executor, defaults.Executor)
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
            DownstreamAssemblyHash = profile.DownstreamAssemblyHash ?? defaults.DownstreamAssemblyHash,
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
            RequireHttpsMetadata = profile.RequireHttpsMetadata ?? defaults.RequireHttpsMetadata,
            TokenIntrospectionEnabled = profile.TokenIntrospectionEnabled ?? defaults.TokenIntrospectionEnabled,
            TokenIntrospectionEndpoint = profile.TokenIntrospectionEndpoint ?? defaults.TokenIntrospectionEndpoint,
            TokenIntrospectionClientId = profile.TokenIntrospectionClientId ?? defaults.TokenIntrospectionClientId,
            TokenIntrospectionClientSecret = profile.TokenIntrospectionClientSecret ?? defaults.TokenIntrospectionClientSecret,
            TokenIntrospectionCacheSeconds = profile.TokenIntrospectionCacheSeconds ?? defaults.TokenIntrospectionCacheSeconds,
            MaxAcceptedAccessTokenLifetimeSeconds = profile.MaxAcceptedAccessTokenLifetimeSeconds ?? defaults.MaxAcceptedAccessTokenLifetimeSeconds
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

    private static OpenRouterProfile? MergeOpenRouter(OpenRouterProfile? profile, OpenRouterProfile? defaults)
    {
        if (profile is null) return defaults;
        if (defaults is null) return profile;
        return profile with
        {
            ApiKey = profile.ApiKey ?? defaults.ApiKey
        };
    }

    private static ObserverProfile? MergeObserver(
        ObserverProfile? profile,
        ObserverProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            AspnetcoreUrls = profile.AspnetcoreUrls ?? defaults.AspnetcoreUrls,
            GatewayBaseUrl = profile.GatewayBaseUrl ?? defaults.GatewayBaseUrl,
            OAuthAuthority = profile.OAuthAuthority ?? defaults.OAuthAuthority,
            ClientId = profile.ClientId ?? defaults.ClientId,
            ClientSecret = profile.ClientSecret ?? defaults.ClientSecret,
            Scope = profile.Scope ?? defaults.Scope,
            LlmProvider = profile.LlmProvider ?? defaults.LlmProvider,
            LlmModel = profile.LlmModel ?? defaults.LlmModel,
            CycleCadenceSeconds = profile.CycleCadenceSeconds ?? defaults.CycleCadenceSeconds,
            CycleWallClockCapSeconds = profile.CycleWallClockCapSeconds ?? defaults.CycleWallClockCapSeconds,
            MaxToolIterations = profile.MaxToolIterations ?? defaults.MaxToolIterations,
            FileSinkRoot = profile.FileSinkRoot ?? defaults.FileSinkRoot,
            PlannerHandoffUrl = profile.PlannerHandoffUrl ?? defaults.PlannerHandoffUrl,
            ObserverHostPath = profile.ObserverHostPath ?? defaults.ObserverHostPath
        };
    }

    private static PlannerProfile? MergePlanner(PlannerProfile? profile, PlannerProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            AspnetcoreUrls = profile.AspnetcoreUrls ?? defaults.AspnetcoreUrls,
            GatewayBaseUrl = profile.GatewayBaseUrl ?? defaults.GatewayBaseUrl,
            ExecutorHandoffUrl = profile.ExecutorHandoffUrl ?? defaults.ExecutorHandoffUrl,
            ClientId = profile.ClientId ?? defaults.ClientId,
            ClientSecret = profile.ClientSecret ?? defaults.ClientSecret,
            OAuthAuthority = profile.OAuthAuthority ?? defaults.OAuthAuthority,
            OAuthScope = profile.OAuthScope ?? defaults.OAuthScope,
            LlmProvider = profile.LlmProvider ?? defaults.LlmProvider,
            LlmModel = profile.LlmModel ?? defaults.LlmModel,
            AnomalyWallClockCapSeconds = profile.AnomalyWallClockCapSeconds ?? defaults.AnomalyWallClockCapSeconds,
            BatchWallClockCapSeconds = profile.BatchWallClockCapSeconds ?? defaults.BatchWallClockCapSeconds,
            MaxToolIterations = profile.MaxToolIterations ?? defaults.MaxToolIterations,
            FileSinkRoot = profile.FileSinkRoot ?? defaults.FileSinkRoot,
            PlannerHostPath = profile.PlannerHostPath ?? defaults.PlannerHostPath
        };
    }

    private static ExecutorProfile? MergeExecutor(ExecutorProfile? profile, ExecutorProfile? defaults)
    {
        if (profile is null) return null;
        if (defaults is null) return profile;
        return profile with
        {
            AspnetcoreUrls = profile.AspnetcoreUrls ?? defaults.AspnetcoreUrls,
            GatewayBaseUrl = profile.GatewayBaseUrl ?? defaults.GatewayBaseUrl,
            ClientId = profile.ClientId ?? defaults.ClientId,
            ClientSecret = profile.ClientSecret ?? defaults.ClientSecret,
            OAuthAuthority = profile.OAuthAuthority ?? defaults.OAuthAuthority,
            OAuthScope = profile.OAuthScope ?? defaults.OAuthScope,
            ConcurrencyCap = profile.ConcurrencyCap ?? defaults.ConcurrencyCap,
            WatchTimeoutSeconds = profile.WatchTimeoutSeconds ?? defaults.WatchTimeoutSeconds,
            ExecutorHostPath = profile.ExecutorHostPath ?? defaults.ExecutorHostPath
        };
    }
}
