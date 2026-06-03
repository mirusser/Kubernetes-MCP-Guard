using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Npgsql;

namespace InfraGate.Planner.Audit;

internal static class PlannerAuditOutboxServiceCollectionExtensions
{
    internal static IServiceCollection AddPlannerAuditOutbox(
        this IServiceCollection services,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        services.AddPostgresAuditOutbox(dataSource);
        services.AddSingleton<IPlannerAuditOutbox>(sp =>
            new PlannerAuditOutbox(sp.GetRequiredService<IPostgresAuditOutboxCore>(), dataSource));

        return services;
    }
}
