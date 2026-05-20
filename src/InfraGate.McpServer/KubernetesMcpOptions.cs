using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpServer;

public sealed record KubernetesMcpOptions(
    IReadOnlySet<string> AllowedNamespaces,
    string ApprovalRoot,
    RuntimeMode RuntimeMode = RuntimeMode.Development,
    bool IsApprovalRootExplicit = true,
    bool HasExplicitAllowedNamespaces = true,
    string? KubeConfig = null,
    bool IsInClusterConfigEnabled = false,
    string? LogPath = null,
    DownstreamAuthOptions? DownstreamAuth = null)
{
    public const string DefaultNamespace = KubernetesConventions.DefaultNamespace;
    private static readonly IReadOnlySet<string> DeniedApprovalRootNames =
        new HashSet<string>([ApprovalConventions.Storage.DefaultRootDirectory], StringComparer.Ordinal);

    public bool HasExplicitKubeConfig => !string.IsNullOrWhiteSpace(KubeConfig);

    public bool IsNamespaceAllowed(string namespaceName) =>
        AllowedNamespaces.Contains(namespaceName);

    public static KubernetesMcpOptions FromEnvironment()
    {
        RuntimeMode runtimeMode = RuntimeModeResolver.FromEnvironment();
        var downstreamAuth = DownstreamAuthOptions.FromEnvironment();
        string? approvalRootValue = Environment.GetEnvironmentVariable(KubernetesConventions.EnvironmentVariables.ApprovalRoot);
        bool isApprovalRootExplicit = !string.IsNullOrWhiteSpace(approvalRootValue);
        string approvalRoot = string.IsNullOrWhiteSpace(approvalRootValue)
            ? Path.Combine(Directory.GetCurrentDirectory(), ApprovalConventions.Storage.DefaultRootDirectory)
            : approvalRootValue;

        string? allowedNamespacesValue =
            Environment.GetEnvironmentVariable(KubernetesConventions.EnvironmentVariables.AllowedNamespaces);
        bool hasExplicitAllowedNamespaces = !string.IsNullOrWhiteSpace(allowedNamespacesValue);
        IReadOnlySet<string> allowedNamespaces = ParseAllowedNamespaces(allowedNamespacesValue);
        string? kubeConfig = Environment.GetEnvironmentVariable(KubernetesConventions.EnvironmentVariables.KubeConfig);
        bool isInClusterConfigEnabled = ParseBooleanEnvironmentVariable(
            Environment.GetEnvironmentVariable(KubernetesConventions.EnvironmentVariables.UseInClusterConfig),
            defaultValue: false);
        string? logPath = Environment.GetEnvironmentVariable(KubernetesConventions.EnvironmentVariables.LogPath);

        return new KubernetesMcpOptions(
            allowedNamespaces,
            approvalRoot,
            runtimeMode,
            isApprovalRootExplicit,
            hasExplicitAllowedNamespaces,
            kubeConfig,
            isInClusterConfigEnabled,
            logPath,
            downstreamAuth);
    }

    public static KubernetesMcpOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        RuntimeMode runtimeMode = RuntimeModeResolver.FromConfiguration(configuration);

        var k8sSettings = configuration
            .GetSection("InfraGate:Kubernetes")
            .Get<InfraGateKubernetesSettings>();

        string? approvalRootValue = configuration[KubernetesConventions.ConfigurationKeys.ApprovalRoot];
        bool isApprovalRootExplicit = !string.IsNullOrWhiteSpace(approvalRootValue);
        string approvalRoot = string.IsNullOrWhiteSpace(approvalRootValue)
            ? Path.Combine(Directory.GetCurrentDirectory(), ApprovalConventions.Storage.DefaultRootDirectory)
            : approvalRootValue;

        string? allowedNamespacesValue = configuration[KubernetesConventions.EnvironmentVariables.AllowedNamespaces];
        bool hasExplicitAllowedNamespaces = !string.IsNullOrWhiteSpace(allowedNamespacesValue) ||
            (k8sSettings?.AllowedNamespaces is { Count: > 0 }) ||
            !string.IsNullOrWhiteSpace(configuration[KubernetesConventions.ConfigurationKeys.AllowedNamespaces]);
        IReadOnlySet<string> allowedNamespaces = ParseAllowedNamespaces(configuration, allowedNamespacesValue, k8sSettings);
        string? kubeConfig = k8sSettings?.KubeConfig;
        bool isInClusterConfigEnabled = k8sSettings?.UseInClusterConfig ?? false;
        string? logPath = k8sSettings?.LogPath;

        return new KubernetesMcpOptions(
            allowedNamespaces,
            approvalRoot,
            runtimeMode,
            isApprovalRootExplicit,
            hasExplicitAllowedNamespaces,
            kubeConfig,
            isInClusterConfigEnabled,
            logPath);
    }

    public void ValidateProductionSafety()
    {
        if (HasExplicitKubeConfig && IsInClusterConfigEnabled)
        {
            throw new InvalidOperationException(
                $"{KubernetesConventions.EnvironmentVariables.KubeConfig} and " +
                $"{KubernetesConventions.EnvironmentVariables.UseInClusterConfig}=true cannot both be configured.");
        }

        if (RuntimeMode != RuntimeMode.Production)
        {
            return;
        }

        var downstreamAuth = DownstreamAuth ?? new DownstreamAuthOptions();
        if (!downstreamAuth.Required)
        {
            throw new InvalidOperationException(
                $"{DownstreamAuthConventions.EnvironmentVariables.Required} must not be false in Production mode. " +
                $"Downstream authentication is required for production deployments.");
        }

        downstreamAuth.ValidateForServer();

        if (!HasExplicitKubeConfig && !IsInClusterConfigEnabled)
        {
            throw new InvalidOperationException(
                $"Production mode requires explicit Kubernetes auth: set " +
                $"{KubernetesConventions.EnvironmentVariables.KubeConfig} or " +
                $"{KubernetesConventions.EnvironmentVariables.UseInClusterConfig}=true.");
        }

        if (!HasExplicitAllowedNamespaces || AllowedNamespaces.Count == 0)
        {
            throw new InvalidOperationException(
                $"{KubernetesConventions.EnvironmentVariables.AllowedNamespaces} must be explicitly configured in Production mode.");
        }

        ProductionSafetyValidator.RequirePersistentDirectory(
            ApprovalRoot,
            KubernetesConventions.EnvironmentVariables.ApprovalRoot,
            IsApprovalRootExplicit,
            DeniedApprovalRootNames);
    }

    public static IReadOnlySet<string> ParseAllowedNamespaces(string? value)
    {
        var namespaces = string.IsNullOrWhiteSpace(value)
            ? [DefaultNamespace]
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return namespaces
            .Where(namespaceName => !string.IsNullOrWhiteSpace(namespaceName))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool ParseBooleanEnvironmentVariable(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out bool result))
        {
            throw new InvalidOperationException(
                $"{KubernetesConventions.EnvironmentVariables.UseInClusterConfig} must be 'true' or 'false'; value '{value}' is not supported.");
        }

        return result;
    }

    private static IReadOnlySet<string> ParseAllowedNamespaces(
        IConfiguration configuration,
        string? environmentValue,
        InfraGateKubernetesSettings? k8sSettings)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return ParseAllowedNamespaces(environmentValue);
        }

        if (k8sSettings?.AllowedNamespaces is { Count: > 0 })
        {
            return k8sSettings.AllowedNamespaces
                .Where(namespaceName => !string.IsNullOrWhiteSpace(namespaceName))
                .ToHashSet(StringComparer.Ordinal);
        }

        return ParseAllowedNamespaces(configuration[KubernetesConventions.ConfigurationKeys.AllowedNamespaces]);
    }

}
