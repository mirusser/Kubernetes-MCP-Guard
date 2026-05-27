namespace InfraGate.RunProfiles;

internal sealed record class GenericApprovalCoreProfile(string ApprovalRoot, string? PostgresConnectionString = null, bool? RunMigrationsOnStartup = null);
