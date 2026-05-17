using System.Text.Json;
using k8s;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class K8sManager
{
    private const int MaxReplicas = K8sConventions.MaxReplicas;
    private const string FieldManager = K8sConventions.ServiceName;
    private const string RestartedAtAnnotation = K8sConventions.K8sResources.RestartedAtAnnotation;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly K8SMcpOptions options;
    private readonly IKubernetes client;
    private readonly ILogger<K8sManager> logger;

    public K8sManager(K8SMcpOptions options, IKubernetes client, ILogger<K8sManager> logger)
    {
        this.options = options;
        this.client = client;
        this.logger = logger;
    }
}
