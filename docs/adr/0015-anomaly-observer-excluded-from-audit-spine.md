# ADR-0015: The Anomaly Observer Is Excluded from the Audit Spine

**Date:** 2026-05-23
**Status:** Accepted

---

## Context

CONTEXT.md defines the **Audit Spine** as the generic lifecycle event sequence required to prove approval-bound execution across **Domain Adapters** — grant validation, adapter pre-execution checks, execution start, blocked execution, failed execution, successful execution. The Spine is the authoritative record of approval-bound activity, and it is published through `IApprovalAuditPublisher` into `ApprovalAuditEvent` streams.

The Anomaly Observer is a non-human MCP client that emits informational `AnomalyReport`s. It does not create `Plan Envelopes`, does not produce `Approval Grants`, and does not call any execution tool — these constraints are stated in CONTEXT.md under the `### Anomaly Observation` Relationships subsection.

Two ways to handle Observer logs and events were considered:

- **Include Observer events in the Audit Spine.** Centralises every machine action in one log stream. Easier "show me everything that happened" queries.
- **Hard-separate Observer events from the Audit Spine.** Observer writes structured logs through the regular Serilog pipeline and emits metrics, but never through `IApprovalAuditPublisher`. The Audit Spine retains its precise approval-lifecycle meaning.

Including the Observer would dilute the Spine's semantics. An auditor inspecting `ApprovalAuditEvent` records expects to see grant validations, pre-execution checks, and execution attempts — not snapshots of cluster health. Mixing the two means every consumer of the Spine must filter Observer noise, and the precise relationship between Spine entries and the approval lifecycle defined in CONTEXT.md weakens.

## Decision

The Anomaly Observer **never writes through `IApprovalAuditPublisher`** and **never emits `ApprovalAuditEvent` records**. The separation is enforced architecturally: the `InfraGate.Observer` project does not reference `InfraGate.Approvals` for audit publishing, and no `IApprovalAuditPublisher` registration appears in the Observer's DI graph.

Observer activity is observable through:

- Structured Serilog events under the `infragate.observer.*` namespace.
- The `InfraGate.Observer` `Meter` exposing counters and histograms.
- `AnomalyReport` batches delivered to registered `IAnomalyHandoffSink` implementations (logging sink, JSON file sink, and any executor sink in v2).

If a future requirement needs a unified view across approval lifecycle and Observer activity, the correct place to compose it is a downstream observability pipeline (Loki, Elastic, etc.) consuming both streams — not by widening the Audit Spine.

## Consequences

- Auditors reading `ApprovalAuditEvent` records continue to see only approval-lifecycle entries. The Spine's precise meaning, as defined in CONTEXT.md, is preserved.
- Observer events are still discoverable in unified log infrastructure via the `infragate.observer.*` event names and the `CycleId` correlation property — the separation is at the application layer, not at the storage layer.
- A unit test asserts that `InfraGate.Observer.csproj` does not reference `InfraGate.Approvals` for the audit-publishing assembly. Project-reference drift is a build break, not a runtime surprise.
- The future executor agent is a different story: when it begins consuming approval grants and calling `execute_approved_plan`, those calls are part of `Approval-Bound Execution` and **do** belong on the Audit Spine. The exclusion is for the Observer specifically, not for every non-human MCP client.
