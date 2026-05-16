using YamlDotNet.RepresentationModel;

namespace InfraGate.RunProfiles;

internal static class RunProfileDocumentReader
{
    private static readonly IReadOnlySet<string> KnownRootKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Version,
                RunProfileConventions.YamlKeys.Profiles
            ],
            StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> KnownProfileKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Kind,
                RunProfileConventions.YamlKeys.RuntimeMode,
                RunProfileConventions.YamlKeys.GenericApprovalCore,
                RunProfileConventions.YamlKeys.DomainAdapters
            ],
            StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> KnownGenericApprovalCoreKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.ApprovalRoot
            ],
            StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> KnownDomainAdapterKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.Name,
                RunProfileConventions.YamlKeys.Type,
                RunProfileConventions.YamlKeys.Kubernetes
            ],
            StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> KnownKubernetesKeys =
        new HashSet<string>(
            [
                RunProfileConventions.YamlKeys.KubeConfig,
                RunProfileConventions.YamlKeys.AllowedNamespaces
            ],
            StringComparer.Ordinal);

    public static async Task<RunProfileDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var yamlStream = new YamlStream();
        using var reader = new StringReader(content);
        yamlStream.Load(reader);

        if (yamlStream.Documents.Count != 1 ||
            yamlStream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Run profile YAML must contain one mapping document.");
        }

        ValidateKnownKeys(root, KnownRootKeys);
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
            GenericApprovalCoreProfile? genericApprovalCore = ReadGenericApprovalCore(profileNode);
            IReadOnlyList<DomainAdapterProfile> domainAdapters = ReadDomainAdapters(profileNode);
            if (domainAdapters.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Run Profile '{profileName}' must declare exactly one Domain Adapter.");
            }

            profiles.Add(new RunProfile(profileName, kind, runtimeMode, genericApprovalCore, domainAdapters));
        }

        return new RunProfileDocument(profiles);
    }

    private static GenericApprovalCoreProfile? ReadGenericApprovalCore(YamlMappingNode profileNode)
    {
        if (!profileNode.Children.TryGetValue(
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
        return new GenericApprovalCoreProfile(approvalRoot);
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
