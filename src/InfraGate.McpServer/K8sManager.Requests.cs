using InfraGate.Approvals;
using InfraGate.McpServer.Policy;
using k8s;
using k8s.Models;

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

        var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return Task.FromResult(
                $"Manifest rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}");
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

        return CreateAndFormatPlanAsync(plan, policyResult, cancellationToken);
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

        var restartedAtUtc = DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.RoundTrip);
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

    public async Task<string> RequestSetDeploymentImageAsync(
        string namespaceName,
        string name,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ??
            ValidateName(name) ??
            ValidateRequiredText(container, "Container name") ??
            ValidateRequiredText(image, "Image");
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
            var deploymentContainer = FindDeploymentContainer(deployment, container);
            if (deploymentContainer is null)
            {
                return $"Deployment '{namespaceName}/{name}' does not contain container '{container}'.";
            }

            var plan = CreateSetDeploymentImagePlan(namespaceName, name, container, image, deploymentContainer);

            return await CreateAndFormatPlanAsync(plan, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FormatApiException("Deployment image plan failed", ex);
        }
    }

    private Task<string> CreateAndFormatPlanAsync(K8sPlan plan, CancellationToken cancellationToken) =>
        CreateAndFormatPlanAsync(plan, policyResult: null, cancellationToken);

    private async Task<string> CreateAndFormatPlanAsync(
        K8sPlan plan,
        K8sPolicyResult? policyResult,
        CancellationToken cancellationToken)
    {
        var result = await approvalStore.CreatePlanAsync(plan, cancellationToken);
        var objects = string.Join(
            Environment.NewLine,
            result.Plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));

        var warnings = policyResult is not null && policyResult.Findings.Any(f => f.Severity == K8sPolicySeverity.Warning)
            ? $"{Environment.NewLine}Policy warnings:{Environment.NewLine}{policyResult.FormatWarnings()}"
            : string.Empty;

        return $"""
               PlanId: {result.Plan.Id}
               Status: pending Gateway approval
               Operation: {result.Plan.Operation}
               Namespace: {result.Plan.Namespace}
               Objects:
               {objects}
               Pending file: {result.PendingPath}
               Plan hash: {result.Hash}

               Next step:
                 Call {K8sConventions.ToolNames.ApplyApprovedPlan} with {K8sConventions.ToolArguments.PlanId} '{result.Plan.Id}'. The Gateway will return a browser approval URL before applying it.
               {warnings}
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

    private K8sPlan CreateSetDeploymentImagePlan(
        string namespaceName,
        string name,
        string container,
        string image,
        V1Container deploymentContainer)
    {
        var currentImage = deploymentContainer.Image ?? string.Empty;
        return CreatePlan(
            operation: K8sConventions.PlanOperations.SetImage,
            namespaceName,
            description: $"Update Deployment '{name}' container '{container}' image from '{currentImage}' to '{image}'.",
            parameters: new Dictionary<string, string>
            {
                [K8sConventions.PlanParameters.Name] = name,
                [K8sConventions.PlanParameters.Container] = container,
                [K8sConventions.PlanParameters.CurrentImage] = currentImage,
                [K8sConventions.PlanParameters.Image] = image
            },
            objects: [K8sConventions.K8sResources.DeploymentRef(namespaceName, name)],
            manifest: null);
    }
}
