using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Npgsql;

namespace InfraGate.Observer.Audit;

internal static class ObserverAuditOutboxServiceCollectionExtensions
{
    internal static IServiceCollection AddObserverAuditOutbox(
        this IServiceCollection services,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        services.AddPostgresAuditOutbox(dataSource);
        services.AddSingleton<IObserverAuditOutbox>(sp =>
            new ObserverAuditOutbox(sp.GetRequiredService<IAuditOutboxCore>(), dataSource));

        return services;
    }
}
