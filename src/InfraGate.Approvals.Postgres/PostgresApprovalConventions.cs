namespace InfraGate.Approvals.Postgres;

internal static class PostgresApprovalConventions
{
    public const string Schema = "approvals";
    public const string MigrationsDirectoryName = "Migrations";
    public const string MigrationsSearchPattern = "*.sql";
    public const long MigrationLockKey = 4_613_604_770_001;
}
