# ADR-0012: Hybrid Severity for the Anomaly Observer — LLM Proposes, Rules Win

**Date:** 2026-05-23
**Status:** Accepted

---

## Context

The Anomaly Observer is an LLM-driven agent that emits `AnomalyReport` records, each carrying a `Severity` of `High`, `Medium`, or `Low`. There are three viable ways to assign that `Severity`:

- **LLM-assigned.** The LLM reads the snapshot and picks `Severity` per the rules in the system prompt. Flexible — the LLM can take into account context the rules table doesn't list — but non-deterministic. Identical snapshots can produce different `Severity` across runs. Hard to write meaningful unit tests against.
- **Code-derived.** Code applies the documented Severity rules over the snapshot. The LLM's role is detection + summary + remediation hint, not classification. Deterministic, auditable, fully testable — but loses any nuance the LLM might catch in cases the rules table doesn't anticipate.
- **Hybrid.** The LLM proposes a `Severity`, code re-classifies the same evidence against the rules table, and code wins on conflict.

Determinism matters here. InfraGate's CONTEXT.md is built around precise, reproducible decisions — `Intent Digest`, `Review Digest`, `Canonicalization`, `Pre-Execution Gate`. A non-deterministic Severity classification clashes with that culture. At the same time, the whole point of choosing an LLM-driven observer (not a rule engine — see deliberately rejected "pure code" path) is that the LLM should add value beyond mechanical rules.

## Decision

The Observer's emitted `Severity` is **rules-derived**. The LLM still proposes a `Severity` value as part of its structured output. The cycle runner re-classifies the same evidence against the deterministic Severity rules table and uses the rules-derived value in the emitted `AnomalyReport`. When the LLM-proposed value differs from the rules-derived value, the disagreement is logged at `Warning` level and counted via the metric `infragate.observer.severity.disagreement` tagged with both values.

The rules table (`High`: Service has 0 ready endpoints / Deployment `availableReplicas == 0` while `spec.replicas > 0` / all pods of a workload in `CrashLoopBackOff` or `ImagePullBackOff`; `Medium`: partial Deployment unavailability / single pod failure with healthy siblings / sustained `BackOff` events; `Low`: one-off Warning events without ongoing impact / single restart since last cycle / Pending pod within scheduling grace) lives in code as `ISeverityClassifier` and is documented identically in the embedded `ObserverSystemPrompt.md`.

## Consequences

- Tests assert exact `Severity` for fixture snapshots without flake.
- The LLM still earns its keep on detection (which signals are anomalies), summary prose, evidence selection, and `RemediationHint`. Severity is the one field it does not control.
- The disagreement counter is a useful operational signal:
  - If it stays near zero forever, the LLM is rubber-stamping rules and the LLM-side `Severity` step can be dropped in a future revision to save tokens.
  - If it climbs, either the rules table is wrong or the LLM is wrong — either way the divergence is worth investigating.
- The `ObserverSystemPrompt.md` and the `ISeverityClassifier` implementation must stay in sync. Drift between them weakens the disagreement signal. Both should be reviewed together.
- This decision is deliberately the opposite of how the `RemediationHint` field works (LLM-authoritative, code does not validate beyond shape). Severity is contractual and observable; remediation is advisory.
