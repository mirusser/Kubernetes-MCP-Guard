using System.Text.Json;
using InfraGate.Approvals;
using k8s;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    private const int MaxReplicas = K8sConventions.MaxReplicas;
    private const string FieldManager = K8sConventions.ServiceName;
    private const string RestartedAtAnnotation = K8sConventions.K8sResources.RestartedAtAnnotation;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly K8sMcpOptions options;
    private readonly ApprovalStore approvalStore;
    private readonly IKubernetes client;

    public K8sManager(K8sMcpOptions options, ApprovalStore approvalStore, IKubernetes client)
    {
        this.options = options;
        this.approvalStore = approvalStore;
        this.client = client;
    }
}
