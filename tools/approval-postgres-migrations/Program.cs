using InfraGate.Approvals.Postgres;
using Npgsql;

if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("usage: ApplyApprovalPostgresMigrations --connection-string <string>").ConfigureAwait(false);
    return 1;
}

string? connectionString = null;
for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--connection-string", StringComparison.Ordinal) &&
        i + 1 < args.Length)
    {
        connectionString = args[i + 1];
        break;
    }
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("--connection-string is required.").ConfigureAwait(false);
    return 1;
}

var dataSource = NpgsqlDataSource.Create(connectionString);
try
{
    await PostgresApprovalMigrationRunner.ApplyAsync(dataSource, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine("Approval PostgreSQL migrations applied successfully.");
    return 0;
}
finally
{
    await dataSource.DisposeAsync().ConfigureAwait(false);
}
