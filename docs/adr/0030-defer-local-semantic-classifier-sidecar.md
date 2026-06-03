# 30. Defer Local Semantic Classifier Sidecar Pending Research

Date: 2026-06-03

## Status

Accepted

## Context

The agent-security hardening slice added a model-visible content guard seam and deterministic guard
layers. The next proposed layer was a local semantic classifier sidecar for prompt-injection detection.

The original Phase D candidates, Protect AI LLM Guard and InjecGuard/PIGuard, are not enough to justify
production wiring by plan text alone. A semantic classifier would become a security-relevant runtime
dependency, so maintenance posture, model provenance, license terms, operational packaging, latency,
false positives, and repo-specific recall matter as much as benchmark claims.

## Decision

Do not implement the local semantic classifier adapter or sidecar wiring in the current vertical slice.

Phase D is extracted into `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md`.
That plan must refresh candidates, run a corpus bakeoff, record model source/revision/checksum/license,
and get human review before implementation starts.

Existing controls remain the active safety boundary for now:
- gateway scanner and response sanitization;
- deterministic AGT-backed model-visible guard seam;
- tool-call guardrails;
- deterministic approval, digest, freshness, and execution gates.

## Consequences

- Production does not depend on a stale or unreviewed classifier project.
- The sidecar adapter, packaging, and run-profile wiring remain intentionally absent.
- Future work must prove that a candidate is maintained, local/offline, license-compatible, and effective
  against InfraGate's corpus before becoming a runtime dependency.

## References

- `.agents/Plans/loose/2026-06-02-agent-security-hardening-vertical-slice.md`
- `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md`
