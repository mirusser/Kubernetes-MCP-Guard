using System.Collections.Frozen;
using Dapper;
using InfraGate.AuditOutbox;
using Npgsql;

namespace InfraGate.AuditOutbox.Postgres;

// PostgresAuditStreamReader is instantiated by DI via ServiceCollectionExtensions.AddPostgresAuditOutbox.
// The reflection-based factory pattern is not visible to the CA1812 static analyzer.
#pragma warning disable CA1812
internal sealed class PostgresAuditStreamReader : IAuditStreamReader
#pragma warning restore CA1812
{
    private static readonly FrozenSet<string> StandardColumnNames = new HashSet<string>(StringComparer.Ordinal)
    {
        AuditOutboxConventions.ColumnNames.AuditSequence,
        AuditOutboxConventions.ColumnNames.EventName,
        AuditOutboxConventions.ColumnNames.OccurredAtUtc,
        AuditOutboxConventions.ColumnNames.ActorSubject,
        AuditOutboxConventions.ColumnNames.ActorClientId,
        AuditOutboxConventions.ColumnNames.Outcome,
        AuditOutboxConventions.ColumnNames.Reason,
        AuditOutboxConventions.ColumnNames.PreviousEventHash,
        AuditOutboxConventions.ColumnNames.EventHash,
        AuditOutboxConventions.ColumnNames.PayloadJsonText,
        AuditOutboxConventions.ColumnNames.PublishedAtUtc,
        AuditOutboxConventions.ColumnNames.PublishAttempts,
        AuditOutboxConventions.ColumnNames.LastPublishError,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly NpgsqlDataSource dataSource;

    public PostgresAuditStreamReader(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
    }

    public Task<IReadOnlyList<AuditStreamRow>> ReadByPlanIdAsync(
        string streamSchema,
        string planId,
        CancellationToken cancellationToken)
    {
        ValidateStreamSchema(streamSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        return ReadAsync(
            streamSchema,
            AuditOutboxConventions.CorrelationColumnNames.PlanId,
            planId,
            cancellationToken);
    }

    public Task<IReadOnlyList<AuditStreamRow>> ReadByAnomalyIdAsync(
        string streamSchema,
        string anomalyId,
        CancellationToken cancellationToken)
    {
        ValidateStreamSchema(streamSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(anomalyId);

        return ReadAsync(
            streamSchema,
            AuditOutboxConventions.CorrelationColumnNames.AnomalyId,
            anomalyId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<AuditStreamRow>> ReadAsync(
        string streamSchema,
        string correlationColumn,
        string correlationValue,
        CancellationToken cancellationToken)
    {
        ValidateSchemaName(streamSchema);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            string sql = $"""
                SELECT *
                FROM {streamSchema}.audit_outbox
                WHERE {correlationColumn} = @CorrelationValue
                ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
                """;

            var rows = await connection.QueryAsync(new CommandDefinition(
                sql,
                new { CorrelationValue = correlationValue },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var result = new List<AuditStreamRow>();
            foreach (IDictionary<string, object> row in rows)
            {
                result.Add(MapRow(row));
            }

            return result.AsReadOnly();
        }
    }

    private static AuditStreamRow MapRow(IDictionary<string, object> row)
    {
        long sequence = Convert.ToInt64(row[AuditOutboxConventions.ColumnNames.AuditSequence]);
        string eventName = (string)row[AuditOutboxConventions.ColumnNames.EventName];
        DateTimeOffset occurredAt = ReadDateTimeOffset(row[AuditOutboxConventions.ColumnNames.OccurredAtUtc]);
        string? actorSubject = row[AuditOutboxConventions.ColumnNames.ActorSubject] as string;
        string? actorClientId = row[AuditOutboxConventions.ColumnNames.ActorClientId] as string;
        string? outcome = row[AuditOutboxConventions.ColumnNames.Outcome] as string;
        string? reason = row[AuditOutboxConventions.ColumnNames.Reason] as string;
        string payloadJsonText = (string)row[AuditOutboxConventions.ColumnNames.PayloadJsonText];

        var correlationColumns = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string column in row.Keys)
        {
            if (!StandardColumnNames.Contains(column))
            {
                correlationColumns[column] = NormalizeValue(row[column]);
            }
        }

        var auditRow = new AuditOutboxRow(
            eventName,
            occurredAt,
            actorSubject,
            actorClientId,
            outcome,
            reason,
            payloadJsonText,
            correlationColumns);

        return new AuditStreamRow(sequence, auditRow);
    }

    private static object? NormalizeValue(object? value) =>
        value is DBNull ? null : value;

    private static DateTimeOffset ReadDateTimeOffset(object value) =>
        value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to DateTimeOffset.")
        };

    private static void ValidateStreamSchema(string streamSchema)
    {
        if (!IsKnownStream(streamSchema))
        {
            throw new ArgumentException(
                $"Stream schema '{streamSchema}' is not a recognized audit stream.",
                nameof(streamSchema));
        }
    }

    private static bool IsKnownStream(string streamSchema) =>
        string.Equals(streamSchema, AuditOutboxConventions.Streams.Approvals, StringComparison.Ordinal) ||
        string.Equals(streamSchema, AuditOutboxConventions.Streams.Observer, StringComparison.Ordinal) ||
        string.Equals(streamSchema, AuditOutboxConventions.Streams.Planner, StringComparison.Ordinal);

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
