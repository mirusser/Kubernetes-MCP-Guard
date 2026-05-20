// Audit-payload records for plan-anchored events written to audit.jsonl.
//
// Why one file with multiple records: this is a deliberate, documented deviation
// from .agents/skills/code-standards/SKILL.md's "one meaningful top-level type per
// file". These records together define a single cohesive schema; they evolve as
// one unit, are referenced together by audit consumers, and splitting them into 9
// files would obscure the schema rather than clarify it.
//
// Why marker interface, not abstract base record: positional record inheritance
// makes the base's positional parameters serialise first, which would reorder
// every JSON payload (e.g. execution.blocked dry-run payloads emit "phase"
// first). The
// IPlanAuditPayload interface enforces the canonical "planId" field's presence at
// compile time without dictating serialisation order. Field order on disk stays
// identical to today's anonymous-type output.
//
// Why "planId" via positional parameter name: System.Text.Json with
// JsonSerializerDefaults.Web (see ApprovalStore.jsonOptions) lowercases the first
// letter, so the C# parameter "PlanId" serialises as "planId". Renaming any
// parameter here is a breaking schema change — AuditPayloadsTests pins the
// canonical field set per payload.

using InfraGate.Approvals;
using System.Text.Json;

namespace InfraGate.Approvals.AuditPayloads;

public interface IPlanAuditPayload
{
    string PlanId { get; }
}

public sealed record class PlanRequestedPayload(
    string PlanId,
    string Operation,
    string Namespace,
    string Hash,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest) : IPlanAuditPayload;

public sealed record class PreExecutionGrantValidatedPayload(
    string PlanId,
    string GrantId,
    string SourceChallengeId,
    string RequesterSubject,
    string ApproverSubject,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest,
    ApprovalPolicy ApprovalPolicy,
    ExecutionReusePolicy ExecutionReusePolicy,
    DateTimeOffset ExpiresAtUtc) : IPlanAuditPayload;

public sealed record class PreExecutionCheckedPayload(
    string PlanId,
    string Operation,
    string AdapterId,
    JsonElement AdapterPayload) : IPlanAuditPayload;

public sealed record class ExecutionStartedPayload(
    string PlanId,
    string Operation,
    string AdapterId,
    JsonElement AdapterPayload) : IPlanAuditPayload;

public sealed record class ApprovalGrantIssuedPayload(
    string PlanId,
    string GrantId,
    string SourceChallengeId,
    string RequesterSubject,
    string ApproverSubject,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest,
    DateTimeOffset ExpiresAtUtc) : IPlanAuditPayload;

public sealed record class PlanAppliedPayload(
    string PlanId,
    string Operation,
    string Namespace,
    string Hash) : IPlanAuditPayload;

public sealed record class ApplyDeniedPayload(
    string PlanId,
    string Message) : IPlanAuditPayload;

public sealed record class ApplyFailedPayload(
    string PlanId,
    string Operation,
    string Message) : IPlanAuditPayload;

public sealed record class ApplyDriftDetectedPayload(
    string PlanId,
    string Operation,
    string Namespace,
    string Message) : IPlanAuditPayload;

public sealed record class DryRunFailedPayload(
    string Phase,
    string PlanId,
    string Operation,
    string Namespace,
    string[] Objects,
    string Message) : IPlanAuditPayload;

public sealed record class DiffFailedPayload(
    string PlanId,
    string Operation,
    string Namespace,
    string[] Objects,
    string Message) : IPlanAuditPayload;
