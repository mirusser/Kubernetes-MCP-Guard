# InfraGate.KubernetesAdapter

`InfraGate.KubernetesAdapter` owns the Kubernetes-specific approval payload and evidence records used by the generic approval flow.

## Contents

- `KubernetesPlanPayload.cs` models the Kubernetes mutation intent and review evidence stored inside a generic approval envelope.
- `KubernetesApprovalAdapter.cs` creates Kubernetes envelopes and decodes generic envelopes back into Kubernetes review/apply plans.
- `K8sObjectRef.cs`, dry-run, diff, and policy-finding records define Kubernetes evidence rendered during review and consumed during apply.

## Boundaries

The adapter depends on `InfraGate.Approvals` for the generic envelope type. `InfraGate.Approvals` must not depend on this project.
