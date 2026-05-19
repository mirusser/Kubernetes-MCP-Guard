using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
AddInfraGateConfiguration(builder.Configuration, args);

builder.Services.Configure<InfraGateGatewaySettings>(
    builder.Configuration.GetSection("InfraGate:Gateway"));
builder.Services.Configure<InfraGateAuthSettings>(
    builder.Configuration.GetSection("InfraGate:Auth"));
builder.Services.Configure<InfraGateApprovalSettings>(
    builder.Configuration.GetSection("InfraGate:Approval"));

var options = McpGatewayOptions.FromConfiguration(builder.Configuration);
options.ValidateProductionSafety();

builder.AddInfraGateObservability(opt => 
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
    string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls]))
{
    string configuredUrls = builder.Configuration[McpGatewayConventions.ConfigurationKeys.AspNetCoreUrls] ??
        McpGatewayOptions.DefaultUrl;
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
        Path.GetDirectoryName(options.ApprovalRoot)!,
        ApprovalConventions.Storage.DataProtectionKeysDirectory)))
    .SetApplicationName(ApprovalConventions.Application.Name);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
builder.Services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
builder.Services.AddSingleton<GuardedToolRunner>();
builder.Services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
builder.Services.AddSingleton<ApprovalStore>();
builder.Services.AddSingleton<IApprovalAuditPublisher, ApprovalStoreAuditPublisher>();
builder.Services.AddSingleton<IApprovalChallengeStore, ApprovalChallengeStore>();
builder.Services.AddSingleton<IAuthorizationCheck, SameSubjectAuthorizationCheck>();
builder.Services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
builder.Services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
builder.Services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
builder.Services.AddKubernetesAdapter();
builder.Services.AddSingleton<DownstreamToolRegistry>();
builder.Services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery();
builder.Services.AddGatewayAuthentication(options.Auth);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithListToolsHandler((RequestContext<ListToolsRequestParams> request, CancellationToken ct) =>
        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().ListToolsAsync(request.Params, ct)))
    .WithCallToolHandler((RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
        new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().CallToolAsync(request.Params, ct)));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync();

static void AddInfraGateConfiguration(IConfigurationBuilder configuration, string[] args)
{
    string? configPath = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
    if (!string.IsNullOrWhiteSpace(configPath))
    {
        configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
        configuration.AddInfraGateEnvironmentVariables(mappings =>
        {
            RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);
            McpGatewayConventions.RegisterInfraGateEnvVarMappings(mappings);
            GatewayAuthConventions.RegisterInfraGateEnvVarMappings(mappings);
            // ApprovalRoot env var is shared; the gateway reads K8S_MCP_APPROVAL_ROOT
            mappings.Map(ApprovalConventions.EnvironmentVariables.ApprovalRoot, McpGatewayConventions.ConfigurationKeys.ApprovalRoot);
        });
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
