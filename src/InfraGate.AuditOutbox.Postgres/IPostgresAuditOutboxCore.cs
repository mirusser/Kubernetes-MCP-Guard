using InfraGate.AuditOutbox;
using Npgsql;

namespace InfraGate.AuditOutbox.Postgres;

public interface IPostgresAuditOutboxCore
{
    Task<long> AppendAsync(
        string streamSchema,
        AuditOutboxRow row,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
