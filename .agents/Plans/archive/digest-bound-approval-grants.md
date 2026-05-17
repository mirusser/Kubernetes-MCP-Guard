# Implementation Plan: Digest-Bound Approval Grants

## Summary
Implement the next ADR-0001 slice as a full digest-binding and Approval Grant migration. The Generic Approval Core will bind approval to `IntentDigest` and `ReviewDigest`, issue durable `ApprovalGrant` records, enforce a 1-hour Plan Validity Window, and reject legacy pending plans that lack the new digest fields. Public MCP tool names and arguments stay unchanged.

## Key Interface Changes
- Extend `PlanEnvelope` with `profile`, `validFromUtc`, `validUntilUtc`, `approvalPolicy`, `executionReusePolicy`, `intentDigest`, and `reviewDigest`.
- Add generic core records: `ApprovalDigest`, `PlanValidityWindow`, `ApprovalPolicy`, `ExecutionReusePolicy`, `ChallengeOutcome`, and `ApprovalGrant`.
- Replace approved `.sha256` authorization with `grants/<planId>.json`; old approved hash files do not authorize execution.
- `ApprovalChallenge` stores expected intent/review digests and records a separate `ChallengeOutcome`; terminal challenge outcome no longer doubles as execution authorization.
- Kubernetes adapter computes the adapter-owned `IntentDigest`; Generic Approval Core computes and verifies the profile-owned `ReviewDigest`.

## Implementation Tasks
1. **Digest primitives and canonical JSON**
   Acceptance: deterministic SHA-256 digests are stable across dictionary/property ordering, declare algorithm/canonicalization, and compare by value.
   Verification: new `ApprovalDigestTests`.

2. **Envelope digest fields and validity window**
   Acceptance: new Kubernetes pending plans include both digests and a 1-hour validity window; pending envelopes without digests are rejected with a re-request message.
   Verification: `ApprovalStoreTests` and `K8sManagerRequestTests`.

3. **Kubernetes intent canonicalization**
   Acceptance: intent digest covers executable mutation meaning only: operation, namespace, object refs, manifest or operation parameters; review evidence changes do not change intent digest.
   Verification: focused Kubernetes adapter tests plus request-plan round trip.

4. **Review digest canonicalization**
   Acceptance: review digest covers envelope metadata, requester, approval/reuse policy, validity window, intent digest, and full review payload including dry-run, diffs, policy findings, and redaction-visible review content.
   Verification: changing a diff, dry-run result, requester, or policy changes review digest and blocks stale approval.

5. **Challenge Outcome split**
   Acceptance: challenge approval, denial, expiry, and rejection write `ChallengeOutcome`; denied/expired/rejected challenges never create an Approval Grant.
   Verification: `GatewayApprovalServiceTests` and `ApprovalChallengeStoreTests`.

6. **Approval Grant issuance**
   Acceptance: approving a valid same-subject challenge writes one grant bound to plan id, requester, approver, source challenge id, both digests, policy, single-execution reuse, issued time, and `expiresAtUtc = plan.validUntilUtc`.
   Verification: approval service tests assert grant exists and approved hash file is not used.

7. **Pre-execution grant gate**
   Acceptance: `apply_approved_plan` loads the grant, validates grant expiry, plan validity, digest matches, same-subject binding already established by the gateway, and single-execution marker before Kubernetes mutation.
   Verification: apply tests for happy path, expired grant/window, tampered pending plan, missing grant, legacy approved hash, and already-applied refusal.

8. **Review surface, audit, and docs**
   Acceptance: browser review and approval-required MCP message show Intent Digest and Review Digest; approval success shows Grant ID; audit payloads include digests and `grant_issued`; READMEs stop describing approved hash files as authorization.
   Verification: gateway endpoint tests, audit payload tests, focused doc review.

## Test Plan
Use TDD vertically: one behavior test, minimal implementation, repeat. Run:
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- `dotnet test InfraGate.slnx --filter "Category!=Keycloak&Category!=SafetyE2E"`
- Optional after code stabilizes: `INFRA_GATE_RUN_SAFETY_E2E=1 dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj --filter "Category=SafetyE2E"`

## Assumptions
- Same-Subject Approval remains the only implemented approval policy.
- Single-Execution Plan remains the only implemented execution reuse policy.
- Plan Validity Window defaults to 1 hour and is not configurable in this slice.
- Existing pending/challenge/approved files without digest/grant fields are legacy and must be re-requested or re-approved.
- Gateway remains the Review Surface host; Kubernetes remains the only Domain Adapter.
