using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.Observability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var options = McpGatewayOptions.FromEnvironment();
options.ValidateProductionSafety();
var builder = WebApplication.CreateBuilder(args);

builder.AddInfraGateObservability(opt => 
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls)))
{
    builder.WebHost.UseUrls(McpGatewayOptions.DefaultUrl);
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
builder.Services.AddSingleton<IPlanReviewAdapter, KubernetesPlanReviewAdapter>();
builder.Services.AddSingleton<IPlanReviewRenderer, KubernetesPlanReviewRenderer>();
builder.Services.AddSingleton<ApprovalChallengeStore>();
builder.Services.AddSingleton<GatewayApprovalService>();
builder.Services.AddSingleton<IDomainPlanBuilder, KubernetesPlanBuilder>();
builder.Services.AddSingleton<IDomainPlanExecutor, KubernetesPlanExecutor>();
builder.Services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
builder.Services.AddSingleton<DownstreamToolRegistry>();
builder.Services.AddSingleton<GatewayToolDispatcher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery();
builder.Services.AddGatewayAuthentication(options.Auth);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithListToolsHandler((RequestContext<ListToolsRequestParams> request, CancellationToken ct) =>
        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<GatewayToolDispatcher>().ListToolsAsync(request.Params, ct)))
    .WithCallToolHandler((RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
        new ValueTask<CallToolResult>(request.Services!.GetRequiredService<GatewayToolDispatcher>().CallToolAsync(request.Params, ct)));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync();
