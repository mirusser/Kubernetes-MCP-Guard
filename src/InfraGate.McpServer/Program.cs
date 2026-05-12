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

var options = K8SMcpOptions.FromEnvironment();
options.ValidateProductionSafety();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
builder.Services.AddSingleton<ApprovalStore>();
builder.Services.AddSingleton<IKubernetes>(_ =>
{
    var config = new KubernetesConfigProvider(options).Create();

    return new Kubernetes(config);
});
builder.Services.AddSingleton<K8sManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

var appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InfraGate.McpServer");
var k8sOptions = app.Services.GetRequiredService<K8SMcpOptions>();
appLogger.LogInformation(
    "InfraGate MCP Server started. KubeConfig={KubeConfig}, AllowedNamespaces={AllowedNamespaces}",
    k8sOptions.KubeConfig ?? "(default)",
    string.Join(",", k8sOptions.AllowedNamespaces.Order(StringComparer.Ordinal)));

await app.RunAsync();
