using InfraGate.Approvals;

namespace InfraGate.McpServer;

public sealed record K8sMcpOptions(IReadOnlySet<string> AllowedNamespaces, string ApprovalRoot)
{
    public const string DefaultNamespace = K8sConventions.DefaultNamespace;

    public bool IsNamespaceAllowed(string namespaceName) =>
        AllowedNamespaces.Contains(namespaceName);

    public static K8sMcpOptions FromEnvironment()
    {
        var approvalRoot = Environment.GetEnvironmentVariable(K8sConventions.EnvironmentVariables.ApprovalRoot);
        if (string.IsNullOrWhiteSpace(approvalRoot))
        {
            approvalRoot = Path.Combine(Directory.GetCurrentDirectory(), ApprovalConventions.Storage.DefaultRootDirectory);
        }

        var allowedNamespaces = ParseAllowedNamespaces(
            Environment.GetEnvironmentVariable(K8sConventions.EnvironmentVariables.AllowedNamespaces));

        return new K8sMcpOptions(allowedNamespaces, approvalRoot);
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
}
