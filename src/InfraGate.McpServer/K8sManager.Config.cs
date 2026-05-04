using System.Text.Json;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public Task<string> GetAllowedNamespacesAsync() =>
        Task.FromResult(JsonSerializer.Serialize(new
        {
            allowedNamespaces = options.AllowedNamespaces.Order(StringComparer.Ordinal).ToArray(),
            count = options.AllowedNamespaces.Count
        }, JsonOptions));
}
