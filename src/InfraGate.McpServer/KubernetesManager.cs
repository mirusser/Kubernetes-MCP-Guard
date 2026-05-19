using k8s;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

// Justification: Kubernetes is the canonical name for K8s. S101 is a false positive here.
public sealed partial class KubernetesManager
{
    private readonly KubernetesMcpOptions options;
    private readonly IKubernetes client;
    private readonly ILogger<KubernetesManager> logger;

    public KubernetesManager(KubernetesMcpOptions options, IKubernetes client, ILogger<KubernetesManager> logger)
    {
        this.options = options;
        this.client = client;
        this.logger = logger;
    }
}
