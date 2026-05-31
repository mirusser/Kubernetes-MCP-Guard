using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.AccessCodes;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

public static class PostgresApprovalPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresApprovalPersistence(
        this IServiceCollection services,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var dataSource = NpgsqlDataSource.Create(connectionString);
        services.AddSingleton(dataSource);
        services.AddPostgresAuditOutbox(dataSource);
        services.AddSingleton<ITransactionalApprovalAuditOutbox, ApprovalAuditOutbox>();
        services.AddSingleton<IApprovalAuditOutbox>(sp => sp.GetRequiredService<ITransactionalApprovalAuditOutbox>());
        services.AddSingleton<IApprovalPersistence, PostgresApprovalPersistence>();
        services.AddSingleton<IApprovalPlanWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalChallengeWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalExecutionWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalAccessCodeStore, PostgresApprovalAccessCodeStore>();
        services.AddSingleton<PostgresApprovalSchemaValidator>();

        return services;
    }
}
