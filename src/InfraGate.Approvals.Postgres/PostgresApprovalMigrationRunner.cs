using System.Security.Cryptography;
using Dapper;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

public static class PostgresApprovalMigrationRunner
{
    public static string DefaultMigrationsDirectory =>
        Path.Combine(AppContext.BaseDirectory, PostgresApprovalConventions.MigrationsDirectoryName);

    public static Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        ApplyAsync(dataSource, DefaultMigrationsDirectory, cancellationToken);

    public static async Task ApplyAsync(
        NpgsqlDataSource dataSource,
        string migrationsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            throw new InvalidOperationException(
                $"Approval PostgreSQL migrations directory '{migrationsDirectory}' does not exist.");
        }

        var migrations = Directory
            .EnumerateFiles(migrationsDirectory, PostgresApprovalConventions.MigrationsSearchPattern)
            .Order(StringComparer.Ordinal)
            .Select(MigrationFile.Read)
            .ToArray();

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select pg_advisory_lock(@LockKey)",
                new { LockKey = PostgresApprovalConventions.MigrationLockKey },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            try
            {
                foreach (var migration in migrations)
                {
                    await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "select pg_advisory_unlock(@LockKey)",
                    new { LockKey = PostgresApprovalConventions.MigrationLockKey },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        MigrationFile migration,
        CancellationToken cancellationToken)
    {
        string? appliedChecksum = await TryGetAppliedMigrationChecksumAsync(
            connection,
            migration.FileName,
            cancellationToken).ConfigureAwait(false);

        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, migration.ChecksumSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Approval PostgreSQL migration '{migration.FileName}' checksum changed after it was applied.");
            }

            return;
        }

        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    migration.Sql,
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into approvals.schema_migrations (filename, checksum_sha256)
                    values (@FileName, @ChecksumSha256)
                    """,
                    new
                    {
                        migration.FileName,
                        migration.ChecksumSha256
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async Task<string?> TryGetAppliedMigrationChecksumAsync(
        NpgsqlConnection connection,
        string fileName,
        CancellationToken cancellationToken)
    {
        bool migrationsTableExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'approvals'
                  and table_name = 'schema_migrations'
            )
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (!migrationsTableExists)
        {
            return null;
        }

        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select checksum_sha256
            from approvals.schema_migrations
            where filename = @FileName
            """,
            new { FileName = fileName },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed record class MigrationFile(string FileName, string Sql, string ChecksumSha256)
    {
        public static MigrationFile Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToUpperInvariant();

            return new MigrationFile(Path.GetFileName(path), File.ReadAllText(path), checksum);
        }
    }
}
