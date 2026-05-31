using Dapper;
using InfraGate.Approvals.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresApprovalMigrationRunnerTests : IAsyncLifetime
{


    private static readonly string[] ExpectedTables =
    [
        "applied_plans",
        "approval_access_codes",
        "approval_challenges",
        "approval_grants",
        "audit_outbox",
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
        container = new PostgreSqlBuilder(TestContainersConstants.PostgresImage)
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

        Assert.Equal(3, migrationCount);
    }

    [Fact]
    public async Task ApplyAsync_EmptyDatabase_AddsPlanPolicyColumns()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        var columns = (await connection.QueryAsync<string>(
                """
                select column_name
                from information_schema.columns
                where table_schema = 'approvals'
                  and table_name = 'plan_envelopes'
                  and column_name in ('policy_kind', 'operator_group')
                order by column_name
                """))
            .ToArray();

        Assert.Equal(["operator_group", "policy_kind"], columns);
    }

    [Fact]
    public async Task ApplyAsync_EmptyDatabase_AddsApprovalAccessCodeTable()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        bool exists = await connection.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'approvals'
                  and table_name = 'approval_access_codes')
            """);

        Assert.True(exists);
    }

    [Fact]
    public async Task ApplyAsync_ChecksumDrift_ThrowsInvalidOperationException()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using (var connection = await dataSource.OpenConnectionAsync(CancellationToken.None))
        {
            await connection.ExecuteAsync(
                "update approvals.schema_migrations set checksum_sha256 = 'tampered-checksum'");
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None));

        Assert.Contains("checksum changed after it was applied", ex.Message);
    }
}
