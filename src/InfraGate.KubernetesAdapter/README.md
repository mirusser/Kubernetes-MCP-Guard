# InfraGate.KubernetesAdapter

`InfraGate.KubernetesAdapter` owns Kubernetes-specific plan building, review evidence, policy checks, freshness checks, and approval-bound execution for the generic approval flow.

## Contents

- `KubernetesPlanPayload.cs` models the Kubernetes mutation intent and review evidence stored inside a generic approval envelope.
- `KubernetesApprovalAdapter.cs` creates Kubernetes envelopes and decodes generic envelopes back into Kubernetes review/apply plans.
- `KubernetesPlanBuilder.cs` implements `IDomainPlanBuilder` by calling downstream evidence tools, building the adapter payload, and returning the target namespace for generic audit storage.
- `KubernetesPlanExecutor.cs` implements `IDomainPlanExecutor` by decoding the envelope, checking live drift where diff evidence exists, repeating pre-execution dry-runs, re-checking policy for apply manifests, and calling raw downstream mutation tools.
- `K8sObjectRef.cs`, dry-run, diff, and policy-finding records define Kubernetes evidence rendered during review and consumed during apply.
- `Policy/` contains the Kubernetes manifest policy validator and rule documentation.

## Boundaries

The adapter depends on `InfraGate.Approvals` for the generic envelope and plan seams. `InfraGate.Approvals` must not depend on this project. `InfraGate.McpGateway` composes this adapter in `Program.cs`; generic gateway code should not contain Kubernetes tool names, argument names, or policy logic.
