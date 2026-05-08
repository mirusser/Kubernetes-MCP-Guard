using InfraGate.Approvals;
using InfraGate.McpServer.Diff;
using InfraGate.McpServer.Policy;
using k8s;
using k8s.Models;

namespace InfraGate.McpServer;

public sealed partial class K8sManager
{
    public async Task<string> RequestApplyManifestAsync(string namespaceName, string manifest, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return ex.Message;
        }

        var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
        if (policyResult.IsDenied)
        {
            return $"Manifest rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}";
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

        return await CreateDryRunPlanAsync(
            plan,
            DryRunApplyManifestAsync(parsed.Objects, cancellationToken),
            policyResult,
            cancellationToken);
    }

    public async Task<string> RequestDeleteManifestAsync(string namespaceName, string manifest, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName);
        if (validation is not null)
        {
            return validation;
        }

        K8sParsedManifest parsed;
        try
        {
            parsed = K8sManifestParser.ParseSupported(manifest, namespaceName);
        }
        catch (K8sValidationException ex)
        {
            return ex.Message;
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

        return await CreateDryRunPlanAsync(
            plan,
            DryRunDeleteManifestAsync(plan.Objects, cancellationToken),
            cancellationToken);
    }

    public async Task<string> RequestScaleDeploymentAsync(string namespaceName, string name, int replicas, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name) ?? ValidateReplicas(replicas);
        if (validation is not null)
        {
            return validation;
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

        return await CreateDryRunPlanAsync(
            plan,
            DryRunScaleDeploymentAsync(namespaceName, name, replicas, cancellationToken),
            cancellationToken);
    }

    public async Task<string> RequestRestartDeploymentAsync(string namespaceName, string name, CancellationToken cancellationToken)
    {
        var validation = ValidateNamespace(namespaceName) ?? ValidateName(name);
        if (validation is not null)
        {
            return validation;
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

        return await CreateDryRunPlanAsync(
            plan,
            DryRunRestartDeploymentAsync(namespaceName, name, restartedAtUtc, cancellationToken),
            cancellationToken);
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

            return await CreateDryRunPlanAsync(
                plan,
                DryRunSetDeploymentImageAsync(namespaceName, name, container, image, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FormatApiException("Deployment image plan failed", ex);
        }
    }

    private Task<string> CreateAndFormatPlanAsync(K8sPlan plan, CancellationToken cancellationToken) =>
        CreateAndFormatPlanAsync(plan, policyResult: null, cancellationToken);

    private Task<string> CreateDryRunPlanAsync(
        K8sPlan plan,
        Task<DryRunResult> dryRunTask,
        CancellationToken cancellationToken) =>
        CreateDryRunPlanAsync(plan, dryRunTask, policyResult: null, cancellationToken);

    private async Task<string> CreateDryRunPlanAsync(
        K8sPlan plan,
        Task<DryRunResult> dryRunTask,
        K8sPolicyResult? policyResult,
        CancellationToken cancellationToken)
    {
        var dryRun = await dryRunTask;
        if (!dryRun.Succeeded || dryRun.DryRun is null)
        {
            await WriteDryRunFailedAuditAsync(
                K8sConventions.DryRunPhases.Request,
                plan,
                dryRun.Message,
                cancellationToken);

            return FormatRequestDryRunRefusal(dryRun.Message);
        }

        var planWithDryRun = plan with
        {
            DryRun = dryRun.DryRun,
            PolicyFindings = ToPlanPolicyFindings(policyResult)
        };

        K8sPlanDiff[] diffs;
        try
        {
            diffs = await K8sDiffService.BuildDiffsAsync(
                client,
                planWithDryRun.Operation,
                planWithDryRun.Objects,
                planWithDryRun.DryRun.Objects,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = FormatApiException("Diff generation failed", ex);
            await WriteDiffFailedAuditAsync(plan, message, cancellationToken);

            return $"Diff generation failed; no approval plan was created.{Environment.NewLine}{message}";
        }

        return await CreateAndFormatPlanAsync(planWithDryRun with { Diffs = diffs }, policyResult, cancellationToken);
    }

    private async Task<string> CreateAndFormatPlanAsync(
        K8sPlan plan,
        K8sPolicyResult? policyResult,
        CancellationToken cancellationToken)
    {
        var result = await approvalStore.CreatePlanAsync(plan, cancellationToken);
        var objects = string.Join(
            Environment.NewLine,
            result.Plan.Objects.Select(obj => $"  - {obj.ApiVersion} {obj.Kind} {obj.Namespace}/{obj.Name}"));

        return $"""
               PlanId: {result.Plan.Id}
               Status: {K8sConventions.PlanResponse.PendingGatewayApproval}
               Operation: {result.Plan.Operation}
               Namespace: {result.Plan.Namespace}
               Objects:
               {objects}
               Policy: {FormatPolicySummary(policyResult)}
               Risk: {K8sConventions.PlanResponse.RiskMedium}
               Next step: call {K8sConventions.ToolNames.ApplyApprovedPlan} with this PlanId.
               Browser approval will show the full server-rendered plan, policy findings, dry-run result, and diff.
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

    private static K8sPlanPolicyFinding[] ToPlanPolicyFindings(K8sPolicyResult? policyResult)
    {
        return policyResult?.Findings
            .Select(finding => new K8sPlanPolicyFinding(
                finding.Severity.ToString(),
                finding.Code,
                finding.ObjectRef,
                finding.Message))
            .ToArray() ?? [];
    }

    private static string FormatPolicySummary(K8sPolicyResult? policyResult)
    {
        if (policyResult is null)
        {
            return K8sConventions.PlanResponse.PolicyNotApplicable;
        }

        int warningCount = policyResult.Findings.Count(finding => finding.Severity == K8sPolicySeverity.Warning);
        if (warningCount == 0)
        {
            return K8sConventions.PlanResponse.PolicyPassed;
        }

        string suffix = warningCount == 1
            ? K8sConventions.PlanResponse.PolicyWarningSuffix
            : K8sConventions.PlanResponse.PolicyWarningsSuffix;

        return $"{K8sConventions.PlanResponse.PolicyPassedWithPrefix}{warningCount}{suffix}";
    }
}
