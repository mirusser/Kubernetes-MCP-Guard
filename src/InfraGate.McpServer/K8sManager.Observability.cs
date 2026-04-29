using System.Globalization;
using System.Text.Json;
using k8s;
using k8s.Models;

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
                events = events.Items.Select(k8sEvent => new
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
                })
            }, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
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

            return normalizedKind switch
            {
                var value when IsKind(value, K8sConventions.K8sResources.Deployment) =>
                    await ReadDeploymentSummaryAsync(namespaceName, name, cancellationToken),
                var value when IsKind(value, K8sConventions.K8sResources.ReplicaSet) =>
                    await ReadReplicaSetSummaryAsync(namespaceName, name, cancellationToken),
                var value when IsKind(value, K8sConventions.K8sResources.Pod) =>
                    await ReadPodSummaryAsync(namespaceName, name, cancellationToken),
                var value when IsKind(value, K8sConventions.K8sResources.Service) =>
                    await ReadServiceSummaryAsync(namespaceName, name, cancellationToken),
                var value when IsKind(value, K8sConventions.K8sResources.ConfigMap) =>
                    await ReadConfigMapSummaryAsync(namespaceName, name, cancellationToken),
                _ => UnsupportedResourceKindMessage(normalizedKind)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FormatApiException("Resource read failed", ex);
        }
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

        return JsonSerializer.Serialize(new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.Deployment,
            name = deployment.Metadata?.Name,
            labels = deployment.Metadata?.Labels,
            replicas = new
            {
                desired = deployment.Spec?.Replicas,
                ready = deployment.Status?.ReadyReplicas,
                available = deployment.Status?.AvailableReplicas,
                updated = deployment.Status?.UpdatedReplicas
            },
            selector = deployment.Spec?.Selector?.MatchLabels,
            conditions = deployment.Status?.Conditions?.Select(ConditionSummary)
        }, JsonOptions);
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

        return JsonSerializer.Serialize(new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.ReplicaSet,
            name = replicaSet.Metadata?.Name,
            labels = replicaSet.Metadata?.Labels,
            replicas = new
            {
                desired = replicaSet.Spec?.Replicas,
                ready = replicaSet.Status?.ReadyReplicas,
                available = replicaSet.Status?.AvailableReplicas
            },
            selector = replicaSet.Spec?.Selector?.MatchLabels,
            conditions = replicaSet.Status?.Conditions?.Select(ConditionSummary)
        }, JsonOptions);
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

        return JsonSerializer.Serialize(new
        {
            @namespace = namespaceName,
            kind = K8sConventions.K8sResources.Pod,
            name = pod.Metadata?.Name,
            labels = pod.Metadata?.Labels,
            phase = pod.Status?.Phase,
            reason = pod.Status?.Reason,
            message = pod.Status?.Message,
            podIp = pod.Status?.PodIP,
            hostIp = pod.Status?.HostIP,
            conditions = pod.Status?.Conditions?.Select(ConditionSummary),
            containers = pod.Status?.ContainerStatuses?.Select(status => new
            {
                name = status.Name,
                ready = status.Ready,
                restartCount = status.RestartCount,
                state = ContainerStateSummary(status.State)
            })
        }, JsonOptions);
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
            ports = service.Spec?.Ports?.Select(port => new
            {
                name = port.Name,
                port = port.Port,
                targetPort = port.TargetPort?.ToString(),
                nodePort = port.NodePort,
                protocol = port.Protocol
            })
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
}
