using InfraGate.Approvals;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Postgres;
using InfraGate.ApprovalUi;
using InfraGate.ClientCredentials;
using InfraGate.DownstreamAuth;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway.Audit;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.McpGateway.Email;
using InfraGate.McpGateway.Endpoints;
using InfraGate.McpGateway.Notifications;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace InfraGate.McpGateway;

internal static class ConfigurationExtensions
{
    extension(IConfigurationBuilder configuration)
    {
        internal void AddInfraGateConfiguration(string[] args)
        {
            string? configPath =
                Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
            }

            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(args);
        }
    }

    extension(WebApplicationBuilder builder)
    {
        internal void AddInfraGateServices()
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

            builder.AddInfraGateTelemetry(opt =>
            {
                opt.ServiceName = "infragate-gateway";
                opt.MeterNames = [McpGatewayConventions.Telemetry.MeterName];
                opt.ActivitySourceNames = [McpGatewayConventions.Telemetry.ActivitySourceName];
            });

            ConfigureUrls(builder);

            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                    Path.GetDirectoryName(options.ApprovalRoot)!,
                    ApprovalConventions.Storage.DataProtectionKeysDirectory)))
                .SetApplicationName(ApprovalConventions.Application.Name);

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(DownstreamProcessDescriptor.ForPrimary(options));
            builder.Services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
            builder.Services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
            builder.Services.AddSingleton<GuardedToolRunner>(sp =>
                new GuardedToolRunner(
                    sp.GetRequiredService<IDownstreamMcpClient>(),
                    sp.GetRequiredService<IGuardrailAuditStore>(),
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<SensitiveDataRedactor>(),
                    sp.GetRequiredService<ILogger<GuardedToolRunner>>()));

            builder.Services.AddPostgresApprovalPersistence(
                builder.Configuration[McpGatewayConventions.ConfigurationKeys.ApprovalPostgresConnectionString]);
            builder.Services.AddSingleton<AuditTimelineAssembler>();
            builder.Services.AddSingleton<IAuthorizationCheck, ApprovalPolicyAuthorizationCheck>();
            builder.Services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
            builder.Services.AddSingleton<IApprovalPageRenderer>(sp =>
                new ApprovalPageRenderer(sp.GetRequiredService<IServiceProvider>(),
                    sp.GetRequiredService<ILoggerFactory>()));
            builder.Services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
            builder.Services.AddSingleton(options.Smtp ?? new SmtpApprovalEmailOptions(
                string.Empty,
                SmtpApprovalEmailOptions.DefaultPort,
                string.Empty));
            builder.Services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
            builder.Services.AddSingleton<IApprovalEmailSender, SmtpApprovalEmailSender>();
            builder.Services.AddSingleton<IProposePlanHandler, ProposePlanHandler>();
            builder.Services.AddSingleton<SensitiveDataRedactor>(sp =>
                new SensitiveDataRedactor(
                    McpGatewayConventions.SensitiveDataRedaction.Defaults,
                    sp.GetRequiredService<ILogger<SensitiveDataRedactor>>()));
            builder.Services.AddSingleton<IToolCaller>(sp =>
                new SanitizingToolCaller(
                    sp.GetRequiredService<IDownstreamMcpClient>(),
                    sp.GetRequiredService<IGuardrailAuditStore>(),
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<SensitiveDataRedactor>(),
                    sp.GetRequiredService<ILogger<SanitizingToolCaller>>()));
            builder.Services.AddKubernetesAdapter();
            builder.Services.AddSingleton<DownstreamToolRegistry>();
            builder.Services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
            builder.Services.AddSingleton<IToolScopeGuard, ToolScopeGuard>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAntiforgery();
            builder.Services.AddGatewayAuthentication(options.Auth);

            RegisterDownstreamAuth(builder.Services, options);
            RegisterKubernetesMcpServerDownstream(builder.Services, builder.Configuration);
            RegisterReadOnlySources(builder.Services);
            RegisterReadinessChecker(builder.Services);

            builder.Services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
            builder.Services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();
            builder.Services.AddSingleton<PlanStatusResourceHandler>();
            builder.Services.AddSingleton<PlanStatusSubscriptionsListenHandler>();
        }

        private void ConfigureUrls()
        {
            if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
                string.IsNullOrWhiteSpace(
                    builder.Configuration[McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls]))
            {
                string configuredUrls = builder.Configuration[McpGatewayConventions.ConfigurationKeys.AspNetCoreUrls] ??
                                        McpGatewayOptions.DefaultUrl;
                builder.WebHost.UseUrls(configuredUrls);
            }
        }
    }

    extension(IConfiguration configuration)
    {
        internal async Task RunPostgresMigrationsAsync(WebApplication app)
        {
            if (string.Equals(configuration[McpGatewayConventions.ConfigurationKeys.ApprovalPostgresRunMigrationsOnStartup],
                    "true", StringComparison.OrdinalIgnoreCase))
            {
                var postgresDataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
                await PostgresApprovalMigrationRunner.ApplyAsync(postgresDataSource, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    extension(IServiceCollection services)
    {
        private void RegisterDownstreamAuth(McpGatewayOptions options)
        {
            DownstreamAuthOptions? downstreamAuth = options.DownstreamAuth;
            if (downstreamAuth is null || !downstreamAuth.Required)
            {
                services.AddSingleton<IDownstreamServiceTokenProvider, NullDownstreamServiceTokenProvider>();
                return;
            }

            services.AddSingleton(downstreamAuth);
            services.AddClientCredentialsTokenProvider(downstreamAuth.ToClientCredentials());
            services.AddSingleton<IDownstreamServiceTokenProvider>(sp =>
                new ClientCredentialsDownstreamServiceTokenProvider(
                    sp.GetRequiredService<IClientCredentialsTokenProvider>()));
        }

        // Optional/off-by-default second downstream: only wired when the operator has
        // configured InfraGate:Gateway:KubernetesMcpServer:Command. Duplicates the primary's
        // client/registry/runner triple under a keyed registration rather than generalizing
        // those types to an N-way source — see docs/adr for the decision record.
        internal void RegisterKubernetesMcpServerDownstream(IConfiguration configuration)
        {
            var kubernetesMcpServerOptions = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);
            if (kubernetesMcpServerOptions is null)
            {
                return;
            }

            KubernetesMcpServerStartupValidator.Validate(kubernetesMcpServerOptions);

            var descriptor = DownstreamProcessDescriptor.ForKubernetesMcpServer(kubernetesMcpServerOptions);
            var supervisorOptions = DownstreamProcessSupervisorOptions.FromConfiguration(configuration);

            services.AddKeyedSingleton<IDownstreamMcpClient>(
                McpGatewayConventions.SecondaryDownstream.ServiceKey,
                (sp, _) =>
                {
                    var client = new DownstreamMcpClient(
                        descriptor,
                        new NullDownstreamServiceTokenProvider(),
                        sp.GetRequiredService<ILogger<DownstreamMcpClient>>(),
                        sp.GetRequiredService<ILoggerFactory>(),
                        McpGatewayConventions.DownstreamSources.Secondary);

                    return new DownstreamProcessSupervisor(
                        client,
                        McpGatewayConventions.DownstreamSources.Secondary,
                        supervisorOptions,
                        sp,
                        TimeProvider.System,
                        sp.GetRequiredService<ILogger<DownstreamProcessSupervisor>>(),
                        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
                });

            services.AddKeyedSingleton<DownstreamToolRegistry>(
                McpGatewayConventions.SecondaryDownstream.ServiceKey,
                (sp, key) => new DownstreamToolRegistry(sp.GetRequiredKeyedService<IDownstreamMcpClient>(key)));

            services.AddKeyedSingleton<GuardedToolRunner>(
                McpGatewayConventions.SecondaryDownstream.ServiceKey,
                (sp, key) => new GuardedToolRunner(
                    sp.GetRequiredKeyedService<IDownstreamMcpClient>(key),
                    sp.GetRequiredService<IGuardrailAuditStore>(),
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<SensitiveDataRedactor>(),
                    sp.GetRequiredService<ILogger<GuardedToolRunner>>()));

            services.AddKeyedSingleton<KubernetesMcpServerRequestPolicy>(
                McpGatewayConventions.SecondaryDownstream.ServiceKey,
                (_, _) => new KubernetesMcpServerRequestPolicy(kubernetesMcpServerOptions.AllowedNamespaces));
            services.AddKeyedSingleton<KubernetesMcpServerResponsePolicy>(
                McpGatewayConventions.SecondaryDownstream.ServiceKey,
                (_, _) => new KubernetesMcpServerResponsePolicy());
        }

        // Composes the primary + optional secondary read-only downstream sources once, here in
        // the composition root, so GatewayToolDispatcher can take the result via ordinary
        // constructor injection instead of resolving IServiceProvider itself at runtime.
        private void RegisterReadOnlySources()
        {
            services.AddSingleton<DownstreamToolCatalog>();
            services.AddSingleton<IReadOnlyList<GatewayToolDispatcher.ReadOnlySource>>(sp =>
            {
                var sources = new List<GatewayToolDispatcher.ReadOnlySource>
                {
                    new(
                        McpGatewayConventions.DownstreamSources.Primary,
                        sp.GetRequiredService<DownstreamToolRegistry>(),
                        sp.GetRequiredService<GuardedToolRunner>())
                };

                DownstreamToolRegistry? secondaryRegistry = sp.GetKeyedService<DownstreamToolRegistry>(
                    McpGatewayConventions.SecondaryDownstream.ServiceKey);
                GuardedToolRunner? secondaryRunner = sp.GetKeyedService<GuardedToolRunner>(
                    McpGatewayConventions.SecondaryDownstream.ServiceKey);
                KubernetesMcpServerRequestPolicy? secondaryRequestPolicy =
                    sp.GetKeyedService<KubernetesMcpServerRequestPolicy>(
                        McpGatewayConventions.SecondaryDownstream.ServiceKey);
                KubernetesMcpServerResponsePolicy? secondaryResponsePolicy =
                    sp.GetKeyedService<KubernetesMcpServerResponsePolicy>(
                        McpGatewayConventions.SecondaryDownstream.ServiceKey);
                if (secondaryRegistry is not null
                    && secondaryRunner is not null
                    && secondaryRequestPolicy is not null
                    && secondaryResponsePolicy is not null)
                {
                    sources.Add(new GatewayToolDispatcher.ReadOnlySource(
                        McpGatewayConventions.DownstreamSources.Secondary,
                        secondaryRegistry,
                        secondaryRunner,
                        secondaryRequestPolicy,
                        secondaryResponsePolicy));
                }

                return sources;
            });
        }

        // The optional secondary's client is only present in DI when Task 10's Kubernetes MCP
        // server config is set; resolve it as nullable rather than requiring
        // RegisterKubernetesMcpServerDownstream to have run first.
        private void RegisterReadinessChecker()
        {
            services.AddSingleton(sp => new GatewayReadinessChecker(
                sp.GetRequiredService<NpgsqlDataSource>(),
                sp.GetRequiredService<IDownstreamMcpClient>(),
                sp.GetKeyedService<IDownstreamMcpClient>(McpGatewayConventions.SecondaryDownstream.ServiceKey),
                sp.GetRequiredService<DownstreamToolCatalog>()));
        }
    }

}
