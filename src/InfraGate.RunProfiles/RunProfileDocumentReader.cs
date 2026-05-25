using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace InfraGate.RunProfiles;

internal static class RunProfileDocumentReader
{
    private static readonly IReadOnlySet<string> KnownRootKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Defaults,
                RunProfileConventions.YamlKeys.Profiles,
                RunProfileConventions.YamlKeys.Version
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownDefaultsKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.ApprovalAuthority,
                RunProfileConventions.YamlKeys.DownstreamAuth,
                RunProfileConventions.YamlKeys.Executor,
                RunProfileConventions.YamlKeys.Gateway,
                RunProfileConventions.YamlKeys.GenericApprovalCore,
                RunProfileConventions.YamlKeys.Host,
                RunProfileConventions.YamlKeys.IdentityProvider,
                RunProfileConventions.YamlKeys.Observer,
                RunProfileConventions.YamlKeys.Planner
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownProfileKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.ApprovalAuthority,
                RunProfileConventions.YamlKeys.DomainAdapters,
                RunProfileConventions.YamlKeys.DownstreamAuth,
                RunProfileConventions.YamlKeys.Executor,
                RunProfileConventions.YamlKeys.Gateway,
                RunProfileConventions.YamlKeys.GenericApprovalCore,
                RunProfileConventions.YamlKeys.Host,
                RunProfileConventions.YamlKeys.IdentityProvider,
                RunProfileConventions.YamlKeys.Kind,
                RunProfileConventions.YamlKeys.Observer,
                RunProfileConventions.YamlKeys.Planner,
                RunProfileConventions.YamlKeys.RuntimeMode
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownDownstreamAuthKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Audience,
                RunProfileConventions.YamlKeys.Authority,
                RunProfileConventions.YamlKeys.GatewayClientId,
                RunProfileConventions.YamlKeys.GatewayClientSecret,
                RunProfileConventions.YamlKeys.MetadataAddress,
                RunProfileConventions.YamlKeys.Required,
                RunProfileConventions.YamlKeys.RequireHttpsMetadata,
                RunProfileConventions.YamlKeys.Scope
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownGatewayKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.AspnetcoreUrls,
                RunProfileConventions.YamlKeys.DownstreamAssembly,
                RunProfileConventions.YamlKeys.GuardAuditRoot
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownIdentityProviderKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Authority,
                RunProfileConventions.YamlKeys.MetadataAddress,
                RunProfileConventions.YamlKeys.RealmImport,
                RunProfileConventions.YamlKeys.RequireHttpsMetadata,
                RunProfileConventions.YamlKeys.Resource,
                RunProfileConventions.YamlKeys.Scope
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownApprovalAuthorityKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.BaseUrl,
                RunProfileConventions.YamlKeys.OauthAuthorizationEndpoint,
                RunProfileConventions.YamlKeys.OauthCallbackPath,
                RunProfileConventions.YamlKeys.OauthClientId,
                RunProfileConventions.YamlKeys.OauthTokenEndpoint
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownGenericApprovalCoreKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.ApprovalRoot,
                RunProfileConventions.YamlKeys.PostgresConnectionString,
                RunProfileConventions.YamlKeys.RunMigrationsOnStartup
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownHostKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.ApprovalHostPath,
                RunProfileConventions.YamlKeys.BindAddress,
                RunProfileConventions.YamlKeys.BindPort,
                RunProfileConventions.YamlKeys.ConfigHostPath,
                RunProfileConventions.YamlKeys.DataProtectionHostPath,
                RunProfileConventions.YamlKeys.GatewayImage,
                RunProfileConventions.YamlKeys.GuardAuditHostPath,
                RunProfileConventions.YamlKeys.KubeconfigHostPath
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownDomainAdapterKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Kubernetes,
                RunProfileConventions.YamlKeys.Name,
                RunProfileConventions.YamlKeys.Type
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownKubernetesKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.AllowedNamespaces,
                RunProfileConventions.YamlKeys.KubeConfig
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownObserverKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.AspnetcoreUrls,
                RunProfileConventions.YamlKeys.ClientId,
                RunProfileConventions.YamlKeys.ClientSecret,
                RunProfileConventions.YamlKeys.CycleCadenceSeconds,
                RunProfileConventions.YamlKeys.CycleWallClockCapSeconds,
                RunProfileConventions.YamlKeys.FileSinkRoot,
                RunProfileConventions.YamlKeys.GatewayBaseUrl,
                RunProfileConventions.YamlKeys.LlmApiKey,
                RunProfileConventions.YamlKeys.LlmModel,
                RunProfileConventions.YamlKeys.LlmProvider,
                RunProfileConventions.YamlKeys.MaxToolIterations,
                RunProfileConventions.YamlKeys.ObserverHostPath,
                RunProfileConventions.YamlKeys.Scope,
                RunProfileConventions.YamlKeys.TokenEndpoint
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownPlannerKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.AnomalyWallClockCapSeconds,
                RunProfileConventions.YamlKeys.AspnetcoreUrls,
                RunProfileConventions.YamlKeys.BatchWallClockCapSeconds,
                RunProfileConventions.YamlKeys.ClientId,
                RunProfileConventions.YamlKeys.ClientSecret,
                RunProfileConventions.YamlKeys.ExecutorHandoffUrl,
                RunProfileConventions.YamlKeys.FileSinkRoot,
                RunProfileConventions.YamlKeys.GatewayBaseUrl,
                RunProfileConventions.YamlKeys.LlmApiKey,
                RunProfileConventions.YamlKeys.LlmModel,
                RunProfileConventions.YamlKeys.LlmProvider,
                RunProfileConventions.YamlKeys.MaxToolIterations,
                RunProfileConventions.YamlKeys.OAuthAuthority,
                RunProfileConventions.YamlKeys.PlannerHostPath,
                RunProfileConventions.YamlKeys.Scope,
                RunProfileConventions.YamlKeys.TokenEndpoint
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownExecutorKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.AspnetcoreUrls,
                RunProfileConventions.YamlKeys.ClientId,
                RunProfileConventions.YamlKeys.ClientSecret,
                RunProfileConventions.YamlKeys.ConcurrencyCap,
                RunProfileConventions.YamlKeys.ExecutorHostPath,
                RunProfileConventions.YamlKeys.GatewayBaseUrl,
                RunProfileConventions.YamlKeys.OAuthAuthority,
                RunProfileConventions.YamlKeys.Scope,
                RunProfileConventions.YamlKeys.TokenEndpoint,
                RunProfileConventions.YamlKeys.WatchTimeoutSeconds
            ],
            StringComparer.Ordinal);

    public static async Task<RunProfileDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var yamlStream = new YamlStream();
        using var reader = new StringReader(content);
        try
        {
            yamlStream.Load(reader);
        }
        catch (YamlException ex) when (ex.Message.StartsWith("Duplicate key ", StringComparison.Ordinal))
        {
            string key = ex.Message["Duplicate key ".Length..];
            throw new InvalidOperationException($"Duplicate profile name: {key}");
        }

        if (yamlStream.Documents.Count != 1 ||
            yamlStream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Run profile YAML must contain one mapping document.");
        }

        ValidateKnownKeys(root, KnownRootKeys);
        ProfileDefaults? defaults = ReadDefaults(root);
        YamlMappingNode profilesNode = GetRequiredMapping(root, RunProfileConventions.YamlKeys.Profiles);
        var profiles = new List<RunProfile>();
        foreach (KeyValuePair<YamlNode, YamlNode> entry in profilesNode.Children)
        {
            string profileName = ScalarValue(entry.Key, "profile name");
            if (entry.Value is not YamlMappingNode profileNode)
            {
                throw new InvalidOperationException($"Profile '{profileName}' must be a mapping.");
            }

            ValidateKnownKeys(profileNode, KnownProfileKeys);
            string kind = GetRequiredScalar(profileNode, RunProfileConventions.YamlKeys.Kind);
            string? runtimeMode = GetOptionalScalar(profileNode, RunProfileConventions.YamlKeys.RuntimeMode);
            GatewayProfile? gateway = ReadGateway(profileNode);
            IdentityProviderProfile? identityProvider = ReadIdentityProvider(profileNode);
            ApprovalAuthorityProfile? approvalAuthority = ReadApprovalAuthority(profileNode);
            GenericApprovalCoreProfile? genericApprovalCore = ReadGenericApprovalCore(profileNode);
            IReadOnlyList<DomainAdapterProfile> domainAdapters = ReadDomainAdapters(profileNode);
            HostProfile? host = ReadHost(profileNode);
            DownstreamAuthProfile? downstreamAuth = ReadDownstreamAuth(profileNode);

            if (domainAdapters.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Run Profile '{profileName}' must declare exactly one Domain Adapter.");
            }

            ObserverProfile? observer = ReadObserver(profileNode);
            PlannerProfile? planner = ReadPlanner(profileNode);
            ExecutorProfile? executor = ReadExecutor(profileNode);

            profiles.Add(new RunProfile(
                profileName,
                kind,
                runtimeMode,
                gateway,
                identityProvider,
                approvalAuthority,
                genericApprovalCore,
                domainAdapters,
                host,
                downstreamAuth,
                observer,
                planner,
                executor));
        }

        return new RunProfileDocument(profiles) { Defaults = defaults };
    }

    private static ProfileDefaults? ReadDefaults(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Defaults),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Defaults}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownDefaultsKeys);
        return new ProfileDefaults(
            ReadGateway(mapping),
            ReadIdentityProvider(mapping),
            ReadApprovalAuthority(mapping),
            ReadGenericApprovalCore(mapping),
            ReadHost(mapping),
            ReadDownstreamAuth(mapping),
            ReadObserver(mapping),
            ReadPlanner(mapping),
            ReadExecutor(mapping));
    }

    private static DownstreamAuthProfile? ReadDownstreamAuth(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.DownstreamAuth),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.DownstreamAuth}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownDownstreamAuthKeys);
        return new DownstreamAuthProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Required),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Authority),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.MetadataAddress),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.RequireHttpsMetadata),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Audience),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Scope),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayClientId),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayClientSecret));
    }

    private static GatewayProfile? ReadGateway(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Gateway),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Gateway}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownGatewayKeys);
        return new GatewayProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.AspnetcoreUrls),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.DownstreamAssembly),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GuardAuditRoot));
    }

    private static IdentityProviderProfile? ReadIdentityProvider(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.IdentityProvider),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.IdentityProvider}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownIdentityProviderKeys);
        return new IdentityProviderProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.RealmImport),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Authority),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.MetadataAddress),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Resource),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Scope),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.RequireHttpsMetadata));
    }

    private static ApprovalAuthorityProfile? ReadApprovalAuthority(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.ApprovalAuthority),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.ApprovalAuthority}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownApprovalAuthorityKeys);
        return new ApprovalAuthorityProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.BaseUrl),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OauthClientId),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OauthCallbackPath),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OauthAuthorizationEndpoint),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OauthTokenEndpoint));
    }

    private static GenericApprovalCoreProfile? ReadGenericApprovalCore(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.GenericApprovalCore),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.GenericApprovalCore}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownGenericApprovalCoreKeys);
        string approvalRoot = GetRequiredScalar(mapping, RunProfileConventions.YamlKeys.ApprovalRoot);
        string? postgresConnectionString = GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.PostgresConnectionString);
        string? runMigrationsOnStartup = GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.RunMigrationsOnStartup);
        bool? runMigrationsOnStartupBool = null;
        if (!string.IsNullOrEmpty(runMigrationsOnStartup) && bool.TryParse(runMigrationsOnStartup, out bool parsed))
        {
            runMigrationsOnStartupBool = parsed;
        }

        return new GenericApprovalCoreProfile(approvalRoot, postgresConnectionString, runMigrationsOnStartupBool);
    }

    private static HostProfile? ReadHost(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Host),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Host}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownHostKeys);
        return new HostProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.BindAddress),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.BindPort),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayImage),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ConfigHostPath),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.KubeconfigHostPath),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ApprovalHostPath),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GuardAuditHostPath),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.DataProtectionHostPath));
    }

    private static ObserverProfile? ReadObserver(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Observer),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Observer}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownObserverKeys);
        return new ObserverProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.AspnetcoreUrls),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayBaseUrl),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.TokenEndpoint),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientId),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientSecret),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Scope),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmProvider),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmModel),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmApiKey),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.CycleCadenceSeconds),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.CycleWallClockCapSeconds),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.MaxToolIterations),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.FileSinkRoot),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ObserverHostPath));
    }

    private static PlannerProfile? ReadPlanner(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Planner),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Planner}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownPlannerKeys);
        return new PlannerProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.AspnetcoreUrls),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayBaseUrl),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ExecutorHandoffUrl),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.TokenEndpoint),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientId),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientSecret),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OAuthAuthority),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Scope),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmProvider),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmModel),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.LlmApiKey),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.AnomalyWallClockCapSeconds),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.BatchWallClockCapSeconds),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.MaxToolIterations),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.FileSinkRoot),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.PlannerHostPath));
    }

    private static ExecutorProfile? ReadExecutor(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Executor),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Executor}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownExecutorKeys);
        return new ExecutorProfile(
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.AspnetcoreUrls),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.GatewayBaseUrl),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.TokenEndpoint),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientId),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ClientSecret),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.OAuthAuthority),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.Scope),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ConcurrencyCap),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.WatchTimeoutSeconds),
            GetOptionalScalar(mapping, RunProfileConventions.YamlKeys.ExecutorHostPath));
    }

    private static IReadOnlyList<DomainAdapterProfile> ReadDomainAdapters(YamlMappingNode profileNode)
    {
        if (!profileNode.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.DomainAdapters),
                out YamlNode? value))
        {
            return [];
        }

        if (value is not YamlSequenceNode sequence)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.DomainAdapters}' must be a sequence.");
        }

        var adapters = new List<DomainAdapterProfile>();
        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlMappingNode adapterNode)
            {
                throw new InvalidOperationException("Domain adapter entries must be mappings.");
            }

            ValidateKnownKeys(adapterNode, KnownDomainAdapterKeys);
            string name = GetRequiredScalar(adapterNode, RunProfileConventions.YamlKeys.Name);
            string type = GetRequiredScalar(adapterNode, RunProfileConventions.YamlKeys.Type);
            if (!string.Equals(type, RunProfileConventions.DomainAdapterTypes.Kubernetes, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported Domain Adapter type: {type}");
            }

            KubernetesAdapterProfile? kubernetes = ReadKubernetesAdapter(adapterNode);
            adapters.Add(new DomainAdapterProfile(name, type, kubernetes));
        }

        return adapters;
    }

    private static KubernetesAdapterProfile? ReadKubernetesAdapter(YamlMappingNode adapterNode)
    {
        if (!adapterNode.Children.TryGetValue(
                new YamlScalarNode(RunProfileConventions.YamlKeys.Kubernetes),
                out YamlNode? value))
        {
            return null;
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                $"YAML key '{RunProfileConventions.YamlKeys.Kubernetes}' must be a mapping.");
        }

        ValidateKnownKeys(mapping, KnownKubernetesKeys);
        string kubeConfig = GetRequiredScalar(mapping, RunProfileConventions.YamlKeys.KubeConfig);
        IReadOnlyList<string> allowedNamespaces = GetRequiredScalarSequence(
            mapping,
            RunProfileConventions.YamlKeys.AllowedNamespaces);

        return new KubernetesAdapterProfile(kubeConfig, allowedNamespaces);
    }

    private static void ValidateKnownKeys(YamlMappingNode node, IReadOnlySet<string> knownKeys)
    {
        foreach (YamlNode keyNode in node.Children.Keys)
        {
            string key = ScalarValue(keyNode, "key");
            if (!knownKeys.Contains(key))
            {
                throw new InvalidOperationException($"Unknown YAML key: {key}");
            }
        }
    }

    private static YamlMappingNode GetRequiredMapping(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            throw new InvalidOperationException($"Missing required YAML key: {key}");
        }

        if (value is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException($"YAML key '{key}' must be a mapping.");
        }

        return mapping;
    }

    private static string GetRequiredScalar(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            throw new InvalidOperationException($"Missing required YAML key: {key}");
        }

        return ScalarValue(value, key);
    }

    private static string? GetOptionalScalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
            ? ScalarValue(value, key)
            : null;
    }

    private static IReadOnlyList<string> GetRequiredScalarSequence(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            throw new InvalidOperationException($"Missing required YAML key: {key}");
        }

        if (value is not YamlSequenceNode sequence)
        {
            throw new InvalidOperationException($"YAML key '{key}' must be a sequence.");
        }

        var values = new List<string>();
        foreach (YamlNode item in sequence.Children)
        {
            values.Add(ScalarValue(item, key));
        }

        return values;
    }

    private static string ScalarValue(YamlNode node, string description)
    {
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new InvalidOperationException($"YAML value '{description}' must be a non-empty scalar.");
        }

        return scalar.Value;
    }
}
