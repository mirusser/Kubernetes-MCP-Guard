namespace InfraGate.McpServer;

public sealed record K8sMcpOptions(IReadOnlySet<string> AllowedNamespaces, string ApprovalRoot)
{
    public const string DefaultNamespace = "mcp-nginx-demo";

    public bool IsNamespaceAllowed(string namespaceName) =>
        AllowedNamespaces.Contains(namespaceName);

    public static K8sMcpOptions FromEnvironment()
    {
        var approvalRoot = Environment.GetEnvironmentVariable("K8S_MCP_APPROVAL_ROOT");
        if (string.IsNullOrWhiteSpace(approvalRoot))
        {
            approvalRoot = Path.Combine(Directory.GetCurrentDirectory(), ".mcp-approvals");
        }

        var allowedNamespaces = ParseAllowedNamespaces(
            Environment.GetEnvironmentVariable("K8S_MCP_ALLOWED_NAMESPACES"));

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
