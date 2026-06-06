using System.Text.Json;
using InfraGate.KubernetesAdapter.Execution;
using InfraGate.KubernetesAdapter.Evidence;
using InfraGate.KubernetesAdapter.PlanBuilding;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class OperationDispatchMapTests
{
    [Fact]
    public void TryGetValue_Apply_ReturnsManifestDispatch()
    {
        var found = OperationDispatchMap.TryGetValue(KubernetesAdapterConventions.PlanOperations.Apply, out var dispatch);

        Assert.True(found);
        Assert.NotNull(dispatch);
        Assert.Equal(KubernetesAdapterConventions.MutationTools.ApplyManifest, dispatch.MutationTool);
        Assert.Equal(KubernetesAdapterConventions.EvidenceTools.DryRunApplyManifest, dispatch.DryRunTool);

        var args = dispatch.ArgsBuilder(CreateManifestPayload());

        Assert.Equal(ManifestNamespace, args[KubernetesAdapterConventions.EvidenceArguments.Namespace]);
        Assert.Equal(DeploymentManifest, args[KubernetesAdapterConventions.EvidenceArguments.Manifest]);
    }

    [Fact]
    public void TryGetValue_Scale_ReturnsScaleDispatch()
    {
        var found = OperationDispatchMap.TryGetValue(KubernetesAdapterConventions.PlanOperations.Scale, out var dispatch);

        Assert.True(found);
        Assert.NotNull(dispatch);
        Assert.Equal(KubernetesAdapterConventions.MutationTools.ScaleDeployment, dispatch.MutationTool);
        Assert.Equal(KubernetesAdapterConventions.EvidenceTools.DryRunScaleDeployment, dispatch.DryRunTool);
    }

    [Fact]
    public void TryGetValue_Restart_ReturnsRestartDispatch()
    {
        var found = OperationDispatchMap.TryGetValue(KubernetesAdapterConventions.PlanOperations.Restart, out var dispatch);

        Assert.True(found);
        Assert.NotNull(dispatch);
        Assert.Equal(KubernetesAdapterConventions.MutationTools.RestartDeployment, dispatch.MutationTool);
        Assert.Equal(KubernetesAdapterConventions.EvidenceTools.DryRunRestartDeployment, dispatch.DryRunTool);
    }

    [Fact]
    public void TryGetValue_SetImage_ReturnsSetImageDispatch()
    {
        var found = OperationDispatchMap.TryGetValue(KubernetesAdapterConventions.PlanOperations.SetImage, out var dispatch);

        Assert.True(found);
        Assert.NotNull(dispatch);
        Assert.Equal(KubernetesAdapterConventions.MutationTools.SetDeploymentImage, dispatch.MutationTool);
        Assert.Equal(KubernetesAdapterConventions.EvidenceTools.DryRunSetDeploymentImage, dispatch.DryRunTool);
    }

    [Fact]
    public void TryGetValue_Delete_ReturnsDeleteDispatch()
    {
        var found = OperationDispatchMap.TryGetValue(KubernetesAdapterConventions.PlanOperations.Delete, out var dispatch);

        Assert.True(found);
        Assert.NotNull(dispatch);
        Assert.Equal(KubernetesAdapterConventions.MutationTools.DeleteManifest, dispatch.MutationTool);
        Assert.Equal(KubernetesAdapterConventions.EvidenceTools.DryRunDeleteManifest, dispatch.DryRunTool);
    }

    [Fact]
    public void TryGetValue_UnknownOperation_ReturnsFalse()
    {
        var found = OperationDispatchMap.TryGetValue("unknown", out var dispatch);

        Assert.False(found);
        Assert.Null(dispatch);
    }

    [Fact]
    public void BuildManifestArgs_ValidPayload_ReturnsManifestArgsWithResourceVersions()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.Apply);
        var payload = CreateManifestPayload() with
        {
            Diffs =
            [
                new KubernetesPlanDiff(
                    new KubernetesObjectRef("apps/v1", "Deployment", ManifestNamespace, DeploymentName),
                    "modified",
                    "deployment changed",
                    "diff",
                    null,
                    null,
                    [],
                    [],
                    [],
                    "12345"),
                new KubernetesPlanDiff(
                    new KubernetesObjectRef("v1", "Service", ManifestNamespace, "web"),
                    "modified",
                    "service changed",
                    "diff",
                    null,
                    null,
                    [],
                    [],
                    [])
            ]
        };

        var args = dispatch.ArgsBuilder(payload);

        Assert.Equal(ManifestNamespace, args[KubernetesAdapterConventions.EvidenceArguments.Namespace]);
        Assert.Equal(DeploymentManifest, args[KubernetesAdapterConventions.EvidenceArguments.Manifest]);
        var resourceVersions = Assert.IsType<string>(args[KubernetesAdapterConventions.EvidenceArguments.ResourceVersions]);
        using var document = JsonDocument.Parse(resourceVersions);
        var version = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("apps/v1 Deployment demo/web", version.GetProperty("key").GetString());
        Assert.Equal("12345", version.GetProperty("resourceVersion").GetString());
    }

    [Fact]
    public void BuildScaleArgs_ValidPayload_ReturnsScaleArgs()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.Scale);

        var args = dispatch.ArgsBuilder(CreateScalePayload());

        Assert.Equal(ManifestNamespace, args[KubernetesAdapterConventions.EvidenceArguments.Namespace]);
        Assert.Equal(DeploymentName, args[KubernetesAdapterConventions.EvidenceArguments.Name]);
        Assert.Equal("3", args[KubernetesAdapterConventions.EvidenceArguments.Replicas]);
    }

    [Fact]
    public void BuildScaleArgs_MissingName_ThrowsArgumentException()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.Scale);
        var payload = CreateScalePayload() with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.Replicas] = "3"
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => dispatch.ArgsBuilder(payload));

        Assert.Equal($"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Name}\"]", exception.ParamName);
    }

    [Fact]
    public void BuildRestartArgs_ValidPayload_ReturnsRestartArgs()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.Restart);

        var args = dispatch.ArgsBuilder(CreateNamedPayload());

        Assert.Equal(ManifestNamespace, args[KubernetesAdapterConventions.EvidenceArguments.Namespace]);
        Assert.Equal(DeploymentName, args[KubernetesAdapterConventions.EvidenceArguments.Name]);
    }

    [Fact]
    public void BuildSetImageArgs_ValidPayload_ReturnsSetImageArgs()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.SetImage);

        var args = dispatch.ArgsBuilder(CreateSetImagePayload());

        Assert.Equal(ManifestNamespace, args[KubernetesAdapterConventions.EvidenceArguments.Namespace]);
        Assert.Equal(DeploymentName, args[KubernetesAdapterConventions.EvidenceArguments.Name]);
        Assert.Equal(ContainerName, args[KubernetesAdapterConventions.EvidenceArguments.Container]);
        Assert.Equal(PinnedImage, args[KubernetesAdapterConventions.EvidenceArguments.Image]);
    }

    [Fact]
    public void BuildSetImageArgs_MissingImage_ThrowsArgumentException()
    {
        OperationDispatch dispatch = GetDispatch(KubernetesAdapterConventions.PlanOperations.SetImage);
        var payload = CreateSetImagePayload() with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesAdapterConventions.PlanParameters.Name] = DeploymentName,
                [KubernetesAdapterConventions.PlanParameters.Container] = ContainerName
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => dispatch.ArgsBuilder(payload));

        Assert.Equal($"payload.Parameters[\"{KubernetesAdapterConventions.PlanParameters.Image}\"]", exception.ParamName);
    }

    private static OperationDispatch GetDispatch(string operation)
    {
        var found = OperationDispatchMap.TryGetValue(operation, out var dispatch);

        Assert.True(found);
        return Assert.IsType<OperationDispatch>(dispatch);
    }

    private static KubernetesPlanPayload CreateManifestPayload() => new(
        ManifestNamespace,
        "Apply deployment",
        [],
        [new KubernetesObjectRef("apps/v1", "Deployment", ManifestNamespace, DeploymentName)])
    {
        Manifest = DeploymentManifest
    };

    private static KubernetesPlanPayload CreateScalePayload() => new(
        ManifestNamespace,
        "Scale deployment",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.PlanParameters.Name] = DeploymentName,
            [KubernetesAdapterConventions.PlanParameters.Replicas] = "3"
        },
        [new KubernetesObjectRef("apps/v1", "Deployment", ManifestNamespace, DeploymentName)]);

    private static KubernetesPlanPayload CreateNamedPayload() => new(
        ManifestNamespace,
        "Restart deployment",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.PlanParameters.Name] = DeploymentName
        },
        [new KubernetesObjectRef("apps/v1", "Deployment", ManifestNamespace, DeploymentName)]);

    private static KubernetesPlanPayload CreateSetImagePayload() => new(
        ManifestNamespace,
        "Set deployment image",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KubernetesAdapterConventions.PlanParameters.Name] = DeploymentName,
            [KubernetesAdapterConventions.PlanParameters.Container] = ContainerName,
            [KubernetesAdapterConventions.PlanParameters.Image] = PinnedImage
        },
        [new KubernetesObjectRef("apps/v1", "Deployment", ManifestNamespace, DeploymentName)]);

    private const string ManifestNamespace = "demo";
    private const string DeploymentName = "web";
    private const string ContainerName = "app";
    private const string PinnedImage = "nginx:1.27-alpine";
    private const string DeploymentManifest = """
                                             apiVersion: apps/v1
                                             kind: Deployment
                                             metadata:
                                               name: web
                                               namespace: demo
                                             spec:
                                               selector:
                                                 matchLabels:
                                                   app: web
                                               template:
                                                 metadata:
                                                   labels:
                                                     app: web
                                                 spec:
                                                   containers:
                                                     - name: app
                                                       image: nginx:1.27-alpine
                                             """;
}
