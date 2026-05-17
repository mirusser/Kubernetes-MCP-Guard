using InfraGate.McpServer;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

var mcpOptions = K8SMcpOptions.FromEnvironment();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

if (!string.IsNullOrWhiteSpace(mcpOptions.LogPath))
{
    builder.Logging.AddProvider(new StreamWriterLoggerProvider(mcpOptions.LogPath));
}

mcpOptions.ValidateProductionSafety();
builder.Services.AddSingleton(mcpOptions);
builder.Services.AddSingleton<IKubernetes>(_ =>
{
    var config = new KubernetesConfigProvider(mcpOptions).Create();

    return new Kubernetes(config);
});
builder.Services.AddSingleton<K8sManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => (request, cancellationToken) =>
        {
            var services = request.Services;
            if (services is null)
            {
                return next(request, cancellationToken);
            }

            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("InfraGate.McpServer.ToolExceptionFilter");
            return ToolExceptionFilter.CreateSafetyNet(next, logger)(request, cancellationToken);
        });
    });

var app = builder.Build();

var appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InfraGate.McpServer");
var k8sOptions = app.Services.GetRequiredService<K8SMcpOptions>();
if (appLogger.IsEnabled(LogLevel.Information))
{
    appLogger.LogInformation(
        "InfraGate MCP Server started. KubeConfig={KubeConfig}, AllowedNamespaces={AllowedNamespaces}",
        k8sOptions.KubeConfig ?? "(default)",
        string.Join(",", k8sOptions.AllowedNamespaces.Order(StringComparer.Ordinal)));
}

using (var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    try
    {
        var k8sClient = app.Services.GetRequiredService<IKubernetes>();
        var version = await k8sClient.Version.GetCodeAsync(probeCts.Token);
        // Justification: CA1873 — log argument is a simple string property access. Negligible evaluation cost.
        appLogger.LogInformation(
            "Kubernetes connectivity OK — server version: {GitVersion}",
            version.GitVersion);
    }
    catch (Exception ex)
    {
        appLogger.LogWarning(
            ex,
            "Kubernetes connectivity check failed — K8s API unreachable. All tool calls will fail until connectivity is restored.");
    }
}

await app.RunAsync();
