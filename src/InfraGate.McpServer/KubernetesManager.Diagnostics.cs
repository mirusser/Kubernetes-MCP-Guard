using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer;

// Justification: K8s is the canonical industry abbreviation for Kubernetes (not K8S). S101 is a false positive here.
public sealed partial class KubernetesManager
{
    public async Task<string> GetDeploymentDiagnosticsAsync(
        string namespaceName,
        string name,
        int limit,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ??
            KubernetesManagerHelpers.ValidateName(name) ??
            ValidateBoundedCount(limit, KubernetesConventions.MaxEventLimit, "Limit");
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(
                name,
                namespaceName,
                cancellationToken: cancellationToken);
            var selector = FormatLabelSelector(deployment.Spec?.Selector);
            V1ReplicaSet[] replicaSets = string.IsNullOrWhiteSpace(selector)
                ? []
                : (await client.AppsV1.ListNamespacedReplicaSetAsync(
                    namespaceName,
                    labelSelector: selector,
                    cancellationToken: cancellationToken)).Items
                .Take(KubernetesConventions.MaxDiagnosticsRelatedItems)
                .ToArray();
            V1Pod[] pods = string.IsNullOrWhiteSpace(selector)
                ? []
                : (await client.CoreV1.ListNamespacedPodAsync(
                    namespaceName,
                    labelSelector: selector,
                    cancellationToken: cancellationToken)).Items
                .Take(KubernetesConventions.MaxDiagnosticsRelatedItems)
                .ToArray();
            var relatedObjects = RelatedRefs(
                new RelatedObjectRef(KubernetesConventions.KubernetesResources.Deployment, name),
                replicaSets.Select(replicaSet => new RelatedObjectRef(
                    KubernetesConventions.KubernetesResources.ReplicaSet,
                    replicaSet.Metadata?.Name ?? string.Empty)),
                pods.Select(pod => new RelatedObjectRef(
                    KubernetesConventions.KubernetesResources.Pod,
                    pod.Metadata?.Name ?? string.Empty)));
            var events = await ReadRelatedEventSummariesAsync(namespaceName, relatedObjects, limit, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                kind = KubernetesConventions.KubernetesResources.Deployment,
                name,
                selector,
                deployment = DeploymentDiagnosticSummary(deployment),
                replicaSets = replicaSets.Select(ReplicaSetDiagnosticSummary),
                pods = pods.Select(PodDiagnosticSummary),
                events
            }, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment diagnostics failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Deployment diagnostics failed", ex);
        }
    }

    public async Task<string> GetPodDiagnosticsAsync(
        string namespaceName,
        string podName,
        int limit,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ??
            KubernetesManagerHelpers.ValidateName(podName) ??
            ValidateBoundedCount(limit, KubernetesConventions.MaxEventLimit, "Limit");
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var pod = await client.CoreV1.ReadNamespacedPodAsync(
                podName,
                namespaceName,
                cancellationToken: cancellationToken);
            var relatedObjects = RelatedRefs(new RelatedObjectRef(KubernetesConventions.KubernetesResources.Pod, podName));
            var events = await ReadRelatedEventSummariesAsync(namespaceName, relatedObjects, limit, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                kind = KubernetesConventions.KubernetesResources.Pod,
                podName,
                pod = PodDiagnosticSummary(pod),
                events
            }, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pod diagnostics failed for {Namespace}/{PodName}", namespaceName, podName);
            return KubernetesManagerHelpers.FormatApiException("Pod diagnostics failed", ex);
        }
    }

    public async Task<string> GetServiceDiagnosticsAsync(
        string namespaceName,
        string name,
        int limit,
        CancellationToken cancellationToken)
    {
        var validation = KubernetesManagerHelpers.ValidateNamespace(options, namespaceName) ??
            KubernetesManagerHelpers.ValidateName(name) ??
            ValidateBoundedCount(limit, KubernetesConventions.MaxEventLimit, "Limit");
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var service = await client.CoreV1.ReadNamespacedServiceAsync(
                name,
                namespaceName,
                cancellationToken: cancellationToken);
            var selector = FormatMatchLabelsSelector(service.Spec?.Selector);
            V1Pod[] pods = string.IsNullOrWhiteSpace(selector)
                ? []
                : (await client.CoreV1.ListNamespacedPodAsync(
                    namespaceName,
                    labelSelector: selector,
                    cancellationToken: cancellationToken)).Items
                .Take(KubernetesConventions.MaxDiagnosticsRelatedItems)
                .ToArray();
            var relatedObjects = RelatedRefs(
                new RelatedObjectRef(KubernetesConventions.KubernetesResources.Service, name),
                pods.Select(pod => new RelatedObjectRef(
                    KubernetesConventions.KubernetesResources.Pod,
                    pod.Metadata?.Name ?? string.Empty)));
            var events = await ReadRelatedEventSummariesAsync(namespaceName, relatedObjects, limit, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                @namespace = namespaceName,
                kind = KubernetesConventions.KubernetesResources.Service,
                name,
                selector,
                service = ServiceDiagnosticSummary(service),
                pods = pods.Select(PodDiagnosticSummary),
                events
            }, KubernetesManagerHelpers.JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Service diagnostics failed for {Namespace}/{Name}", namespaceName, name);
            return KubernetesManagerHelpers.FormatApiException("Service diagnostics failed", ex);
        }
    }

    private async Task<object[]> ReadRelatedEventSummariesAsync(
        string namespaceName,
        IReadOnlySet<RelatedObjectRef> relatedObjects,
        int limit,
        CancellationToken cancellationToken)
    {
        var events = await client.EventsV1.ListNamespacedEventAsync(
            namespaceName,
            limit: KubernetesConventions.MaxEventLimit,
            cancellationToken: cancellationToken);

        return events.Items
            .Where(k8sEvent => k8sEvent.Regarding is not null &&
                relatedObjects.Contains(new RelatedObjectRef(
                    k8sEvent.Regarding.Kind ?? string.Empty,
                    k8sEvent.Regarding.Name ?? string.Empty)))
            .Take(limit)
            .Select(EventSummary)
            .ToArray();
    }

    private static object DeploymentDiagnosticSummary(V1Deployment deployment) => new
    {
        name = deployment.Metadata?.Name,
        labels = deployment.Metadata?.Labels,
        generation = deployment.Metadata?.Generation,
        replicas = DeploymentReplicaSummary(deployment.Spec, deployment.Status),
        selector = DeploymentSelector(deployment.Spec),
        containers = DeploymentContainerSummaries(deployment.Spec),
        conditions = DeploymentConditionSummaries(deployment.Status)
    };

    private static object ReplicaSetDiagnosticSummary(V1ReplicaSet replicaSet) => new
    {
        name = replicaSet.Metadata?.Name,
        labels = replicaSet.Metadata?.Labels,
        generation = replicaSet.Metadata?.Generation,
        replicas = ReplicaSetReplicaSummary(replicaSet.Spec, replicaSet.Status),
        conditions = ReplicaSetConditionSummaries(replicaSet.Status)
    };

    private static object PodDiagnosticSummary(V1Pod pod)
    {
        var status = PodStatusFields.From(pod.Status);
        return new
        {
            name = pod.Metadata?.Name,
            labels = pod.Metadata?.Labels,
            phase = status.Phase,
            reason = status.Reason,
            message = status.Message,
            podIp = status.PodIp,
            hostIp = status.HostIp,
            nodeName = pod.Spec?.NodeName,
            conditions = PodConditionSummaries(pod.Status),
            containers = PodDiagnosticContainerStatusSummaries(pod.Status)
        };
    }

    private static object ServiceDiagnosticSummary(V1Service service) => new
    {
        name = service.Metadata?.Name,
        labels = service.Metadata?.Labels,
        type = service.Spec?.Type,
        clusterIp = service.Spec?.ClusterIP,
        selector = service.Spec?.Selector,
        ports = ServicePortSummaries(service.Spec)
    };

    private static HashSet<RelatedObjectRef> RelatedRefs(
        RelatedObjectRef first,
        IEnumerable<RelatedObjectRef>? second = null,
        IEnumerable<RelatedObjectRef>? third = null)
    {
        var refs = new HashSet<RelatedObjectRef>();
        Add(first);

        foreach (var obj in second ?? [])
        {
            Add(obj);
        }

        foreach (var obj in third ?? [])
        {
            Add(obj);
        }

        return refs;

        void Add(RelatedObjectRef obj)
        {
            if (!string.IsNullOrWhiteSpace(obj.Kind) && !string.IsNullOrWhiteSpace(obj.Name))
            {
                refs.Add(obj);
            }
        }
    }

    private static string? FormatLabelSelector(V1LabelSelector? selector)
    {
        if (selector is null)
        {
            return null;
        }

        var terms = new List<string>();
        terms.AddRange(FormatMatchLabelTerms(selector.MatchLabels));

        foreach (var expression in selector.MatchExpressions ?? [])
        {
            var term = FormatLabelSelectorExpression(expression);
            if (!string.IsNullOrWhiteSpace(term))
            {
                terms.Add(term);
            }
        }

        return terms.Count == 0 ? null : string.Join(",", terms);
    }

    private static string? FormatMatchLabelsSelector(IDictionary<string, string>? labels)
    {
        var terms = FormatMatchLabelTerms(labels).ToArray();

        return terms.Length == 0 ? null : string.Join(",", terms);
    }

    private static IEnumerable<string> FormatMatchLabelTerms(IDictionary<string, string>? labels)
    {
        return labels?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}") ?? [];
    }

    private static string? FormatLabelSelectorExpression(V1LabelSelectorRequirement expression)
    {
        if (string.IsNullOrWhiteSpace(expression.Key))
        {
            return null;
        }

        var values = expression.Values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];

        return expression.OperatorProperty switch
        {
            KubernetesConventions.LabelSelectorOperators.In =>
                FormatSetBasedLabelSelectorExpression(expression.Key, "in", values),
            KubernetesConventions.LabelSelectorOperators.NotIn =>
                FormatSetBasedLabelSelectorExpression(expression.Key, "notin", values),
            KubernetesConventions.LabelSelectorOperators.Exists => expression.Key,
            KubernetesConventions.LabelSelectorOperators.DoesNotExist => $"!{expression.Key}",
            _ => null
        };
    }

    private static string? FormatSetBasedLabelSelectorExpression(string key, string operatorText, string[] values)
    {
        return values.Length == 0
            ? null
            : $"{key} {operatorText} ({string.Join(",", values)})";
    }

    private sealed record RelatedObjectRef(string Kind, string Name);
}
