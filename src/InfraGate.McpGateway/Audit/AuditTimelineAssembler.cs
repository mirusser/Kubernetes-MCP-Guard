using System.Collections.Frozen;
using System.Text.Json;
using InfraGate.ApprovalUi;
using InfraGate.AuditOutbox;

namespace InfraGate.McpGateway.Audit;

/// <summary>
/// Builds a correlated, read-only audit timeline for a single plan_id by reading
/// the approvals, planner, and observer audit streams through <see cref="IAuditStreamReader"/>.
/// </summary>
internal sealed class AuditTimelineAssembler(IAuditStreamReader reader)
{
    private static readonly FrozenSet<string> AllowedPayloadFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "namespace",
        "target_namespace",
        "operation",
        "message",
        "status",
        "gate_result",
        "digest_algorithm",
        "digest_value",
    }.ToFrozenSet(StringComparer.Ordinal);

    public async Task<AuditTimelinePageData> BuildTimelineAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        var approvals = await reader.ReadByPlanIdAsync(
            AuditOutboxConventions.Streams.Approvals,
            planId,
            cancellationToken).ConfigureAwait(false);

        var planner = await reader.ReadByPlanIdAsync(
            AuditOutboxConventions.Streams.Planner,
            planId,
            cancellationToken).ConfigureAwait(false);

        string? anomalyId = FindAnomalyId(planner, approvals);

        IReadOnlyList<AuditStreamRow> observer = [];
        if (!string.IsNullOrWhiteSpace(anomalyId))
        {
            observer = await reader.ReadByAnomalyIdAsync(
                AuditOutboxConventions.Streams.Observer,
                anomalyId,
                cancellationToken).ConfigureAwait(false);
        }

        var entries = new List<AuditTimelineEntry>(
            approvals.Count + planner.Count + observer.Count);
        entries.AddRange(MapRows(AuditOutboxConventions.Streams.Approvals, approvals));
        entries.AddRange(MapRows(AuditOutboxConventions.Streams.Planner, planner));
        entries.AddRange(MapRows(AuditOutboxConventions.Streams.Observer, observer));

        AuditTimelineEntry[] ordered = entries
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Stream, StringComparer.Ordinal)
            .ToArray();

        return new AuditTimelinePageData(planId, anomalyId, ordered);
    }

    private static string? FindAnomalyId(params IReadOnlyList<AuditStreamRow>[] sources)
    {
        foreach (IReadOnlyList<AuditStreamRow> source in sources)
        {
            foreach (AuditStreamRow row in source)
            {
                if (row.Row.CorrelationColumns.TryGetValue(
                        AuditOutboxConventions.CorrelationColumnNames.AnomalyId,
                        out object? value) &&
                    value is string anomalyId &&
                    !string.IsNullOrWhiteSpace(anomalyId))
                {
                    return anomalyId;
                }
            }
        }

        return null;
    }

    private static IEnumerable<AuditTimelineEntry> MapRows(
        string stream,
        IReadOnlyList<AuditStreamRow> rows) =>
        rows.Select(row => new AuditTimelineEntry(
            row.Row.OccurredAtUtc,
            stream,
            row.Row.EventName,
            row.Row.ActorSubject,
            row.Row.ActorClientId,
            row.Row.Outcome,
            row.Row.Reason,
            ExtractDisplayFields(row.Row.PayloadJsonText)));

    private static IReadOnlyDictionary<string, string?> ExtractDisplayFields(string payloadJsonText)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJsonText);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (AllowedPayloadFields.Contains(property.Name) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Payload is not valid JSON; leave display fields empty.
        }

        return result.AsReadOnly();
    }
}
