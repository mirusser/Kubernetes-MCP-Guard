# Implementation Plan: Decouple Gateway from Kubernetes Adapter Types

## Overview
Replace the gateway's direct dependency on `InfraGate.KubernetesAdapter` concrete types (`KubernetesPlan`, `K8sPlanDiff`, etc.) with two interfaces defined in `InfraGate.Approvals`. The gateway operates on `IPlanReview`/`IPlanReviewAdapter`; the Kubernetes adapter implements them. The gateway keeps its project reference to the adapter for DI registration (composition root), but `GatewayApprovalService.cs` and `GatewayApprovalEndpoints.cs` drop all Kubernetes-specific `using` statements.

## Architecture Decisions

- **Interfaces live in `InfraGate.Approvals`** (the Generic Approval Core). This follows ADR-0001's separation: the core defines the contract; adapters implement it.
- **Rendering moves to the adapter**. HTML rendering for adapter-specific evidence (objects, manifest, policy findings, dry-run results, diffs) lives in `IPlanReview.RenderReviewContent()`. The gateway keeps the HTML shell, generic plan summary (reads from `PlanEnvelope` + `ApprovalChallenge`), and approve/deny actions.
- **MCP message formatting moves to the adapter**. `IPlanReview.RenderApprovalRequiredMessage()` produces the adapter-specific content; the gateway wraps it with the approval URL and instructions.
- **Readiness check becomes `HasReviewEvidence`**. Replaces `plan.DryRun != null && plan.Diffs.Length > 0` in the gateway with an adapter-owned boolean.
- **Deny-policy gate becomes `CanBeApproved`**. Replaces `plan.PolicyFindings.Any(f => f.Severity == "Deny")` in rendering.
- **Static adapter class becomes injectable.** `KubernetesApprovalAdapter` (currently a static class) gains instance-based `IPlanReviewAdapter` implementation, registered via DI.

## Task List

### Phase 1: Foundation — Interfaces + Adapter Implementation

**Task 1: Create `IPlanReviewAdapter` and `IPlanReview` interfaces**

Acceptance: Two interfaces in `InfraGate.Approvals`. Builds without errors.

**Task 2: Implement interfaces on Kubernetes types**

Acceptance: `KubernetesPlan` implements `IPlanReview`. `KubernetesApprovalAdapter` gains an `IPlanReviewAdapter` implementation (non-static). All existing tests still pass.

**Task 3: Write adapter interface-compliance tests**

Acceptance: New tests verify `KubernetesPlan` correctly reports `HasReviewEvidence`, `CanBeApproved`, and renders HTML/MCP messages correctly.

### Phase 2: Gateway Service Layer

**Task 4: Register `IPlanReviewAdapter` in DI**

**Task 5: Rewrite `GatewayApprovalService` to use `IPlanReviewAdapter`/`IPlanReview`**

**Task 6: Update `GatewayApprovalServiceTests`**

### Phase 3: Gateway Rendering Layer

**Task 7: Rewrite `GatewayApprovalEndpoints` rendering — move to adapter**

**Task 8: Update rendering tests**

### Phase 4: Cleanup

**Task 9: Remove adapter imports from gateway**

## Open Questions
- Rendering methods move fully to the adapter. No internal backward-compatibility in the gateway.
