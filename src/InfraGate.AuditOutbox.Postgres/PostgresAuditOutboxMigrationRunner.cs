using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace InfraGate.AuditOutbox.Postgres;

public static class PostgresAuditOutboxMigrationRunner
{
    private const string MigrationsSearchPattern = "*.sql";

    public static async Task ApplyAsync(
        NpgsqlDataSource dataSource,
        string schemaName,
        string migrationsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            throw new InvalidOperationException(
                $"AuditOutbox PostgreSQL migrations directory '{migrationsDirectory}' does not exist.");
        }

        var migrations = Directory
            .EnumerateFiles(migrationsDirectory, MigrationsSearchPattern)
            .Order(StringComparer.Ordinal)
            .Select(MigrationFile.Read)
            .ToArray();

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            long lockKey = MigrationLockKey(schemaName);

            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_lock(@LockKey)",
                new { LockKey = lockKey },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            try
            {
                foreach (var migration in migrations)
                {
                    await ApplyMigrationAsync(connection, schemaName, migration, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_advisory_unlock(@LockKey)",
                    new { LockKey = lockKey },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        string schemaName,
        MigrationFile migration,
        CancellationToken cancellationToken)
    {
        string? appliedChecksum = await TryGetAppliedChecksumAsync(
            connection, schemaName, migration.FileName, cancellationToken).ConfigureAwait(false);

        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, migration.ChecksumSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"AuditOutbox PostgreSQL migration '{migration.FileName}' checksum changed after it was applied.");
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
                    $"""
                    INSERT INTO {schemaName}.schema_migrations (filename, checksum_sha256)
                    VALUES (@FileName, @ChecksumSha256)
                    """,
                    new { migration.FileName, migration.ChecksumSha256 },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async Task<string?> TryGetAppliedChecksumAsync(
        NpgsqlConnection connection,
        string schemaName,
        string fileName,
        CancellationToken cancellationToken)
    {
        bool tableExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @SchemaName
                  AND table_name = 'schema_migrations'
            )
            """,
            new { SchemaName = schemaName },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (!tableExists)
        {
            return null;
        }

        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            $"""
            SELECT checksum_sha256
            FROM {schemaName}.schema_migrations
            WHERE filename = @FileName
            """,
            new { FileName = fileName },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    // Stable migration lock key derived from schema name to avoid collisions across schemas.
    private static long MigrationLockKey(string schemaName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("audit_outbox_migration:" + schemaName));
        return BitConverter.ToInt64(hash, 0);
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
