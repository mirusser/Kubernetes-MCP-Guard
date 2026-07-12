using InfraGate.AuditOutbox;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace InfraGate.AuditOutbox.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresAuditOutbox(
        this IServiceCollection services,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);

        services.AddSingleton<IPostgresAuditOutboxCore, PostgresAuditOutboxCore>();
        services.AddSingleton<IAuditStreamReader>(_ => new PostgresAuditStreamReader(dataSource));

        return services;
    }
}
