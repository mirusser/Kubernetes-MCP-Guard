using System.Globalization;
using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public async Task<string> GetEventsAsync(
        string namespaceName,
        string? labelSelector,
        string? fieldSelector,
        int limit,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ??
            ValidateBoundedCount(limit, K8sConventions.MaxEventLimit, "Limit");
        if (validation is not null)
        {
            return validation;
        }

        labelSelector = NormalizeOptionalText(labelSelector);
        fieldSelector = NormalizeOptionalText(fieldSelector);

        try
        {
            var events = await client.EventsV1.ListNamespacedEventAsync(
                namespaceName,
                fieldSelector: fieldSelector,
                labelSelector: labelSelector,
                limit: limit,
                cancellationToken: cancellationToken);

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                labelSelector,
                fieldSelector,
                limit,
                events = events.Items.Select(EventSummary)
            }, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Event read failed for namespace {Namespace}", namespaceName);
            return FormatApiException("Event read failed", ex);
        }
    }

    public async Task<string> GetPodLogsAsync(
        string namespaceName,
        string podName,
        string? container,
        int tailLines,
        bool previous,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ??
            ValidateName(podName) ??
            ValidateBoundedCount(tailLines, K8sConventions.MaxLogTailLines, "TailLines");
        if (validation is not null)
        {
            return validation;
        }

        container = NormalizeOptionalText(container);

        try
        {
            await using var logStream = await client.CoreV1.ReadNamespacedPodLogAsync(
                podName,
                namespaceName,
                container: container,
                follow: false,
                insecureSkipTLSVerifyBackend: false,
                limitBytes: K8sConventions.LogLimitBytes,
                previous: previous,
                tailLines: tailLines,
                cancellationToken: cancellationToken);
            using var reader = new StreamReader(logStream);
            var log = await reader.ReadToEndAsync(cancellationToken);

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                podName,
                container,
                previous,
                tailLines,
                limitBytes = K8sConventions.LogLimitBytes,
                log
            }, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Pod log read failed for {Namespace}/{PodName}", namespaceName, podName);
            return FormatApiException("Pod log read failed", ex);
        }
    }

    public async Task<string> GetResourceAsync(
        string namespaceName,
        string kind,
        string name,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ??
            ValidateResourceKind(kind) ??
            ValidateName(name);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var normalizedKind = kind.Trim();
            return await ReadResourceSummaryAsync(namespaceName, normalizedKind, name, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Resource read failed for {Namespace}/{Kind}/{Name}", namespaceName, kind, name);
            return FormatApiException("Resource read failed", ex);
        }
    }

    private Task<string> ReadResourceSummaryAsync(
        string namespaceName,
        string normalizedKind,
        string name,
        CancellationToken cancellationToken)
    {
        if (IsKind(normalizedKind, K8sConventions.K8sResources.Deployment))
        {
            return ReadDeploymentSummaryAsync(namespaceName, name, cancellationToken);
        }

        if (IsKind(normalizedKind, K8sConventions.K8sResources.ReplicaSet))
        {
            return ReadReplicaSetSummaryAsync(namespaceName, name, cancellationToken);
        }

        if (IsKind(normalizedKind, K8sConventions.K8sResources.Pod))
        {
            return ReadPodSummaryAsync(namespaceName, name, cancellationToken);
        }

        if (IsKind(normalizedKind, K8sConventions.K8sResources.Service))
        {
            return ReadServiceSummaryAsync(namespaceName, name, cancellationToken);
        }

        return IsKind(normalizedKind, K8sConventions.K8sResources.ConfigMap)
            ? ReadConfigMapSummaryAsync(namespaceName, name, cancellationToken)
            : Task.FromResult(UnsupportedResourceKindMessage(normalizedKind));
    }

    private async Task<string> ReadDeploymentSummaryAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(
            name,
            namespaceName,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(DeploymentResourceSummary(namespaceName, deployment), JsonOptions);
    }

    private async Task<string> ReadReplicaSetSummaryAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var replicaSet = await client.AppsV1.ReadNamespacedReplicaSetAsync(
            name,
            namespaceName,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(ReplicaSetResourceSummary(namespaceName, replicaSet), JsonOptions);
    }

    private async Task<string> ReadPodSummaryAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var pod = await client.CoreV1.ReadNamespacedPodAsync(
            name,
            namespaceName,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(PodResourceSummary(namespaceName, pod), JsonOptions);
    }

    private async Task<string> ReadServiceSummaryAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var service = await client.CoreV1.ReadNamespacedServiceAsync(
            name,
            namespaceName,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.Service,
            name = service.Metadata?.Name,
            labels = service.Metadata?.Labels,
            type = service.Spec?.Type,
            clusterIp = service.Spec?.ClusterIP,
            selector = service.Spec?.Selector,
            ports = ServicePortSummaries(service.Spec)
        }, JsonOptions);
    }

    private async Task<string> ReadConfigMapSummaryAsync(
        string namespaceName,
        string name,
        CancellationToken cancellationToken)
    {
        var configMap = await client.CoreV1.ReadNamespacedConfigMapAsync(
            name,
            namespaceName,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.ConfigMap,
            name = configMap.Metadata?.Name,
            labels = configMap.Metadata?.Labels,
            immutable = configMap.Immutable,
            dataKeys = configMap.Data?.Keys.Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
            binaryDataKeys = configMap.BinaryData?.Keys.Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>()
        }, JsonOptions);
    }

    private static object DeploymentResourceSummary(string namespaceName, V1Deployment deployment) => new
    {
        @namespace = namespaceName,
        kind = K8sConventions.K8sResources.Deployment,
        name = deployment.Metadata?.Name,
        labels = deployment.Metadata?.Labels,
        replicas = DeploymentReplicaSummary(deployment.Spec, deployment.Status),
        selector = DeploymentSelector(deployment.Spec),
        conditions = DeploymentConditionSummaries(deployment.Status)
    };

    private static object DeploymentReplicaSummary(V1DeploymentSpec? spec, V1DeploymentStatus? status) => new
    {
        desired = spec?.Replicas,
        ready = status?.ReadyReplicas,
        available = status?.AvailableReplicas,
        updated = status?.UpdatedReplicas
    };

    private static IDictionary<string, string>? DeploymentSelector(V1DeploymentSpec? spec) =>
        spec?.Selector?.MatchLabels;

    private static IEnumerable<object>? DeploymentConditionSummaries(V1DeploymentStatus? status) =>
        status?.Conditions?.Select(ConditionSummary);

    private static IEnumerable<object>? DeploymentContainerSummaries(V1DeploymentSpec? spec) =>
        spec?.Template?.Spec?.Containers?.Select(DeploymentContainerSummary);

    private static object DeploymentContainerSummary(V1Container container) => new
    {
        name = container.Name,
        image = container.Image
    };

    private static object ReplicaSetResourceSummary(string namespaceName, V1ReplicaSet replicaSet) => new
    {
        @namespace = namespaceName,
        kind = K8sConventions.K8sResources.ReplicaSet,
        name = replicaSet.Metadata?.Name,
        labels = replicaSet.Metadata?.Labels,
        replicas = ReplicaSetReplicaSummary(replicaSet.Spec, replicaSet.Status),
        selector = ReplicaSetSelector(replicaSet.Spec),
        conditions = ReplicaSetConditionSummaries(replicaSet.Status)
    };

    private static object ReplicaSetReplicaSummary(V1ReplicaSetSpec? spec, V1ReplicaSetStatus? status) => new
    {
        desired = spec?.Replicas,
        ready = status?.ReadyReplicas,
        available = status?.AvailableReplicas
    };

    private static IDictionary<string, string>? ReplicaSetSelector(V1ReplicaSetSpec? spec) =>
        spec?.Selector?.MatchLabels;

    private static IEnumerable<object>? ReplicaSetConditionSummaries(V1ReplicaSetStatus? status) =>
        status?.Conditions?.Select(ConditionSummary);

    private static object PodResourceSummary(string namespaceName, V1Pod pod)
    {
        var status = PodStatusFields.From(pod.Status);
        return new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.Pod,
            name = pod.Metadata?.Name,
            labels = pod.Metadata?.Labels,
            phase = status.Phase,
            reason = status.Reason,
            message = status.Message,
            podIp = status.PodIp,
            hostIp = status.HostIp,
            conditions = PodConditionSummaries(pod.Status),
            containers = PodContainerStatusSummaries(pod.Status)
        };
    }

    private static IEnumerable<object>? PodConditionSummaries(V1PodStatus? status) =>
        status?.Conditions?.Select(ConditionSummary);

    private static IEnumerable<object>? PodContainerStatusSummaries(V1PodStatus? status) =>
        status?.ContainerStatuses?.Select(PodResourceContainerStatusSummary);

    private static object PodResourceContainerStatusSummary(V1ContainerStatus status) => new
    {
        name = status.Name,
        ready = status.Ready,
        restartCount = status.RestartCount,
        state = ContainerStateSummary(status.State)
    };

    private static IEnumerable<object>? PodDiagnosticContainerStatusSummaries(V1PodStatus? status) =>
        status?.ContainerStatuses?.Select(PodDiagnosticContainerStatusSummary);

    private static object PodDiagnosticContainerStatusSummary(V1ContainerStatus status) => new
    {
        name = status.Name,
        ready = status.Ready,
        restartCount = status.RestartCount,
        image = status.Image,
        state = ContainerStateSummary(status.State)
    };

    private static IEnumerable<object>? ServicePortSummaries(V1ServiceSpec? spec) =>
        spec?.Ports?.Select(ServicePortSummary);

    private static object ServicePortSummary(V1ServicePort port) => new
    {
        name = port.Name,
        port = port.Port,
        targetPort = port.TargetPort?.ToString(),
        nodePort = port.NodePort,
        protocol = port.Protocol
    };

    private static string? ValidateBoundedCount(int value, int maxValue, string name)
    {
        return value is < 1 || value > maxValue
            ? $"{name} must be between 1 and {maxValue}."
            : null;
    }

    private static string? ValidateResourceKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Resource kind is required.";
        }

        if (IsKind(kind, K8sConventions.K8sResources.Secret))
        {
            return "Secret resource details are intentionally unavailable; Secret values are not exposed.";
        }

        return IsSupportedResourceSummaryKind(kind)
            ? null
            : UnsupportedResourceKindMessage(kind.Trim());
    }

    private static bool IsSupportedResourceSummaryKind(string kind) =>
        IsKind(kind, K8sConventions.K8sResources.Deployment) ||
        IsKind(kind, K8sConventions.K8sResources.ReplicaSet) ||
        IsKind(kind, K8sConventions.K8sResources.Pod) ||
        IsKind(kind, K8sConventions.K8sResources.Service) ||
        IsKind(kind, K8sConventions.K8sResources.ConfigMap);

    private static bool IsKind(string actual, string expected) =>
        string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string UnsupportedResourceKindMessage(string kind) =>
        $"Unsupported resource kind '{kind}'. Supported kinds: {K8sConventions.K8sResources.SupportedResourceSummaryKindsDescription}.";

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object ConditionSummary(V1DeploymentCondition condition) => new
    {
        type = condition.Type,
        status = condition.Status,
        reason = condition.Reason,
        message = condition.Message
    };

    private static object ConditionSummary(V1ReplicaSetCondition condition) => new
    {
        type = condition.Type,
        status = condition.Status,
        reason = condition.Reason,
        message = condition.Message
    };

    private static object ConditionSummary(V1PodCondition condition) => new
    {
        type = condition.Type,
        status = condition.Status,
        reason = condition.Reason,
        message = condition.Message
    };

    private static object EventSummary(Eventsv1Event k8sEvent) => new
    {
        name = k8sEvent.Metadata?.Name,
        type = k8sEvent.Type,
        reason = k8sEvent.Reason,
        action = k8sEvent.Action,
        note = k8sEvent.Note,
        eventTime = FormatK8sTime(k8sEvent.EventTime),
        reportingController = k8sEvent.ReportingController,
        reportingInstance = k8sEvent.ReportingInstance,
        regarding = k8sEvent.Regarding is null
            ? null
            : new
            {
                apiVersion = k8sEvent.Regarding.ApiVersion,
                kind = k8sEvent.Regarding.Kind,
                name = k8sEvent.Regarding.Name,
                @namespace = k8sEvent.Regarding.NamespaceProperty
            }
    };

    private static string? ContainerStateSummary(V1ContainerState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.Running is not null)
        {
            return "Running";
        }

        if (state.Waiting is not null)
        {
            return state.Waiting.Reason;
        }

        return state.Terminated?.Reason;
    }

    private static string? FormatK8sTime(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private sealed record PodStatusFields(
        string? Phase,
        string? Reason,
        string? Message,
        string? PodIp,
        string? HostIp)
    {
        public static PodStatusFields From(V1PodStatus? status) =>
            status is null
                ? new PodStatusFields(null, null, null, null, null)
                : new PodStatusFields(status.Phase, status.Reason, status.Message, status.PodIP, status.HostIP);
    }
}
