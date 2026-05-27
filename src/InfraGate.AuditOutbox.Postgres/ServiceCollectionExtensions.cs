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

        services.AddSingleton(dataSource);
        services.AddSingleton<IAuditOutboxCore, PostgresAuditOutboxCore>();

        return services;
    }
}
