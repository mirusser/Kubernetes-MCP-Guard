using InfraGate.McpServer;
using InfraGate.McpServer.Policy;

namespace InfraGate.McpServer.Tests.UnitTests;

public sealed class K8sPolicyValidatorTests
{
    [Fact]
    public void Validate_PrivilegedContainer_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: priv
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: priv
                         template:
                           metadata:
                             labels:
                               app: priv
                           spec:
                             containers:
                               - name: app
                                 image: nginx:1.27
                                 securityContext:
                                   privileged: true
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentPrivilegedContainer);
    }

    [Fact]
    public void Validate_NonPrivilegedContainer_IsAllowed()
    {
        var result = Validate(SafeDeploymentManifest);

        Assert.DoesNotContain(result.Findings, f => f.Code == K8sConventions.PolicyCodes.DeploymentPrivilegedContainer);
    }

    [Fact]
    public void Validate_DeploymentWithoutPodSpec_HasNoFindings()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: no-pod-spec
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: no-pod-spec
                         template:
                           metadata:
                             labels:
                               app: no-pod-spec
                       """;

        var result = Validate(manifest);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_HostPathVolume_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: hp
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: hp
                         template:
                           metadata:
                             labels:
                               app: hp
                           spec:
                             containers:
                               - name: app
                                 image: nginx:1.27
                             volumes:
                               - name: host-vol
                                 hostPath:
                                   path: /etc
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentHostPath);
    }

    [Fact]
    public void Validate_EmptyDirVolume_IsAllowed()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: ed
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: ed
                         template:
                           metadata:
                             labels:
                               app: ed
                           spec:
                             containers:
                               - name: app
                                 image: nginx:1.27
                             volumes:
                               - name: tmp
                                 emptyDir: {}
                       """;

        var result = Validate(manifest);

        Assert.DoesNotContain(result.Findings, f => f.Code == K8sConventions.PolicyCodes.DeploymentHostPath);
    }

    [Fact]
    public void Validate_HostNetwork_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: hn
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: hn
                         template:
                           metadata:
                             labels:
                               app: hn
                           spec:
                             hostNetwork: true
                             containers:
                               - name: app
                                 image: nginx:1.27
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentHostNamespace);
    }

    [Fact]
    public void Validate_HostPid_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: hpid
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: hpid
                         template:
                           metadata:
                             labels:
                               app: hpid
                           spec:
                             hostPID: true
                             containers:
                               - name: app
                                 image: nginx:1.27
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentHostNamespace);
    }

    [Fact]
    public void Validate_HostIpc_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: hipc
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: hipc
                         template:
                           metadata:
                             labels:
                               app: hipc
                           spec:
                             hostIPC: true
                             containers:
                               - name: app
                                 image: nginx:1.27
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentHostNamespace);
    }

    [Fact]
    public void Validate_AddedCapabilities_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: caps
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: caps
                         template:
                           metadata:
                             labels:
                               app: caps
                           spec:
                             containers:
                               - name: app
                                 image: nginx:1.27
                                 securityContext:
                                   capabilities:
                                     add:
                                       - SYS_ADMIN
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentAddedCapabilities);
    }

    [Fact]
    public void Validate_DroppedCapabilitiesOnly_IsAllowed()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: dropped
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: dropped
                         template:
                           metadata:
                             labels:
                               app: dropped
                           spec:
                             containers:
                               - name: app
                                 image: nginx:1.27
                                 securityContext:
                                   capabilities:
                                     drop:
                                       - ALL
                       """;

        var result = Validate(manifest);

        Assert.DoesNotContain(result.Findings, f => f.Code == K8sConventions.PolicyCodes.DeploymentAddedCapabilities);
    }

    [Fact]
    public void Validate_InitContainerPrivileged_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: init-priv
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: init-priv
                         template:
                           metadata:
                             labels:
                               app: init-priv
                           spec:
                             initContainers:
                               - name: init
                                 image: busybox:1.36
                                 securityContext:
                                   privileged: true
                             containers:
                               - name: app
                                 image: nginx:1.27
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.DeploymentPrivilegedContainer);
    }

    [Fact]
    public void Validate_LatestImageTag_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: latest
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: latest
                         template:
                           metadata:
                             labels:
                               app: latest
                           spec:
                             containers:
                               - name: app
                                 image: nginx:latest
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.ImageLatestTag);
    }

    [Fact]
    public void Validate_ImplicitLatestImage_IsDenied()
    {
        var manifest = """
                       apiVersion: apps/v1
                       kind: Deployment
                       metadata:
                         name: implicit
                         namespace: demo
                       spec:
                         selector:
                           matchLabels:
                             app: implicit
                         template:
                           metadata:
                             labels:
                               app: implicit
                           spec:
                             containers:
                               - name: app
                                 image: nginx
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.ImageLatestTag);
    }

    [Fact]
    public void Validate_PinnedImageTag_IsAllowed()
    {
        var result = Validate(SafeDeploymentManifest);

        Assert.DoesNotContain(result.Findings, f => f.Code == K8sConventions.PolicyCodes.ImageLatestTag);
    }

    [Fact]
    public void Validate_LoadBalancerService_IsDenied()
    {
        var manifest = """
                       apiVersion: v1
                       kind: Service
                       metadata:
                         name: lb
                         namespace: demo
                       spec:
                         type: LoadBalancer
                         selector:
                           app: demo
                         ports:
                           - port: 80
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.ServiceLoadBalancer);
    }

    [Fact]
    public void Validate_NodePortService_IsDenied()
    {
        var manifest = """
                       apiVersion: v1
                       kind: Service
                       metadata:
                         name: np
                         namespace: demo
                       spec:
                         type: NodePort
                         selector:
                           app: demo
                         ports:
                           - port: 80
                             nodePort: 30080
                       """;

        var result = Validate(manifest);

        Assert.True(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Deny &&
            f.Code == K8sConventions.PolicyCodes.ServiceNodePort);
    }

    [Fact]
    public void Validate_ClusterIPService_IsAllowed()
    {
        var manifest = """
                       apiVersion: v1
                       kind: Service
                       metadata:
                         name: svc
                         namespace: demo
                       spec:
                         type: ClusterIP
                         selector:
                           app: demo
                         ports:
                           - port: 80
                       """;

        var result = Validate(manifest);

        Assert.False(result.IsDenied);
        Assert.DoesNotContain(result.Findings, f =>
            f.Code == K8sConventions.PolicyCodes.ServiceLoadBalancer ||
            f.Code == K8sConventions.PolicyCodes.ServiceNodePort);
    }

    [Fact]
    public void Validate_OmittedServiceType_IsAllowed()
    {
        var manifest = """
                       apiVersion: v1
                       kind: Service
                       metadata:
                         name: svc
                         namespace: demo
                       spec:
                         selector:
                           app: demo
                         ports:
                           - port: 80
                       """;

        var result = Validate(manifest);

        Assert.False(result.IsDenied);
        Assert.DoesNotContain(result.Findings, f =>
            f.Code == K8sConventions.PolicyCodes.ServiceLoadBalancer ||
            f.Code == K8sConventions.PolicyCodes.ServiceNodePort);
    }

    [Fact]
    public void Validate_ConfigMapSecretLikeKey_IsWarned()
    {
        var manifest = """
                       apiVersion: v1
                       kind: ConfigMap
                       metadata:
                         name: cfg
                         namespace: demo
                       data:
                         password: hunter2
                       """;

        var result = Validate(manifest);

        Assert.False(result.IsDenied);
        Assert.Contains(result.Findings, f =>
            f.Severity == K8sPolicySeverity.Warning &&
            f.Code == K8sConventions.PolicyCodes.ConfigMapSecretLikeKey);
    }

    [Fact]
    public void Validate_ConfigMapWithoutData_HasNoFindings()
    {
        var manifest = """
                       apiVersion: v1
                       kind: ConfigMap
                       metadata:
                         name: cfg
                         namespace: demo
                       """;

        var result = Validate(manifest);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Validate_SafeMinimalDeployment_HasNoFindings()
    {
        var result = Validate(SafeDeploymentManifest);

        Assert.Empty(result.Findings);
    }

    private static K8sPolicyResult Validate(string manifest)
    {
        var parsed = K8sManifestParser.ParseSupported(manifest, "demo");
        return K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
    }

    private const string SafeDeploymentManifest = """
                                                   apiVersion: apps/v1
                                                   kind: Deployment
                                                   metadata:
                                                     name: safe
                                                     namespace: demo
                                                   spec:
                                                     selector:
                                                       matchLabels:
                                                         app: safe
                                                     template:
                                                       metadata:
                                                         labels:
                                                           app: safe
                                                       spec:
                                                         containers:
                                                           - name: app
                                                             image: nginx:1.27-alpine
                                                   """;
}
