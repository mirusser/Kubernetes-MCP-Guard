using System.Security.Cryptography;
using System.Text;

namespace InfraGate.AuditOutbox;

public static class AuditOutboxConventions
{
    // Unique sentinel for the two-argument pg_advisory_xact_lock category.
    // Chosen to avoid collision with other advisory lock callers in the same database.
    public const int LockCategory = unchecked((int)0xA0D17011);

    public static int StreamLockKey(string schemaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schemaName));
        return BitConverter.ToInt32(hash, 0);
    }

    // Builds the canonical input object for hash computation.
    // Includes all audit-relevant columns: universal top-level columns + per-stream correlation columns.
    // The caller is responsible for serializing this to canonical JSON (via CanonicalJson.Serialize).
    public static Dictionary<string, object?> BuildCanonicalInputObject(AuditOutboxRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ColumnNames.EventName] = row.EventName,
            [ColumnNames.OccurredAtUtc] = row.OccurredAtUtc,
            [ColumnNames.ActorSubject] = row.ActorSubject,
            [ColumnNames.ActorClientId] = row.ActorClientId,
            [ColumnNames.Outcome] = row.Outcome,
            [ColumnNames.Reason] = row.Reason,
            [ColumnNames.PayloadJsonText] = row.PayloadJsonText,
        };

        if (row.CorrelationColumns is not null)
        {
            foreach (var (key, value) in row.CorrelationColumns)
            {
                dict[key] = value;
            }
        }

        return dict;
    }

    public static class Streams
    {
        public const string Approvals = "approvals";
        public const string Observer = "observer";
        public const string Planner = "planner";
    }

    public static class ColumnNames
    {
        public const string AuditSequence = "audit_sequence";
        public const string EventName = "event_name";
        public const string OccurredAtUtc = "occurred_at_utc";
        public const string ActorSubject = "actor_subject";
        public const string ActorClientId = "actor_client_id";
        public const string Outcome = "outcome";
        public const string Reason = "reason";
        public const string PreviousEventHash = "previous_event_hash";
        public const string EventHash = "event_hash";
        public const string PayloadJsonText = "payload_json_text";
        public const string PublishedAtUtc = "published_at_utc";
        public const string PublishAttempts = "publish_attempts";
        public const string LastPublishError = "last_publish_error";
    }

    public static class CorrelationColumnNames
    {
        public const string PlanId = "plan_id";
        public const string AnomalyId = "anomaly_id";
        public const string ChallengeId = "challenge_id";
        public const string GrantId = "grant_id";
        public const string ExecutionAttemptId = "execution_attempt_id";
        public const string ProposalId = "proposal_id";
        public const string CycleId = "cycle_id";
        public const string DedupeKey = "dedupe_key";
    }
}
