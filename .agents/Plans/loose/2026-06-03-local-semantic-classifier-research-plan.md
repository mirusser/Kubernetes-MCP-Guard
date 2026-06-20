# Research Plan: Local Semantic Classifier for Model-Visible Content

> Status: research gate required (2026-06-03).
>
> Extracted from Phase D of `2026-06-02-agent-security-hardening-vertical-slice.md`.
> This is deliberately **not** an implementation plan until candidate maintenance, licensing,
> provenance, and repo-specific measurements are reviewed.

## Objective

Find whether InfraGate should add a local semantic classifier sidecar behind
`IModelVisibleContentGuard`, and if so, select an operationally acceptable candidate.

The classifier must run locally/offline, require no paid API, keep Observer and Planner images
.NET-only, and fail closed in Production when unavailable.

## Current Decision

Do not implement `InfraGate.AgentGuardrails.LocalClassifier` yet.

**F-03 follow-up (2026-06-20):** the prompt-injection mitigation deliberately ships without a semantic classifier. The current production boundary is structural `model_visible_tool_result` output isolation, deterministic scanner hardening, and model-visible guard enforcement. This research gate remains open; no candidate has passed the maintenance, licensing, provenance, runtime-fit, and repo-corpus bakeoff gates below.

The original candidates, Protect AI LLM Guard and InjecGuard/PIGuard, remain useful baselines, but
their maintenance posture is not assumed acceptable. Candidate freshness, release cadence, license,
model provenance, dependency risk, and container/runtime fit are hard gates before production wiring.

## Task List

### Task D1: Refresh Candidate Shortlist

**Description:** Re-evaluate the original two candidates and search for newer local/offline prompt-injection
classifiers. Treat stale or research-only projects as benchmarks unless they pass the operational screen.

**Acceptance criteria:**
- [ ] Each candidate has repository URL, latest release/commit date, license, model license, runtime shape, and dependency summary.
- [ ] Candidates that require cloud APIs, paid services, or unreviewable model artifacts are rejected.
- [ ] At least one maintained local/offline candidate is identified, or the plan records that no suitable candidate exists.

**Verification:**
- [ ] Human review of the shortlist table before any sidecar work.

**Dependencies:** None

**Files likely touched:**
- this plan
- optional notes under `tools/guardrail-eval/**`

**Estimated scope:** Small

### Task D2: Build Disposable Bakeoff Harness

**Description:** Run the adversarial corpus and clean Kubernetes samples against shortlisted candidates.
Measure recall by attack tag, clean false-positive rate, p50/p95 latency, warm-up time, memory, CPU,
image size, model source, model revision, checksum, and license.

**Acceptance criteria:**
- [ ] Results distinguish lexical-only, semantic paraphrase, multilingual, encoded, and split-field cases.
- [ ] The harness does not commit model weights or generated caches.
- [ ] Measurements are reproducible from documented commands.

**Verification:**
- [ ] Run the corpus evaluation against every shortlisted candidate.
- [ ] Review generated comparison table with a human.

**Dependencies:** Task D1

**Files likely touched:**
- `tools/guardrail-eval/**`
- this plan

**Estimated scope:** Medium

### Task D3: Select or Reject Runtime Classifier

**Description:** Decide whether any candidate is good enough to become a production sidecar dependency.
No selection is acceptable if maintenance, licensing, recall, false-positive rate, latency, or operational
fit is weak.

**Acceptance criteria:**
- [ ] Selected candidate rationale, rejected candidate rationale, model artifact source, revision, checksum, and license are recorded.
- [ ] Production outage policy is confirmed as fail-closed for model ingestion.
- [ ] Quarantine retention policy is confirmed as metadata plus digest only by default.

**Verification:**
- [ ] Human approval before adapter implementation starts.

**Dependencies:** Task D2

**Files likely touched:**
- this plan
- ADR update if the decision changes

**Estimated scope:** Small

### Task D4: Implement Adapter Only After Selection

**Description:** If a candidate passes D3, add `InfraGate.AgentGuardrails.LocalClassifier` as an HTTP adapter
behind `IModelVisibleContentGuard`. Enforce request-size bounds, timeout, cancellation, malformed response
handling, unavailable-sidecar behavior, classifier-version capture, and latency metrics.

**Acceptance criteria:**
- [ ] Adapter implements only `IModelVisibleContentGuard`.
- [ ] Timeout, cancellation, malformed response, unavailable sidecar, and oversized input behavior are tested.
- [ ] Production fails closed after configured deterministic fallback.
- [ ] Development degraded mode is explicit, metered, and opt-in.

**Verification:**
- [ ] `dotnet test tests/InfraGate.AgentGuardrails.LocalClassifier.Tests/InfraGate.AgentGuardrails.LocalClassifier.Tests.csproj`
- [ ] Integration tests run against the actual local sidecar container.

**Dependencies:** Task D3

**Files likely touched:**
- new `src/InfraGate.AgentGuardrails.LocalClassifier/**`
- new `tests/InfraGate.AgentGuardrails.LocalClassifier.Tests/**`
- `InfraGate.slnx`

**Estimated scope:** Medium

### Task D5: Package and Opt In

**Description:** Package the selected classifier as a separately versioned local container and add opt-in
Compose/Run Profile wiring. Observer and Planner call only the adapter base URL.

**Acceptance criteria:**
- [ ] Sidecar image is pinned by digest or immutable tag.
- [ ] Model revision and checksum are pinned.
- [ ] Compose health check and resource limits exist.
- [ ] Observer/Planner images do not contain model weights.
- [ ] Run Profile validation catches semantic-classifier-enabled profiles without a base URL.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] Local Compose smoke: stop the sidecar and verify degraded/fail-closed behavior.

**Dependencies:** Task D4

**Files likely touched:**
- `deploy/run-profiles.yaml`
- Compose files under `deploy/**`
- sidecar packaging under `tools/guardrail-classifier/**`
- RunProfiles schema/rendering/tests

**Estimated scope:** Medium

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---:|---|
| Stale classifier project | High | Treat as benchmark only unless maintenance and release posture are acceptable. |
| Weak recall on repo-specific attacks | High | Require corpus bakeoff before selection. |
| High false positives on Kubernetes diagnostics | Medium | Include clean Kubernetes samples and require human review. |
| Unclear model license or provenance | High | Reject candidate until source, revision, checksum, and license are recorded. |
| Sidecar outage weakens Production safety | High | Fail closed for model ingestion in Production. |

## Open Questions

- Which maintained local/offline candidates should be added beyond the original two baselines?
- What minimum recall and false-positive thresholds are acceptable for production use?
- Are CPU-only latency and memory limits sufficient for local deployments?
