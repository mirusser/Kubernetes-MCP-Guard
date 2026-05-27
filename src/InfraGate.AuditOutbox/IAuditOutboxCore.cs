using Npgsql;

namespace InfraGate.AuditOutbox;

public interface IAuditOutboxCore
{
    Task<long> AppendAsync(
        string streamSchema,
        AuditOutboxRow row,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
