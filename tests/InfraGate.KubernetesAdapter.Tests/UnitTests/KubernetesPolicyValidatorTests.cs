using InfraGate.KubernetesAdapter.Policy;
using k8s;
using k8s.Models;

namespace InfraGate.KubernetesAdapter.Tests.UnitTests;

public sealed class KubernetesPolicyValidatorUnitTests
{
    [Theory]
    [InlineData("nginx")]
    [InlineData("nginx:latest")]
    [InlineData("")]
    public void ValidateSetDeploymentImage_LatestOrImplicitTag_IsDenied(string image)
    {
        var result = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            ManifestNamespace,
            DeploymentName,
            ContainerName,
            image,
            KubernetesPolicyOptions.Default);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag));
    }

    [Theory]
    [InlineData("nginx:1.27-alpine")]
    [InlineData("nginx@sha256:2f8f4f2081d7d7fdccdb8a2f9c4db03776f5b2b0d49ce83c2cb72b7bbd84d17b")]
    public void ValidateSetDeploymentImage_PinnedImage_IsAllowed(string image)
    {
        var result = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            ManifestNamespace,
            DeploymentName,
            ContainerName,
            image,
            KubernetesPolicyOptions.Default);

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("nginx:latest")]
    [InlineData("")]
    public void ValidateSetDeploymentImage_DenyLatestImageTagDisabled_IsAllowed(string image)
    {
        var options = KubernetesPolicyOptions.Default with { DenyLatestImageTag = false };

        var result = KubernetesPolicyValidator.ValidateSetDeploymentImage(
            ManifestNamespace,
            DeploymentName,
            ContainerName,
            image,
            options);

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_DeploymentWithPrivilegedContainer_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.Containers[0].SecurityContext = new V1SecurityContext { Privileged = true };

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentPrivilegedContainer));
    }

    [Fact]
    public void Validate_DeploymentWithHostNetwork_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.HostNetwork = true;

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostNamespace));
    }

    [Fact]
    public void Validate_DeploymentWithHostPid_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.HostPID = true;

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostNamespace));
    }

    [Fact]
    public void Validate_DeploymentWithHostIpc_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.HostIPC = true;

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostNamespace));
    }

    [Fact]
    public void Validate_DeploymentWithHostPathVolume_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.Volumes =
        [
            new V1Volume
            {
                Name = "host-data",
                HostPath = new V1HostPathVolumeSource { Path = "/etc" }
            }
        ];

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostPath));
    }

    [Fact]
    public void Validate_DeploymentWithAddedCapabilities_IsDenied()
    {
        var deployment = CreateDeployment(PinnedImage);
        deployment.Spec.Template.Spec.Containers[0].SecurityContext = new V1SecurityContext
        {
            Capabilities = new V1Capabilities { Add = ["SYS_ADMIN"] }
        };

        var result = Validate(deployment);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentAddedCapabilities));
    }

    [Fact]
    public void Validate_DeploymentWithLatestImage_IsDenied()
    {
        var result = Validate(CreateDeployment("nginx:latest"));

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag));
    }

    [Fact]
    public void Validate_DeploymentWithNoImageTag_IsDenied()
    {
        var result = Validate(CreateDeployment("nginx"));

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag));
    }

    [Fact]
    public void Validate_DeploymentWithPinnedImage_IsAllowed()
    {
        var result = Validate(CreateDeployment(PinnedImage));

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_ServiceWithLoadBalancerType_IsDenied()
    {
        var result = Validate(CreateService("LoadBalancer"));

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ServiceLoadBalancer));
    }

    [Fact]
    public void Validate_ServiceWithNodePortType_IsDenied()
    {
        var result = Validate(CreateService("NodePort"));

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ServiceNodePort));
    }

    [Fact]
    public void Validate_ServiceWithClusterIpType_IsAllowed()
    {
        var result = Validate(CreateService("ClusterIP"));

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("apikey")]
    [InlineData("connectionstring")]
    public void Validate_ConfigMapWithSecretLikeKey_IsWarning(string secretLikeKey)
    {
        var configMap = new V1ConfigMap
        {
            Metadata = CreateMetadata("settings"),
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [secretLikeKey] = "value"
            }
        };

        var result = Validate(configMap);

        Assert.False(result.IsDenied);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(KubernetesPolicySeverity.Warning, finding.Severity);
        Assert.Equal(KubernetesAdapterConventions.PolicyCodes.ConfigMapSecretLikeKey, finding.Code);
    }

    [Fact]
    public void Validate_CleanDeploymentManifest_HasNoFindings()
    {
        var result = Validate(CleanDeploymentManifest);

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_DisabledPolicyOptions_HasNoFindings()
    {
        var result = KubernetesPolicyValidator.Validate(
            ParseObjects(DangerousMixedManifest),
            DisabledPolicyOptions);

        Assert.False(result.IsDenied);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_MultipleMixedObjects_AggregatesFindings()
    {
        var result = Validate(DangerousMixedManifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentPrivilegedContainer));
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostNamespace));
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentHostPath));
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.DeploymentAddedCapabilities));
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ImageLatestTag));
        Assert.Contains(result.Findings, HasDenyCode(KubernetesAdapterConventions.PolicyCodes.ServiceLoadBalancer));
        Assert.Contains(result.Findings, f =>
            f.Severity == KubernetesPolicySeverity.Warning &&
            f.Code == KubernetesAdapterConventions.PolicyCodes.ConfigMapSecretLikeKey);
    }

    private static V1Deployment CreateDeployment(string image) => new()
    {
        ApiVersion = KubernetesAdapterConventions.ApiVersions.AppsV1,
        Kind = KubernetesAdapterConventions.ResourceKinds.Deployment,
        Metadata = CreateMetadata(DeploymentName),
        Spec = new V1DeploymentSpec
        {
            Selector = new V1LabelSelector { MatchLabels = AppLabels },
            Template = new V1PodTemplateSpec
            {
                Metadata = new V1ObjectMeta { Labels = AppLabels },
                Spec = new V1PodSpec
                {
                    Containers =
                    [
                        new V1Container
                        {
                            Name = ContainerName,
                            Image = image
                        }
                    ]
                }
            }
        }
    };

    private static V1Service CreateService(string type) => new()
    {
        ApiVersion = KubernetesAdapterConventions.ApiVersions.V1,
        Kind = KubernetesAdapterConventions.ResourceKinds.Service,
        Metadata = CreateMetadata("web"),
        Spec = new V1ServiceSpec
        {
            Type = type,
            Selector = AppLabels,
            Ports = [new V1ServicePort { Port = 80 }]
        }
    };

    private static V1ObjectMeta CreateMetadata(string name) => new()
    {
        Name = name,
        NamespaceProperty = ManifestNamespace
    };

    private static KubernetesPolicyResult Validate(IKubernetesObject<V1ObjectMeta> obj) =>
        KubernetesPolicyValidator.Validate([obj], KubernetesPolicyOptions.Default);

    private static KubernetesPolicyResult Validate(string manifest) =>
        KubernetesPolicyValidator.Validate(ParseObjects(manifest), KubernetesPolicyOptions.Default);

    private static IReadOnlyList<IKubernetesObject<V1ObjectMeta>> ParseObjects(string manifest) =>
        KubernetesYaml.LoadAllFromString(manifest, YamlTypeMap)
            .OfType<IKubernetesObject<V1ObjectMeta>>()
            .ToList();

    private static Predicate<KubernetesPolicyFinding> HasDenyCode(string code) => finding =>
        finding.Severity == KubernetesPolicySeverity.Deny &&
        finding.Code == code;

    private static readonly Dictionary<string, string> AppLabels = new(StringComparer.Ordinal)
    {
        ["app"] = "web"
    };

    private static readonly Dictionary<string, Type> YamlTypeMap = new(StringComparer.Ordinal)
    {
        ["apps/v1/Deployment"] = typeof(V1Deployment),
        ["v1/Service"] = typeof(V1Service),
        ["v1/ConfigMap"] = typeof(V1ConfigMap)
    };

    private static readonly KubernetesPolicyOptions DisabledPolicyOptions = new()
    {
        DenyPrivilegedContainers = false,
        DenyHostPathVolumes = false,
        DenyHostNetwork = false,
        DenyHostPid = false,
        DenyHostIpc = false,
        DenyAddedCapabilities = false,
        DenyLatestImageTag = false,
        DenyServiceTypeNodePort = false,
        DenyServiceTypeLoadBalancer = false,
        WarnOnConfigMapSecretLikeKeys = false
    };

    private const string ManifestNamespace = "demo";
    private const string DeploymentName = "web";
    private const string ContainerName = "app";
    private const string PinnedImage = "nginx:1.27-alpine";
    private const string CleanDeploymentManifest = """
                                                   apiVersion: apps/v1
                                                   kind: Deployment
                                                   metadata:
                                                     name: clean
                                                     namespace: demo
                                                   spec:
                                                     selector:
                                                       matchLabels:
                                                         app: clean
                                                     template:
                                                       metadata:
                                                         labels:
                                                           app: clean
                                                       spec:
                                                         containers:
                                                           - name: app
                                                             image: nginx:1.27-alpine
                                                   """;

    private const string DangerousMixedManifest = """
                                                     apiVersion: apps/v1
                                                     kind: Deployment
                                                     metadata:
                                                       name: dangerous
                                                       namespace: demo
                                                     spec:
                                                       selector:
                                                         matchLabels:
                                                           app: dangerous
                                                       template:
                                                         metadata:
                                                           labels:
                                                             app: dangerous
                                                         spec:
                                                           hostNetwork: true
                                                           containers:
                                                             - name: app
                                                               image: nginx:latest
                                                               securityContext:
                                                                 privileged: true
                                                                 capabilities:
                                                                   add:
                                                                     - SYS_ADMIN
                                                           volumes:
                                                             - name: host-data
                                                               hostPath:
                                                                 path: /etc
                                                     ---
                                                     apiVersion: v1
                                                     kind: Service
                                                     metadata:
                                                       name: web
                                                       namespace: demo
                                                     spec:
                                                       type: LoadBalancer
                                                       selector:
                                                         app: dangerous
                                                       ports:
                                                         - port: 80
                                                     ---
                                                     apiVersion: v1
                                                     kind: ConfigMap
                                                     metadata:
                                                       name: settings
                                                       namespace: demo
                                                     data:
                                                       password: hunter2
                                                     """;
}
