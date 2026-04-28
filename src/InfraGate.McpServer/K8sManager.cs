using System.Net;
using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed class K8sManager
{
    private const int MaxReplicas = 5;
    private const string FieldManager = "infra-gate-mcp";
    private const string RestartedAtAnnotation = "kubectl.kubernetes.io/restartedAt";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly K8sMcpOptions _options;
    private readonly ApprovalStore _approvalStore;
    private readonly IKubernetes _client;

    public K8sManager(K8sMcpOptions options, ApprovalStore approvalStore, IKubernetes client)
    {
        _options = options;
        _approvalStore = approvalStore;
        _client = client;
    }

    public async Task<string> GetStatusAsync(string namespaceName, string? labelSelector, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        labelSelector = string.IsNullOrWhiteSpace(labelSelector) ? null : labelSelector;

        try
        {
            var deployments = await _client.AppsV1.ListNamespacedDeploymentAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
            var services = await _client.CoreV1.ListNamespacedServiceAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
            var configMaps = await _client.CoreV1.ListNamespacedConfigMapAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
            var pods = await _client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
            var replicaSets = await _client.AppsV1.ListNamespacedReplicaSetAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

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
            }, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FormatApiException("Status read failed", ex);
        }
    }

    public Task<string> RequestApplyManifestAsync(string namespaceName, string manifest, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return Task.FromResult(ex.Message);
        }

        var plan = CreatePlan(
            operation: "apply",
            namespaceName,
            description: $"Apply {parsed.ObjectRefs.Length} supported Kubernetes object(s) in namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                ["objectCount"] = parsed.ObjectRefs.Length.ToString()
            },
            objects: parsed.ObjectRefs,
            manifest);

        return CreateAndFormatPlanAsync(plan, cancellationToken);
    }

    public Task<string> RequestDeleteManifestAsync(string namespaceName, string manifest, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return Task.FromResult(ex.Message);
        }

        var plan = CreatePlan(
            operation: "delete",
            namespaceName,
            description: $"Delete {parsed.ObjectRefs.Length} supported Kubernetes object(s) from namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                ["objectCount"] = parsed.ObjectRefs.Length.ToString()
            },
            objects: parsed.ObjectRefs,
            manifest);

        return CreateAndFormatPlanAsync(plan, cancellationToken);
    }

    public Task<string> RequestScaleDeploymentAsync(string namespaceName, string name, int replicas, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name) ?? ValidateReplicas(replicas);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var plan = CreatePlan(
            operation: "scale",
            namespaceName,
            description: $"Scale Deployment '{name}' in namespace '{namespaceName}' to {replicas} replicas.",
            parameters: new Dictionary<string, string>
            {
                ["name"] = name,
                ["replicas"] = replicas.ToString()
            },
            objects: [new K8sObjectRef("apps/v1", "Deployment", namespaceName, name)],
            manifest: null);

        return CreateAndFormatPlanAsync(plan, cancellationToken);
    }

    public Task<string> RequestRestartDeploymentAsync(string namespaceName, string name, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var plan = CreatePlan(
            operation: "restart",
            namespaceName,
            description: $"Restart Deployment '{name}' in namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                ["name"] = name,
                ["restartedAtUtc"] = restartedAtUtc
            },
            objects: [new K8sObjectRef("apps/v1", "Deployment", namespaceName, name)],
            manifest: null);

        return CreateAndFormatPlanAsync(plan, cancellationToken);
    }

    public async Task<string> ApplyApprovedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        var approved = await _approvalStore.GetApprovedPlanAsync(planId, cancellationToken);
        if (!approved.IsApproved || approved.Plan is null || approved.Hash is null)
        {
            await _approvalStore.WriteAuditAsync("apply_denied", new
            {
                planId,
                approved.Message
            }, cancellationToken);

            return $"Refused: {approved.Message}";
        }

        var applyResult = await ApplyPlanAsync(approved.Plan, cancellationToken);
        if (!applyResult.Succeeded)
        {
            await _approvalStore.WriteAuditAsync("apply_failed", new
            {
                approved.Plan.Id,
                approved.Plan.Operation,
                applyResult.Message
            }, cancellationToken);

            return applyResult.Message;
        }

        await _approvalStore.MarkAppliedAsync(approved.Plan, approved.Hash, cancellationToken);

        var rollout = approved.Plan.Operation == "delete"
            ? "No rollout wait for delete operations."
            : await WaitForDeploymentsAsync(approved.Plan.Namespace, DeploymentNames(approved.Plan), cancellationToken);
        var status = await GetStatusAsync(approved.Plan.Namespace, labelSelector: null, cancellationToken);

        return $"""
               Applied plan: {approved.Plan.Id}
               Operation: {approved.Plan.Operation}

               API operations:
               {applyResult.Message}

               Rollout:
               {rollout}

               Current status:
               {status}
               """;
    }

    private async Task<ApplyResult> ApplyPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            return plan.Operation switch
            {
                "apply" => await ApplyManifestPlanAsync(plan, cancellationToken),
                "delete" => await DeleteManifestPlanAsync(plan, cancellationToken),
                "scale" => await ScaleDeploymentPlanAsync(plan, cancellationToken),
                "restart" => await RestartDeploymentPlanAsync(plan, cancellationToken),
                _ => ApplyResult.Failed($"Unsupported plan operation '{plan.Operation}'.")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApplyResult.Failed(FormatApiException("API operation failed", ex));
        }
    }

    private async Task<ApplyResult> ApplyManifestPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plan.Manifest))
        {
            return ApplyResult.Failed("Apply plan is missing a manifest.");
        }

        var parsed = K8sManifestParser.ParseSupported(plan.Manifest, plan.Namespace);
        if (!SameObjects(parsed.ObjectRefs, plan.Objects))
        {
            return ApplyResult.Failed("Apply plan manifest no longer matches the planned object references.");
        }

        var messages = new List<string>();
        foreach (var obj in parsed.Objects)
        {
            await ApplyObjectAsync(obj, cancellationToken);
            messages.Add($"Applied {obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}");
        }

        return ApplyResult.Success(string.Join(Environment.NewLine, messages));
    }

    private async Task<ApplyResult> DeleteManifestPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        foreach (var obj in plan.Objects)
        {
            messages.Add(await DeleteObjectAsync(obj, cancellationToken));
        }

        return ApplyResult.Success(string.Join(Environment.NewLine, messages));
    }

    private async Task<ApplyResult> ScaleDeploymentPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var name = plan.Parameters["name"];
        var replicas = int.Parse(plan.Parameters["replicas"]);
        var patch = new
        {
            spec = new
            {
                replicas
            }
        };

        await _client.AppsV1.PatchNamespacedDeploymentScaleAsync(
            new V1Patch(patch, V1Patch.PatchType.MergePatch),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success($"Scaled apps/v1 Deployment {plan.Namespace}/{name} to {replicas} replicas.");
    }

    private async Task<ApplyResult> RestartDeploymentPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var name = plan.Parameters["name"];
        var restartedAtUtc = plan.Parameters["restartedAtUtc"];
        var patch = new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            [RestartedAtAnnotation] = restartedAtUtc
                        }
                    }
                }
            }
        };

        await _client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch),
            name,
            plan.Namespace,
            fieldManager: FieldManager,
            cancellationToken: cancellationToken);

        return ApplyResult.Success($"Restarted apps/v1 Deployment {plan.Namespace}/{name} at {restartedAtUtc}.");
    }

    private async Task ApplyObjectAsync(IKubernetesObject<V1ObjectMeta> obj, CancellationToken cancellationToken)
    {
        switch (obj)
        {
            case V1Deployment deployment:
                await _client.AppsV1.PatchNamespacedDeploymentAsync(
                    new V1Patch(deployment, V1Patch.PatchType.ApplyPatch),
                    deployment.Metadata.Name,
                    deployment.Metadata.NamespaceProperty,
                    fieldManager: FieldManager,
                    force: true,
                    cancellationToken: cancellationToken);
                break;
            case V1Service service:
                await _client.CoreV1.PatchNamespacedServiceAsync(
                    new V1Patch(service, V1Patch.PatchType.ApplyPatch),
                    service.Metadata.Name,
                    service.Metadata.NamespaceProperty,
                    fieldManager: FieldManager,
                    force: true,
                    cancellationToken: cancellationToken);
                break;
            case V1ConfigMap configMap:
                await _client.CoreV1.PatchNamespacedConfigMapAsync(
                    new V1Patch(configMap, V1Patch.PatchType.ApplyPatch),
                    configMap.Metadata.Name,
                    configMap.Metadata.NamespaceProperty,
                    fieldManager: FieldManager,
                    force: true,
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task<string> DeleteObjectAsync(K8sObjectRef obj, CancellationToken cancellationToken)
    {
        try
        {
            switch (obj.ApiVersion, obj.Kind)
            {
                case ("apps/v1", "Deployment"):
                    await _client.AppsV1.DeleteNamespacedDeploymentAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case ("v1", "Service"):
                    await _client.CoreV1.DeleteNamespacedServiceAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                case ("v1", "ConfigMap"):
                    await _client.CoreV1.DeleteNamespacedConfigMapAsync(
                        obj.Name,
                        obj.Namespace,
                        cancellationToken: cancellationToken);
                    break;
                default:
                    return $"Skipped unsupported {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}.";
            }

            return $"Deleted {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return $"Skipped missing {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}";
        }
    }

    private async Task<string> WaitForDeploymentsAsync(string namespaceName, IEnumerable<string> deploymentNames, CancellationToken cancellationToken)
    {
        var names = deploymentNames.Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length == 0)
        {
            return "No Deployments to wait for.";
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var pending = new List<string>();
            foreach (var name in names)
            {
                var deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(
                    name,
                    namespaceName,
                    cancellationToken: cancellationToken);
                var desired = deployment.Spec?.Replicas ?? 0;
                var updated = deployment.Status?.UpdatedReplicas ?? 0;
                var ready = deployment.Status?.ReadyReplicas ?? 0;
                var available = deployment.Status?.AvailableReplicas ?? 0;
                var observedGeneration = deployment.Status?.ObservedGeneration ?? 0;
                var generation = deployment.Metadata?.Generation ?? 0;
                var observed = observedGeneration >= generation;

                if (!observed || updated != desired || ready != desired || available != desired)
                {
                    pending.Add($"{name}: desired={desired}, updated={updated}, ready={ready}, available={available}");
                }
            }

            if (pending.Count == 0)
            {
                return $"Deployment rollout completed for {string.Join(", ", names)}.";
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return $"Timed out waiting for Deployment rollout: {string.Join(", ", names)}.";
    }

    private async Task<string> CreateAndFormatPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var result = await _approvalStore.CreatePlanAsync(plan, cancellationToken);
        var objects = string.Join(
            Environment.NewLine,
            result.Plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));
        var manifestBlock = string.IsNullOrWhiteSpace(result.Plan.Manifest)
            ? string.Empty
            : $"{Environment.NewLine}Manifest:{Environment.NewLine}```yaml{Environment.NewLine}{result.Plan.Manifest}```{Environment.NewLine}";

        return $"""
               PlanId: {result.Plan.Id}
               Status: pending human approval
               Operation: {result.Plan.Operation}
               Namespace: {result.Plan.Namespace}
               Objects:
               {objects}
               Pending file: {result.PendingPath}
               Approval file: {result.ApprovedPath}
               Plan hash: {result.Hash}

               Human approval:
                 ./scripts/approve-plan.sh {result.Plan.Id}

               After approval, call apply_approved_plan with planId '{result.Plan.Id}'.
               {manifestBlock}
               """;
    }

    private K8sPlan CreatePlan(
        string operation,
        string namespaceName,
        string description,
        Dictionary<string, string> parameters,
        K8sObjectRef[] objects,
        string? manifest)
    {
        return new K8sPlan(
            ApprovalStore.NewPlanId(),
            operation,
            namespaceName,
            DateTimeOffset.UtcNow,
            description,
            parameters,
            objects,
            manifest);
    }

    private string? ValidateNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return "Namespace is required.";
        }

        if (!_options.IsNamespaceAllowed(namespaceName))
        {
            return $"Namespace '{namespaceName}' is not allowed. Allowed namespaces: {string.Join(", ", _options.AllowedNamespaces.Order(StringComparer.Ordinal))}.";
        }

        return null;
    }

    private static string? ValidateName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "Resource name is required."
            : null;
    }

    private static string? ValidateReplicas(int replicas)
    {
        return replicas is < 0 or > MaxReplicas
            ? $"Replicas must be between 0 and {MaxReplicas}."
            : null;
    }

    private static IEnumerable<string> DeploymentNames(K8sPlan plan)
    {
        return plan.Objects
            .Where(obj => obj is { ApiVersion: "apps/v1", Kind: "Deployment" })
            .Select(obj => obj.Name);
    }

    private static bool SameObjects(K8sObjectRef[] left, K8sObjectRef[] right)
    {
        static string Key(K8sObjectRef obj) => $"{obj.ApiVersion}/{obj.Kind}/{obj.Namespace}/{obj.Name}";

        return left.Select(Key).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool IsNotFound(Exception ex)
    {
        return ex is KubernetesException { Status.Code: 404 } ||
               ex is HttpOperationException { Response.StatusCode: HttpStatusCode.NotFound };
    }

    private static string FormatApiException(string prefix, Exception ex)
    {
        return ex switch
        {
            KubernetesException kube when kube.Status is not null =>
                $"{prefix}: Kubernetes API returned {kube.Status.Code} {kube.Status.Reason}: {kube.Status.Message}",
            HttpOperationException http when http.Response is not null =>
                $"{prefix}: Kubernetes API returned {(int)http.Response.StatusCode} {http.Response.ReasonPhrase}: {http.Message}",
            _ => $"{prefix}: {ex.Message}"
        };
    }

    private sealed record ApplyResult(bool Succeeded, string Message)
    {
        public static ApplyResult Success(string message) => new(true, message);

        public static ApplyResult Failed(string message) => new(false, message);
    }
}
