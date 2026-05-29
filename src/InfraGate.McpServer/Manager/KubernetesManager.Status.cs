using System.Text.Json;
using k8s;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

public sealed partial class KubernetesManager
{
    public async Task<string> GetStatusAsync(string namespaceName, string? labelSelector, CancellationToken cancellationToken)
    {
        // Justification: CA1873 — all log arguments are simple scalars (strings). Negligible evaluation cost.
        logger.LogInformation("GetStatus called: Namespace={Namespace}, LabelSelector={LabelSelector}", namespaceName, labelSelector);

        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        labelSelector = string.IsNullOrWhiteSpace(labelSelector) ? null : labelSelector;

        try
        {
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var services = await client.CoreV1.ListNamespacedServiceAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var configMaps = await client.CoreV1.ListNamespacedConfigMapAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var pods = await client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var replicaSets = await client.AppsV1.ListNamespacedReplicaSetAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "GetStatus result: Namespace={Namespace}, Deployments={DeploymentCount}, Services={ServiceCount}, ConfigMaps={ConfigMapCount}, Pods={PodCount}, ReplicaSets={ReplicaSetCount}",
                    namespaceName,
                    deployments.Items.Count,
                    services.Items.Count,
                    configMaps.Items.Count,
                    pods.Items.Count,
                    replicaSets.Items.Count);
            }

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                labelSelector,
                deployments = deployments.Items.Select(deployment => new
                {
                    name = deployment.Metadata.Name,
                    labels = deployment.Metadata.Labels,
                    replicas = new
                    {
                        desired = deployment.Spec.Replicas,
                        ready = deployment.Status.ReadyReplicas,
                        available = deployment.Status.AvailableReplicas,
                        updated = deployment.Status.UpdatedReplicas
                    }
                }),
                services = services.Items.Select(service => new
                {
                    name = service.Metadata.Name,
                    labels = service.Metadata.Labels,
                    type = service.Spec.Type,
                    clusterIp = service.Spec.ClusterIP,
                    ports = service.Spec.Ports.Select(port => new
                    {
                        name = port.Name,
                        port = port.Port,
                        targetPort = port.TargetPort?.ToString(),
                        nodePort = port.NodePort
                    })
                }),
                configMaps = configMaps.Items.Select(configMap => new
                {
                    name = configMap.Metadata.Name,
                    labels = configMap.Metadata.Labels,
                    dataKeys = configMap.Data?.Keys.Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>()
                }),
                pods = pods.Items.Select(pod => new
                {
                    name = pod.Metadata.Name,
                    labels = pod.Metadata.Labels,
                    phase = pod.Status.Phase,
                    readyContainers = pod.Status.ContainerStatuses?.Count(status => status.Ready) ?? 0,
                    totalContainers = pod.Status.ContainerStatuses?.Count ?? 0,
                    restarts = pod.Status.ContainerStatuses?.Sum(status => status.RestartCount) ?? 0
                }),
                replicaSets = replicaSets.Items.Select(replicaSet => new
                {
                    name = replicaSet.Metadata.Name,
                    labels = replicaSet.Metadata.Labels,
                    replicas = new
                    {
                        desired = replicaSet.Spec.Replicas,
                        ready = replicaSet.Status.ReadyReplicas,
                        available = replicaSet.Status.AvailableReplicas
                    }
                })
            }, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Status read failed for namespace {Namespace}", namespaceName);
            return KubernetesManagerHelpers.FormatApiException("Status read failed", ex);
        }
    }
}
