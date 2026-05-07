using k8s;
using k8s.Models;

namespace InfraGate.McpServer.Policy;

internal static class K8sPolicyValidator
{
    private static readonly string[] SecretLikeKeys =
    [
        "password", "passwd", "pwd", "secret", "token",
        "apikey", "api_key", "private_key", "connectionstring", "connection_string"
    ];

    public static K8sPolicyResult Validate(
        IReadOnlyList<IKubernetesObject<V1ObjectMeta>> objects,
        K8sPolicyOptions options)
    {
        var findings = new List<K8sPolicyFinding>();

        foreach (var obj in objects)
        {
            switch (obj)
            {
                case V1Deployment deployment:
                    ValidateDeployment(deployment, options, findings);
                    break;
                case V1Service service:
                    ValidateService(service, options, findings);
                    break;
                case V1ConfigMap configMap:
                    ValidateConfigMap(configMap, options, findings);
                    break;
            }
        }

        return new K8sPolicyResult(findings);
    }

    private static void ValidateDeployment(
        V1Deployment deployment,
        K8sPolicyOptions options,
        List<K8sPolicyFinding> findings)
    {
        var objRef = ObjectRef(deployment);
        var podSpec = deployment.Spec?.Template?.Spec;
        if (podSpec is null)
        {
            return;
        }

        if (options.DenyHostNetwork && podSpec.HostNetwork == true)
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentHostNamespace, objRef,
                "hostNetwork is not allowed."));
        }

        if (options.DenyHostPid && podSpec.HostPID == true)
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentHostNamespace, objRef,
                "hostPID is not allowed."));
        }

        if (options.DenyHostIpc && podSpec.HostIPC == true)
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentHostNamespace, objRef,
                "hostIPC is not allowed."));
        }

        if (options.DenyHostPathVolumes)
        {
            foreach (var volume in podSpec.Volumes ?? [])
            {
                if (volume.HostPath is not null)
                {
                    findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentHostPath, objRef,
                        $"Volume '{volume.Name}' uses hostPath, which is not allowed."));
                }
            }
        }

        var allContainers = (podSpec.Containers ?? []).Concat(podSpec.InitContainers ?? []);
        foreach (var container in allContainers)
        {
            ValidateContainer(container, objRef, options, findings);
        }
    }

    private static void ValidateContainer(
        V1Container container,
        string objRef,
        K8sPolicyOptions options,
        List<K8sPolicyFinding> findings)
    {
        var sc = container.SecurityContext;

        if (options.DenyPrivilegedContainers && sc?.Privileged == true)
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentPrivilegedContainer, objRef,
                $"Container '{container.Name}' is privileged, which is not allowed."));
        }

        if (options.DenyAddedCapabilities && sc?.Capabilities?.Add?.Count > 0)
        {
            var caps = string.Join(", ", sc.Capabilities.Add);
            findings.Add(Deny(K8sConventions.PolicyCodes.DeploymentAddedCapabilities, objRef,
                $"Container '{container.Name}' adds Linux capabilities [{caps}], which is not allowed."));
        }

        if (options.DenyLatestImageTag)
        {
            var image = container.Image;
            if (string.IsNullOrEmpty(image) || !image.Contains(':') || image.EndsWith(":latest", StringComparison.Ordinal))
            {
                findings.Add(Deny(K8sConventions.PolicyCodes.ImageLatestTag, objRef,
                    $"Container '{container.Name}' uses image '{image}' which resolves to latest. Pin to a specific tag."));
            }
        }
    }

    private static void ValidateService(
        V1Service service,
        K8sPolicyOptions options,
        List<K8sPolicyFinding> findings)
    {
        var objRef = ObjectRef(service);
        var type = service.Spec?.Type;

        if (options.DenyServiceTypeLoadBalancer &&
            string.Equals(type, "LoadBalancer", StringComparison.Ordinal))
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.ServiceLoadBalancer, objRef,
                "Service type LoadBalancer is not allowed."));
        }

        if (options.DenyServiceTypeNodePort &&
            string.Equals(type, "NodePort", StringComparison.Ordinal))
        {
            findings.Add(Deny(K8sConventions.PolicyCodes.ServiceNodePort, objRef,
                "Service type NodePort is not allowed."));
        }
    }

    private static void ValidateConfigMap(
        V1ConfigMap configMap,
        K8sPolicyOptions options,
        List<K8sPolicyFinding> findings)
    {
        if (!options.WarnOnConfigMapSecretLikeKeys || configMap.Data is null)
        {
            return;
        }

        var objRef = ObjectRef(configMap);
        foreach (var key in configMap.Data.Keys)
        {
            if (IsSecretLikeKey(key))
            {
                findings.Add(Warn(K8sConventions.PolicyCodes.ConfigMapSecretLikeKey, objRef,
                    $"Key '{key}' looks like a secret. Use a Secret resource instead."));
            }
        }
    }

    private static bool IsSecretLikeKey(string key)
    {
        var lower = key.ToLowerInvariant();
        foreach (var pattern in SecretLikeKeys)
        {
            if (lower.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string ObjectRef(IKubernetesObject<V1ObjectMeta> obj) =>
        $"{obj.ApiVersion} {obj.Kind} {obj.Metadata?.NamespaceProperty}/{obj.Metadata?.Name}";

    private static K8sPolicyFinding Deny(string code, string objRef, string message) =>
        new(K8sPolicySeverity.Deny, code, objRef, message);

    private static K8sPolicyFinding Warn(string code, string objRef, string message) =>
        new(K8sPolicySeverity.Warning, code, objRef, message);
}
