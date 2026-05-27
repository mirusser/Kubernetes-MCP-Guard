using Dapper;
using InfraGate.AuditOutbox.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.AuditOutbox.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class MigrationRunnerTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17-alpine";
    private const string TestSchema = "test_stream";

    private static readonly string FixturesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
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
    public async Task ApplyAsync_EmptyDatabase_CreatesSchemaAndTables()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();

        bool schemaExists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @SchemaName)",
            new { SchemaName = TestSchema });

        bool migrationsTableExists = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'test_stream' AND table_name = 'schema_migrations'
            )
            """);

        bool outboxTableExists = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'test_stream' AND table_name = 'audit_outbox'
            )
            """);

        Assert.True(schemaExists);
        Assert.True(migrationsTableExists);
        Assert.True(outboxTableExists);
    }

    [Fact]
    public async Task ApplyAsync_AlreadyApplied_IsNoOp()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);
        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        int migrationCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM test_stream.schema_migrations");

        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public async Task ApplyAsync_ChecksumDrift_ThrowsInvalidOperationException()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE test_stream.schema_migrations SET checksum_sha256 = 'tampered'");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PostgresAuditOutboxMigrationRunner.ApplyAsync(
                dataSource, TestSchema, FixturesDirectory, CancellationToken.None));

        Assert.Contains("checksum changed after it was applied", ex.Message);
    }

    [Fact]
    public async Task ApplyAsync_NonExistentDirectory_ThrowsInvalidOperationException()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PostgresAuditOutboxMigrationRunner.ApplyAsync(
                dataSource, TestSchema, "/non/existent/dir", CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ApplyAsync_RecordsChecksumsInMigrationsTable()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await PostgresAuditOutboxMigrationRunner.ApplyAsync(
            dataSource, TestSchema, FixturesDirectory, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(string Filename, string Checksum)>(
            "SELECT filename, checksum_sha256 FROM test_stream.schema_migrations");

        Assert.Equal("0001-test-outbox.sql", row.Filename);
        Assert.NotEmpty(row.Checksum);
        Assert.Equal(64, row.Checksum.Length);
    }
}
