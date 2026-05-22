using System.Security.Cryptography;
using Dapper;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

public sealed class PostgresApprovalSchemaValidator(NpgsqlDataSource dataSource)
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var expectedMigrations = LoadExpectedMigrations();

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
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
                throw new InvalidOperationException(
                    "Approval PostgreSQL schema is not initialized. Run migrations before starting the application.");
            }

            var applied = await ReadAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);

            foreach (var (filename, checksum) in expectedMigrations)
            {
                if (!applied.TryGetValue(filename, out var appliedChecksum))
                {
                    throw new InvalidOperationException(
                        $"Approval PostgreSQL migration '{filename}' has not been applied. Run migrations before starting the application.");
                }

                if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Approval PostgreSQL migration '{filename}' checksum mismatch — schema may be corrupted.");
                }
            }
        }
    }

    private static async Task<Dictionary<string, string>> ReadAppliedMigrationsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = "select filename, checksum_sha256 from approvals.schema_migrations";
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    result[reader.GetString(0)] = reader.GetString(1);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<(string Filename, string Checksum)> LoadExpectedMigrations()
    {
        var migrationsDir = PostgresApprovalMigrationRunner.DefaultMigrationsDirectory;

        if (!Directory.Exists(migrationsDir))
        {
            throw new InvalidOperationException(
                $"Approval PostgreSQL migrations directory '{migrationsDir}' does not exist.");
        }

        return Directory
            .EnumerateFiles(migrationsDir, PostgresApprovalConventions.MigrationsSearchPattern)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var bytes = File.ReadAllBytes(path);
                var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToUpperInvariant();
                return (Path.GetFileName(path), checksum);
            })
            .ToArray();
    }

}
