using InfraGate.Approvals;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;

var options = McpGatewayOptions.FromEnvironment();
options.ValidateProductionSafety();
var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls)))
{
    builder.WebHost.UseUrls(McpGatewayOptions.DefaultUrl);
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<PromptInjectionGuard>();
builder.Services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
builder.Services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
builder.Services.AddSingleton<GuardedToolRunner>();
builder.Services.AddSingleton(new ApprovalStoreOptions(options.ApprovalRoot));
builder.Services.AddSingleton<ApprovalStore>();
builder.Services.AddSingleton<ApprovalChallengeStore>();
builder.Services.AddSingleton<GatewayApprovalService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery();
builder.Services.AddGatewayAuthentication(options.Auth);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync();
