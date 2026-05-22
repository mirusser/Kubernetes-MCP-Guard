using InfraGate.Approvals.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.Approvals.Postgres.Tests.IntegrationTests;

[Trait("Category", "Postgres")]
public sealed class PostgresApprovalSchemaValidatorTests : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17-alpine";

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
    public async Task ValidateAsync_UnmigratedDatabase_ThrowsInvalidOperationException()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        var validator = new PostgresApprovalSchemaValidator(dataSource);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_MigratedDatabase_Succeeds()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        var validator = new PostgresApprovalSchemaValidator(dataSource);

        await validator.ValidateAsync(CancellationToken.None);
    }
}
