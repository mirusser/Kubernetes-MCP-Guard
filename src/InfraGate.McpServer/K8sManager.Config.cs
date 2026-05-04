using System.Text.Json;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public Task<string> GetAllowedNamespacesAsync()
    {
        var namespaces = options.AllowedNamespaces
            .Order(StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            allowedNamespaces = namespaces,
            count = namespaces.Length
        }, JsonOptions));
    }
}
