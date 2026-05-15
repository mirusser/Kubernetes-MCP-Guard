# ADR 0001 First Slice: Generic Core And Kubernetes Adapter

## Summary

Refactor the current approval flow so generic approval lifecycle code no longer depends on Kubernetes plan DTOs. Preserve current user-facing gateway behavior while introducing a real Kubernetes Adapter Module, a generic stored Plan Envelope shape, and explicit requester propagation from gateway identity into mutation plan creation.

This slice does not implement ADR 0002's two-digest model yet. The current stored plan hash remains the execution-binding value for approval/apply consistency.

## Key Changes

- Add a generic approval envelope model in `InfraGate.Approvals` with:
  - opaque plan identifier
  - requester subject and authentication type
  - adapter id, fixed to `kubernetes` for this slice
  - operation name
  - created metadata matching current behavior
  - generic payload JSON
  - existing single plan hash
- Move Kubernetes plan DTOs and evidence concepts out of `InfraGate.Approvals` into a new `InfraGate.KubernetesAdapter` project.
- Make the Kubernetes Adapter own:
  - building the Kubernetes mutation payload from request inputs
  - decoding envelope payloads for apply/review
  - Kubernetes evidence types: objects, dry-run result, diff, policy findings
  - adapter-id validation and clear refusal for non-Kubernetes envelopes
- Update `ApprovalStore` so it persists and returns generic envelopes, not `K8sPlan`.
- Keep challenge lifecycle, approval status, audit conventions, and hash comparison inside the Generic Approval Core.
- Update gateway review/apply code so Kubernetes-specific rendering goes through the Kubernetes Adapter instead of making the generic approval Module understand Kubernetes DTOs.
- Propagate requester identity from the gateway into downstream mutation-plan creation.
- Add explicit requester parameters to direct stdio mutation request tools and reject mutation plan creation when requester subject is missing.

## Implementation Tasks

1. Create the Kubernetes Adapter project and move Kubernetes DTOs there.
   - Add project reference from MCP server and gateway where Kubernetes-specific behavior remains needed.
   - Keep DTO wire names compatible unless a test proves the old shape is no longer required.
   - Acceptance: `InfraGate.Approvals` no longer references Kubernetes DTO types.

2. Introduce the generic envelope storage Interface.
   - Replace `ApprovalStore` methods that accept/return `K8sPlan` with envelope-based methods.
   - Store adapter payload as JSON, not as a typed Kubernetes object.
   - Reject legacy raw `K8sPlan` pending files with a clear error; no migration in this slice.
   - Acceptance: approval storage tests prove envelope round-trip and legacy refusal.

3. Adapt Kubernetes request/apply flow.
   - `K8sManager.Requests` builds a Kubernetes payload through the adapter, wraps it in a generic envelope, and stores it.
   - `K8sManager.Apply` loads the envelope, validates adapter id/hash/status, decodes Kubernetes payload, then executes existing Kubernetes apply behavior.
   - Acceptance: existing request/apply behavior is preserved through the new envelope path.

4. Propagate requester identity.
   - Gateway mutation request tools inject authenticated requester subject and auth type into downstream stdio calls.
   - Direct stdio mutation request tools require requester subject and fail closed when it is absent.
   - Approval checks continue to reject requester/approver mismatch according to existing same-subject policy.
   - Acceptance: tests cover gateway-created requester binding and direct stdio missing-requester refusal.

5. Move Kubernetes review presentation behind an adapter seam.
   - Gateway remains the review surface host.
   - Kubernetes Adapter supplies the decoded review model needed for current approval UI rendering.
   - Generic approval code must not inspect Kubernetes evidence fields.
   - Acceptance: approval page still renders manifest, objects, dry-run, diffs, and policy findings from an envelope-backed Kubernetes plan.

6. Update focused documentation.
   - Refresh project READMEs only where project ownership changes.
   - Note that ADR 0001 first slice creates the adapter/core separation while ADR 0002 digest separation remains pending.
   - Acceptance: docs do not claim two-digest support exists in code.

## Test Plan

- Run approval storage unit tests for envelope persistence, hash stability, approved/pending lookup, and legacy raw-plan refusal.
- Run Kubernetes server tests for request-plan creation, missing requester rejection, adapter id, operation, and apply from envelope.
- Run gateway tests for requester injection, approval requester/approver checks, approval review rendering, and unchanged public gateway tool behavior.
- Add one integration-style gateway approval flow test proving request, challenge, approval, and apply still work with the new stored envelope.
- Final verification:
  - `dotnet build`
  - targeted approval, gateway, and MCP server test projects

## Assumptions

- No durable `ApprovalGrant` type is introduced in this slice.
- No ADR 0002 `IntentDigest` / `ReviewDigest` split is introduced in this slice.
- No migration is required for old pending approval files.
- Only the Kubernetes Adapter exists; unknown adapter ids are rejected clearly.
- Gateway public MCP tool names and user-facing approval behavior remain unchanged.
