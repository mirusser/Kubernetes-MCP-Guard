using Dapper;
using InfraGate.Approvals.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresApprovalMigrationRunnerTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17-alpine";

    private static readonly string[] ExpectedTables =
    [
        "applied_plans",
        "approval_challenges",
        "approval_grants",
        "audit_events",
        "challenge_outcomes",
        "execution_attempts",
        "execution_outcomes",
        "plan_envelopes",
        "plan_execution_claims",
        "schema_migrations"
    ];

    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage)
            .Build();

        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ApplyAsync_EmptyDatabase_CreatesApprovalSchemaTables()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using (var tables = await dataSource.OpenConnectionAsync(CancellationToken.None))
        {
            var actual = (await tables.QueryAsync<string>(
                    """
                    select table_name
                    from information_schema.tables
                    where table_schema = 'approvals'
                    order by table_name
                    """)
                ).ToArray();

            Assert.Equal(ExpectedTables, actual);
        }
    }

    [Fact]
    public async Task ApplyAsync_AlreadyAppliedWithSameChecksum_IsNoOp()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        int migrationCount = await connection.ExecuteScalarAsync<int>(
            "select count(*) from approvals.schema_migrations");

        Assert.Equal(1, migrationCount);
    }
}
