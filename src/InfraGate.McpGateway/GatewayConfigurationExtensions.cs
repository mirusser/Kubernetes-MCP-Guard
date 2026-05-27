using InfraGate.Approvals;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Postgres;
using InfraGate.ApprovalUi;
using InfraGate.ClientCredentials;
using InfraGate.DownstreamAuth;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.McpGateway.Email;
using InfraGate.McpGateway.Notifications;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace InfraGate.McpGateway;

internal static class GatewayConfigurationExtensions
{
    internal static void AddInfraGateConfiguration(IConfigurationBuilder configuration, string[] args)
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
                mappings.Map(ApprovalConventions.EnvironmentVariables.ApprovalRoot, McpGatewayConventions.ConfigurationKeys.ApprovalRoot);
            });
            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(args);
        }
    }

    internal static void AddInfraGateServices(this WebApplicationBuilder builder)
    {
        var options = McpGatewayOptions.FromConfiguration(builder.Configuration);
        options.ValidateProductionSafety();

        builder.Services.Configure<InfraGateGatewaySettings>(
            builder.Configuration.GetSection("InfraGate:Gateway"));
        builder.Services.Configure<InfraGateAuthSettings>(
            builder.Configuration.GetSection("InfraGate:Auth"));
        builder.Services.Configure<InfraGateApprovalSettings>(
            builder.Configuration.GetSection("InfraGate:Approval"));

        builder.AddInfraGateObservability(opt =>
        {
            opt.WriteToConsole = true;
            opt.ConsoleToStandardError = false;
        });

        ConfigureUrls(builder);

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                Path.GetDirectoryName(options.ApprovalRoot)!,
                ApprovalConventions.Storage.DataProtectionKeysDirectory)))
            .SetApplicationName(ApprovalConventions.Application.Name);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
        builder.Services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
        builder.Services.AddSingleton<GuardedToolRunner>();
        builder.Services.AddPostgresApprovalPersistence(
            builder.Configuration[McpGatewayConventions.ConfigurationKeys.ApprovalPostgresConnectionString]);
        builder.Services.AddSingleton<IAuthorizationCheck, ApprovalPolicyAuthorizationCheck>();
        builder.Services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
        builder.Services.AddSingleton<IApprovalPageRenderer>(sp =>
            new ApprovalPageRenderer(sp.GetRequiredService<IServiceProvider>(), sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
        builder.Services.AddSingleton(options.Smtp ?? new SmtpApprovalEmailOptions(
            string.Empty,
            SmtpApprovalEmailOptions.DefaultPort,
            string.Empty));
        builder.Services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
        builder.Services.AddSingleton<IApprovalEmailSender, SmtpApprovalEmailSender>();
        builder.Services.AddSingleton<IProposePlanHandler, ProposePlanHandler>();
        builder.Services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
        builder.Services.AddKubernetesAdapter();
        builder.Services.AddSingleton<DownstreamToolRegistry>();
        builder.Services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
        builder.Services.AddSingleton<IToolScopeGuard, ToolScopeGuard>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAntiforgery();
        builder.Services.AddGatewayAuthentication(options.Auth);

        RegisterDownstreamAuth(builder.Services, options);

        builder.Services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
        builder.Services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();
        builder.Services.AddSingleton<PlanStatusResourceHandler>();
    }

    internal static async Task RunPostgresMigrationsAsync(IConfiguration configuration, WebApplication app)
    {
        if (string.Equals(configuration[McpGatewayConventions.ConfigurationKeys.ApprovalPostgresRunMigrationsOnStartup], "true", StringComparison.OrdinalIgnoreCase))
        {
            var postgresDataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
            await PostgresApprovalMigrationRunner.ApplyAsync(postgresDataSource, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void ConfigureUrls(WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
            string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls]))
        {
            string configuredUrls = builder.Configuration[McpGatewayConventions.ConfigurationKeys.AspNetCoreUrls] ??
                McpGatewayOptions.DefaultUrl;
            builder.WebHost.UseUrls(configuredUrls);
        }
    }

    private static void RegisterDownstreamAuth(IServiceCollection services, McpGatewayOptions options)
    {
        var downstreamAuth = options.DownstreamAuth ?? new DownstreamAuthOptions();
        if (downstreamAuth.Required)
        {
            services.AddSingleton(downstreamAuth);
            var clientOptions = new ClientCredentialsTokenOptions
            {
                Authority = downstreamAuth.Authority,
                MetadataAddress = downstreamAuth.MetadataAddress,
                RequireHttpsMetadata = downstreamAuth.RequireHttpsMetadata,
                ClientId = downstreamAuth.GatewayClientId,
                ClientSecret = downstreamAuth.GatewayClientSecret,
                Scope = downstreamAuth.Scope
            };
            services.AddClientCredentialsTokenProvider(clientOptions);
            services.AddSingleton<IDownstreamServiceTokenProvider>(sp =>
                new ClientCredentialsDownstreamServiceTokenProvider(
                    sp.GetRequiredService<IClientCredentialsTokenProvider>()));
        }
        else
        {
            services.AddSingleton<IDownstreamServiceTokenProvider, NullDownstreamServiceTokenProvider>();
        }
    }
}
