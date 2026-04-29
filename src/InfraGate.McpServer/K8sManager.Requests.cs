namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
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
            operation: K8sConventions.PlanOperations.Apply,
            namespaceName,
            description: $"Apply {parsed.ObjectRefs.Length} supported Kubernetes object(s) in namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.ObjectCount] = parsed.ObjectRefs.Length.ToString()
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
            operation: K8sConventions.PlanOperations.Delete,
            namespaceName,
            description: $"Delete {parsed.ObjectRefs.Length} supported Kubernetes object(s) from namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.ObjectCount] = parsed.ObjectRefs.Length.ToString()
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
            operation: K8sConventions.PlanOperations.Scale,
            namespaceName,
            description: $"Scale Deployment '{name}' in namespace '{namespaceName}' to {replicas} replicas.",
            parameters: new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.Name] = name,
                [K8sConventions.PlanParameters.Replicas] = replicas.ToString()
            },
            objects: [K8sConventions.K8sResources.DeploymentRef(namespaceName, name)],
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

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(K8sConventions.DateTimeFormats.RoundTrip);
        var plan = CreatePlan(
            operation: K8sConventions.PlanOperations.Restart,
            namespaceName,
            description: $"Restart Deployment '{name}' in namespace '{namespaceName}'.",
            parameters: new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.Name] = name,
                [K8sConventions.PlanParameters.RestartedAtUtc] = restartedAtUtc
            },
            objects: [K8sConventions.K8sResources.DeploymentRef(namespaceName, name)],
            manifest: null);

        return CreateAndFormatPlanAsync(plan, cancellationToken);
    }

    private async Task<string> CreateAndFormatPlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        var result = await approvalStore.CreatePlanAsync(plan, cancellationToken);
        var objects = string.Join(
            Environment.NewLine,
            result.Plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));
        var manifestBlock = string.IsNullOrWhiteSpace(result.Plan.Manifest)
            ? string.Empty
            : $"{Environment.NewLine}Manifest:{Environment.NewLine}```yaml{Environment.NewLine}{result.Plan.Manifest}```{Environment.NewLine}";

        return $"""
               PlanId: {result.Plan.Id}
               Status: pending MCP server approval
               Operation: {result.Plan.Operation}
               Namespace: {result.Plan.Namespace}
               Objects:
               {objects}
               Pending file: {result.PendingPath}
               Plan hash: {result.Hash}

               Next step:
                 Call {K8sConventions.ToolNames.ApplyApprovedPlan} with {K8sConventions.ToolArguments.PlanId} '{result.Plan.Id}'. The MCP server will request user approval before applying it.
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
}
