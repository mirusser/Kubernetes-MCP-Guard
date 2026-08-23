using k8s;
using k8s.KubeConfigModels;

namespace InfraGate.McpGateway;

// Descriptor for the secondary, read-only-only kubernetes-mcp-server downstream
// process (see docs/adr for the decision record). Deliberately a sibling type,
// not an extension of McpGatewayOptions: ValidateProductionSafety()'s
// DownstreamAuth.Required==true production gate is scoped to the primary
// downstream and must never apply to this always-unauthenticated descriptor.
public sealed record class KubernetesMcpServerProcessOptions(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Kubeconfig,
    string Context,
    IReadOnlySet<string> AllowedNamespaces)
{
    // Always false: this stock downstream does not participate in InfraGate's
    // service-token _meta convention. Trust relies on trusted
    // launch + process containment instead (see Gateway README security
    // priority order, where the downstream token already ranks last).
    public const bool AuthRequired = false;

    public static KubernetesMcpServerProcessOptions? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(
            McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection);
        string? command = section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerCommandKey];
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string[] arguments = section
            .GetSection(McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerArgumentsKey)
            .Get<string[]>() ?? [];
        string gatewayWorkingDirectory = Directory.GetCurrentDirectory();
        string workingDirectory =
            section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerWorkingDirectoryKey]
            ?? gatewayWorkingDirectory;
        string? kubeconfig =
            section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerKubeconfigKey];
        if (string.IsNullOrWhiteSpace(kubeconfig))
        {
            throw new InvalidOperationException(
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:" +
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerKubeconfigKey} is required when the secondary downstream is enabled.");
        }

        string? primaryKubeconfig = configuration[
            McpGatewayConventions.ConfigurationKeys.PrimaryKubeconfig];
        if (!string.IsNullOrWhiteSpace(primaryKubeconfig)
            && ArePathsEqual(
                kubeconfig,
                workingDirectory,
                primaryKubeconfig,
                gatewayWorkingDirectory))
        {
            throw new InvalidOperationException(
                "The Kubernetes MCP secondary kubeconfig must be distinct from the primary downstream kubeconfig.");
        }

        string? context = section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerContextKey];
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new InvalidOperationException(
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:" +
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerContextKey} is required when the secondary downstream is enabled.");
        }

        ValidateKubeconfig(kubeconfig, workingDirectory, context);

        string[] configuredNamespaces = section
            .GetSection(McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerAllowedNamespacesKey)
            .Get<string[]>() ?? [];
        IReadOnlySet<string> allowedNamespaces = configuredNamespaces
            .Where(namespaceName => !string.IsNullOrWhiteSpace(namespaceName))
            .ToHashSet(StringComparer.Ordinal);
        if (configuredNamespaces.Length == 0
            || configuredNamespaces.Any(string.IsNullOrWhiteSpace)
            || allowedNamespaces.Contains("*"))
        {
            throw new InvalidOperationException(
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:" +
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerAllowedNamespacesKey} must contain exact, non-wildcard namespaces when the secondary downstream is enabled.");
        }

        ValidateArguments(arguments, workingDirectory);

        return new KubernetesMcpServerProcessOptions(
            command,
            arguments,
            workingDirectory,
            kubeconfig,
            context,
            allowedNamespaces);
    }

    private static bool ArePathsEqual(
        string left,
        string leftWorkingDirectory,
        string right,
        string rightWorkingDirectory)
    {
        string normalizedLeft = Path.GetFullPath(left, Path.GetFullPath(leftWorkingDirectory));
        string normalizedRight = Path.GetFullPath(right, Path.GetFullPath(rightWorkingDirectory));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    internal static void ValidateArguments(IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (arguments.Count != 2
            || !string.Equals(
                arguments[0],
                McpGatewayConventions.SecondaryDownstream.ConfigArgument,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[1])
            || arguments[1].StartsWith('-'))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server arguments must contain exactly '{McpGatewayConventions.SecondaryDownstream.ConfigArgument} <path>'.");
        }

        string configPath = Path.GetFullPath(arguments[1], Path.GetFullPath(workingDirectory));
        string? configDirectory = Path.GetDirectoryName(configPath);
        if (configDirectory is null)
        {
            return;
        }

        string dropInDirectory = Path.Combine(
            configDirectory,
            McpGatewayConventions.SecondaryDownstream.DropInConfigurationDirectoryName);
        if (Directory.Exists(dropInDirectory)
            && Directory.EnumerateFiles(
                    dropInDirectory,
                    McpGatewayConventions.SecondaryDownstream.TomlSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .Any(path => !Path.GetFileName(path).StartsWith('.')))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP server drop-in configuration directory '{dropInDirectory}' must not contain TOML overrides.");
        }
    }

    private static void ValidateKubeconfig(string kubeconfig, string workingDirectory, string expectedContext)
    {
        string path = Path.GetFullPath(kubeconfig, Path.GetFullPath(workingDirectory));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Kubernetes MCP secondary kubeconfig '{path}' does not exist.");
        }

        K8SConfiguration configuration = KubernetesClientConfiguration.LoadKubeConfig(path, useRelativePaths: true);
        Context[] contexts = configuration.Contexts?.ToArray() ?? [];
        if (contexts.Length != 1)
        {
            throw new InvalidOperationException(
                "The Kubernetes MCP secondary kubeconfig must contain exactly one context.");
        }

        if (!string.Equals(configuration.CurrentContext, expectedContext, StringComparison.Ordinal)
            || !string.Equals(contexts[0].Name, expectedContext, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Kubernetes MCP secondary kubeconfig current context must be '{expectedContext}'.");
        }
    }
}
