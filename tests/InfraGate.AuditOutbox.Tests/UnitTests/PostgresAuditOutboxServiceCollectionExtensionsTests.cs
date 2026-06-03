using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace InfraGate.AuditOutbox.Tests.UnitTests;

public sealed class PostgresAuditOutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPostgresAuditOutbox_DataSource_RegistersPostgresAuditOutboxCore()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=localhost");
        var services = new ServiceCollection();

        services.AddPostgresAuditOutbox(dataSource);

        using var provider = services.BuildServiceProvider();
        var core = provider.GetRequiredService<IPostgresAuditOutboxCore>();

        Assert.NotNull(core);
    }
}
