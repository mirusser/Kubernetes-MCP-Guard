using InfraGate.Approvals;
using InfraGate.McpServer;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var options = K8sMcpOptions.FromEnvironment();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
builder.Services.AddSingleton<ApprovalStore>();
builder.Services.AddSingleton<IKubernetes>(_ =>
{
    var kubeconfig = Environment.GetEnvironmentVariable(K8sConventions.EnvironmentVariables.KubeConfig);
    var config = string.IsNullOrWhiteSpace(kubeconfig)
        ? KubernetesClientConfiguration.BuildDefaultConfig()
        : KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfig);

    config.UserAgent = K8sConventions.ServiceName;

    return new Kubernetes(config);
});
builder.Services.AddSingleton<K8sManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
