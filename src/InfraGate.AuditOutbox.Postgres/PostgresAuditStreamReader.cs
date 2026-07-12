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

    // Every audit query below is a compile-time constant selected via SelectSql's switch, so no
    // runtime value is ever interpolated into SQL text (avoids SonarQube S2077: dynamically
    // formatted SQL). Only the @CorrelationValue parameter varies per call, via Dapper.
    private const string ApprovalsByPlanIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Approvals}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.PlanId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

    private const string ApprovalsByAnomalyIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Approvals}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.AnomalyId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

    private const string ObserverByPlanIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Observer}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.PlanId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

    private const string ObserverByAnomalyIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Observer}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.AnomalyId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

    private const string PlannerByPlanIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Planner}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.PlanId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

    private const string PlannerByAnomalyIdSql = $"""
        SELECT *
        FROM {AuditOutboxConventions.Streams.Planner}.audit_outbox
        WHERE {AuditOutboxConventions.CorrelationColumnNames.AnomalyId} = @CorrelationValue
        ORDER BY {AuditOutboxConventions.ColumnNames.AuditSequence}
        """;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        return ReadAsync(
            SelectSql(streamSchema, AuditOutboxConventions.CorrelationColumnNames.PlanId),
            planId,
            cancellationToken);
    }

    public Task<IReadOnlyList<AuditStreamRow>> ReadByAnomalyIdAsync(
        string streamSchema,
        string anomalyId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anomalyId);

        return ReadAsync(
            SelectSql(streamSchema, AuditOutboxConventions.CorrelationColumnNames.AnomalyId),
            anomalyId,
            cancellationToken);
    }

    private static string SelectSql(string streamSchema, string correlationColumn) =>
        (streamSchema, correlationColumn) switch
        {
            (AuditOutboxConventions.Streams.Approvals, AuditOutboxConventions.CorrelationColumnNames.PlanId) => ApprovalsByPlanIdSql,
            (AuditOutboxConventions.Streams.Approvals, AuditOutboxConventions.CorrelationColumnNames.AnomalyId) => ApprovalsByAnomalyIdSql,
            (AuditOutboxConventions.Streams.Observer, AuditOutboxConventions.CorrelationColumnNames.PlanId) => ObserverByPlanIdSql,
            (AuditOutboxConventions.Streams.Observer, AuditOutboxConventions.CorrelationColumnNames.AnomalyId) => ObserverByAnomalyIdSql,
            (AuditOutboxConventions.Streams.Planner, AuditOutboxConventions.CorrelationColumnNames.PlanId) => PlannerByPlanIdSql,
            (AuditOutboxConventions.Streams.Planner, AuditOutboxConventions.CorrelationColumnNames.AnomalyId) => PlannerByAnomalyIdSql,
            _ => throw new ArgumentException(
                $"Stream schema '{streamSchema}' is not a recognized audit stream.", nameof(streamSchema)),
        };

    private async Task<IReadOnlyList<AuditStreamRow>> ReadAsync(
        string sql,
        string correlationValue,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync(new CommandDefinition(
                sql,
                new { CorrelationValue = correlationValue },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var result = new List<AuditStreamRow>();
            foreach (IDictionary<string, object> row in rows.Cast<IDictionary<string, object>>())
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
        foreach (string column in row.Keys.Where(k => !StandardColumnNames.Contains(k)))
        {
            correlationColumns[column] = NormalizeValue(row[column]);
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
}
