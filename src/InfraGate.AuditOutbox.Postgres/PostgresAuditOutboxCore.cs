using Dapper;
using InfraGate.AuditOutbox;
using Npgsql;

namespace InfraGate.AuditOutbox.Postgres;

// PostgresAuditOutboxCore is instantiated by DI via ServiceCollectionExtensions.AddPostgresAuditOutbox.
// The reflection-based factory pattern is not visible to the CA1812 static analyzer.
#pragma warning disable CA1812
internal sealed class PostgresAuditOutboxCore : IPostgresAuditOutboxCore
#pragma warning restore CA1812
{
    public async Task<long> AppendAsync(
        string streamSchema,
        AuditOutboxRow row,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamSchema);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        ValidateSchemaName(streamSchema);

        int streamKey = AuditOutboxConventions.StreamLockKey(streamSchema);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@Category, @StreamKey)",
            new { Category = AuditOutboxConventions.LockCategory, StreamKey = streamKey },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        string? previousHash = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            GetSelectPreviousHashSql(streamSchema),
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var canonicalInputObject = AuditOutboxConventions.BuildCanonicalInputObject(row);
        string canonicalText = CanonicalJson.Serialize(canonicalInputObject);
        string hashInput = (previousHash ?? string.Empty) + canonicalText;
        string eventHash = CanonicalJson.ComputeSha256Hex(hashInput);

        var parameters = BuildInsertParameters(row, previousHash, eventHash);
        string sql = BuildInsertSql(streamSchema, row.CorrelationColumns.Keys);

        long auditSequence = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            parameters,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return auditSequence;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> SelectPreviousHashSqlCache = new(StringComparer.Ordinal);

    private static string GetSelectPreviousHashSql(string schema) =>
        SelectPreviousHashSqlCache.GetOrAdd(
            schema,
            static s => $"SELECT event_hash FROM {s}.audit_outbox ORDER BY audit_sequence DESC LIMIT 1");

    private static DynamicParameters BuildInsertParameters(
        AuditOutboxRow row,
        string? previousHash,
        string eventHash)
    {
        var parameters = new DynamicParameters();
        parameters.Add("EventName", row.EventName);
        parameters.Add("OccurredAtUtc", row.OccurredAtUtc);
        parameters.Add("ActorSubject", row.ActorSubject);
        parameters.Add("ActorClientId", row.ActorClientId);
        parameters.Add("Outcome", row.Outcome);
        parameters.Add("Reason", row.Reason);
        parameters.Add("PreviousEventHash", previousHash);
        parameters.Add("EventHash", eventHash);
        parameters.Add("PayloadJsonText", row.PayloadJsonText);

        foreach (var (colName, value) in row.CorrelationColumns)
        {
            parameters.Add(colName, value);
        }

        return parameters;
    }

    private static string BuildInsertSql(string schema, IEnumerable<string> correlationKeys)
    {
        ArgumentNullException.ThrowIfNull(correlationKeys);

        var keys = correlationKeys.ToArray();
        string corrColumns = keys.Length > 0 ? ", " + string.Join(", ", keys) : string.Empty;
        string corrParams = keys.Length > 0 ? ", " + string.Join(", ", keys.Select(k => $"@{k}")) : string.Empty;

        return $"""
            INSERT INTO {schema}.audit_outbox (
                event_name, occurred_at_utc, actor_subject, actor_client_id,
                outcome, reason, previous_event_hash, event_hash, payload_json_text{corrColumns}
            ) VALUES (
                @EventName, @OccurredAtUtc, @ActorSubject, @ActorClientId,
                @Outcome, @Reason, @PreviousEventHash, @EventHash, @PayloadJsonText{corrParams}
            ) RETURNING audit_sequence
            """;
    }

    private static void ValidateSchemaName(string schema)
    {
        if (schema.Length == 0 || !char.IsLetter(schema[0]) && schema[0] != '_')
        {
            throw new ArgumentException(
                $"Schema name '{schema}' is not a valid PostgreSQL identifier.", nameof(schema));
        }

        for (int i = 1; i < schema.Length; i++)
        {
            if (!char.IsLetterOrDigit(schema[i]) && schema[i] != '_')
            {
                throw new ArgumentException(
                    $"Schema name '{schema}' is not a valid PostgreSQL identifier.", nameof(schema));
            }
        }
    }
}
