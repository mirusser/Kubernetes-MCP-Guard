# InfraGate.KubernetesAdapter

`InfraGate.KubernetesAdapter` owns Kubernetes-specific plan building, review evidence, policy checks, freshness checks, and approval-bound execution for the generic approval flow.

**Owns:** Kubernetes-specific intent, evidence, policy, freshness, execution

## Contents

- `PlanBuilding/KubernetesPlanPayload.cs` models the Kubernetes mutation intent and review evidence stored inside a generic approval envelope.
- `Approval/KubernetesApprovalAdapter.cs` creates Kubernetes envelopes, derives evidence artifact summaries for Review Digest binding, and decodes generic envelopes back into Kubernetes review/apply plans with adapter-owned failure reason codes.
- `PlanBuilding/KubernetesPlanBuilder.cs` implements `IDomainPlanBuilder` as a router from mutation tool names to operation-specific `IOperationPlanBuilder` implementations. `ApplyManifestBuilder`, `DeleteManifestBuilder`, `ScaleDeploymentBuilder`, `RestartDeploymentBuilder`, and `SetDeploymentImageBuilder` apply parameter-level Kubernetes policy checks, call shared evidence services, build adapter payloads, return target namespaces for generic audit storage, and tag failed branches with Kubernetes reason codes.
- `Execution/KubernetesPlanExecutor.cs` implements `IDomainPlanExecutor` by separating adapter-owned pre-execution checks from raw downstream mutation calls. It uses `OperationDispatchMap` for operation-to-tool dispatch, publishes `pre_execution.checked` after successful adapter checks, and publishes `execution.started` immediately before mutation dispatch; blocked checks return stable reason codes for drift, policy, dry-run, decode, and unsupported-operation cases.
- `Evidence/` contains `IKubernetesEvidenceService`, `KubernetesEvidenceService`, dry-run, diff, and policy-finding records that define Kubernetes evidence rendered during review and consumed during apply.
- `KubernetesAuditHelper.cs` centralizes adapter audit event construction for dry-run, diff, drift, and policy-denial outcomes.
- `Policy/` contains the Kubernetes manifest policy validator and rule documentation.

## Boundaries

The adapter depends on `InfraGate.Approvals` for the generic envelope and plan seams. `InfraGate.Approvals` must not depend on this project. `InfraGate.McpGateway` composes this adapter in `Program.cs`; generic gateway code should not contain Kubernetes tool names, argument names, or policy logic.

Review HTML is rendered by `InfraGate.ApprovalUi` Razor components, not by this adapter. The `IPlanReview.Description` and `IPlanReview.Targets` properties (mapped from the Kubernetes payload) supply content for those components. Tests should use the semantic `data-section`, `data-field`, and `data-action` attributes provided by the ApprovalUi components rather than heading text or CSS classes when locating sections.
