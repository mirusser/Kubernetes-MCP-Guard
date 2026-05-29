using Dapper;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Planner.IntegrationTests.IntegrationTests;

public sealed class PlannerPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    public NpgsqlDataSource? DataSource { get; private set; }
    internal IAuditOutboxCore? Core { get; private set; }

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await container.StartAsync();

        DataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
        if (!Directory.Exists(fixturesDir))
        {
            // Try fallback path if running from source tree directly
            var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../src/InfraGate.Planner/Migrations"));
            if (Directory.Exists(sourcePath)) fixturesDir = sourcePath;
        }

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            DataSource, "planner_audit", fixturesDir, CancellationToken.None);

        var services = new ServiceCollection();
        services.AddPostgresAuditOutbox(DataSource);
        Core = services.BuildServiceProvider().GetRequiredService<IAuditOutboxCore>();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        if (container is not null) await container.DisposeAsync();
    }
}

[CollectionDefinition("PlannerPostgres")]
public class PlannerPostgresCollection : ICollectionFixture<PlannerPostgresFixture>
{
}
