using InfraGate.Approvals;
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

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IApprovalPersistence, PostgresApprovalPersistence>();
        services.AddSingleton<IApprovalPlanWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalChallengeWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalExecutionWorkflow>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<IApprovalAuditPublisher>(sp => sp.GetRequiredService<IApprovalPersistence>());
        services.AddSingleton<PostgresApprovalSchemaValidator>();

        return services;
    }
}
