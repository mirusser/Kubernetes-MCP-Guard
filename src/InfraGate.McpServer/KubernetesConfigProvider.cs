using InfraGate.RuntimeSafety;
using k8s;

namespace InfraGate.McpServer;

internal sealed class KubernetesConfigProvider(K8SMcpOptions options)
{
    public KubernetesClientConfiguration Create() =>
        Create(
            kubeConfig => KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfig),
            KubernetesClientConfiguration.InClusterConfig,
            KubernetesClientConfiguration.BuildDefaultConfig);

    internal KubernetesClientConfiguration Create(
        Func<string, KubernetesClientConfiguration> kubeConfigFactory,
        Func<KubernetesClientConfiguration> inClusterFactory,
        Func<KubernetesClientConfiguration> defaultFactory)
    {
        options.ValidateProductionSafety();

        KubernetesClientConfiguration config;
        if (options.HasExplicitKubeConfig)
        {
            string kubeConfig = options.KubeConfig ??
                                throw new InvalidOperationException($"{K8sConventions.EnvironmentVariables.KubeConfig} is required.");
            config = kubeConfigFactory(kubeConfig);
        }
        else if (options.IsInClusterConfigEnabled)
        {
            config = inClusterFactory();
        }
        else if (options.RuntimeMode == RuntimeMode.Development)
        {
            config = defaultFactory();
        }
        else
        {
            // ValidateProductionSafety rejects this today; keep this local guard so a future runtime mode cannot
            // silently fall back to developer kubeconfig discovery without explicit Kubernetes auth.
            throw new InvalidOperationException(
                $"Production mode requires explicit Kubernetes auth: set " +
                $"{K8sConventions.EnvironmentVariables.KubeConfig} or " +
                $"{K8sConventions.EnvironmentVariables.UseInClusterConfig}=true.");
        }

        config.UserAgent = K8sConventions.ServiceName;

        return config;
    }
}
