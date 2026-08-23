# Implementation Plan: Approval-Bound kubernetes-mcp-server Mutation Integration

**Date:** 2026-08-15
**Status:** Proposed; implementation is gated by upstream evidence parity
**Repository state investigated:** feature/mcp-server, clean worktree
**Upstream baseline:** containers/kubernetes-mcp-server v0.0.66

## Goal

Replace the in-repository InfraGate.McpServer Kubernetes execution substrate with a checksum-pinned containers/kubernetes-mcp-server release for broad reads and approval-bound writes, including destructive operations, without exposing raw upstream mutations or weakening InfraGate's Generic Approval Core, Plan Envelope, Approval Grant, digest, pre-execution, authorization, audit, and Kubernetes RBAC guarantees.

The final architecture should use the upstream binary as two separately credentialed child processes:

    MCP clients / agents
            |
            v
    InfraGate HTTP Gateway
      |                |
      | reads          | request/propose/execute plan
      v                v
    upstream viewer    Generic Approval Core
    process            -> Kubernetes Adapter
    viewer RBAC           -> hidden upstream executor process
                            writer/admin RBAC
                                  |
                                  v
                             Kubernetes API

Only the viewer process contributes tools to the public federated catalog. The executor process is not listed or directly dispatchable; the Kubernetes Adapter can call it only after the Generic Approval Core has passed every required gate.

## Restated Request

- Extend the existing optional read-only kubernetes-mcp-server integration to support writes and destructive capabilities.
- Use the current implementation rather than constructing a parallel gateway or approval system.
- Preserve human, out-of-band approval of the exact reviewed mutation.
- Produce a staged, verifiable implementation path, including upstream enablement work when the released server cannot yet meet InfraGate's safety contract.

## Investigation Summary

### Current InfraGate implementation

- The gateway already launches the pinned upstream v0.0.66 binary as an optional, read-only secondary stdio process. It exposes only pods_list_in_namespace, pods_get, and pods_log through a dedicated viewer kubeconfig, exact namespace/tool policy, response bounds, schema/collision checks, restart supervision, and source-isolated health.
- GatewayToolDispatcher builds request_* wrappers only from the primary InfraGate.McpServer destructive catalog. The secondary source is structurally excluded from plan creation and execution.
- InfraGate.KubernetesAdapter owns five current mutation intents: apply, delete, scale, restart, and set-image. Its evidence and execution code expects the private McpServer's structured dry-run, diff, resourceVersion, live-drift, policy, and mutation contracts.
- The Generic Approval Core validates Approval Grants, Plan Validity Window, requester/approver authorization, Intent Digest, Review Digest, and Single-Execution reuse before delegating Kubernetes freshness/domain policy checks to KubernetesPlanExecutor.
- ADR-0021 and ADR-0033 require real-cluster mutation evidence parity and a separate accepted decision before replacing InfraGate.McpServer. docs/mutation-evidence-parity-contract.md currently records a no-go result.

### Upstream v0.0.66 findings

- v0.0.66, released 2026-07-31, is both the repository's pinned version and the latest release as of this investigation. It is Apache-2.0 and pre-1.0, with frequent potentially breaking releases and no LTS/backport line.
- It supports stdio, Streamable HTTP, and legacy SSE. InfraGate should keep stdio child-process isolation for its current shared-ServiceAccount model; public or agent-reachable upstream HTTP endpoints are out of scope.
- The release exposes 52 tools across config, core, helm, kcp, kiali, kubevirt, netobserv, and tekton toolsets. Core writes include pods_delete, pods_exec, pods_run, resources_create_or_update, resources_delete, and resources_scale. Helm and several optional toolsets add more writes.
- resources_create_or_update performs force server-side apply over sequential multi-document YAML. It is not transactional and can leave earlier objects applied if a later object fails.
- No released tool exposes Kubernetes server-side dry-run, admission-mutated prospective objects, semantic diff, or an InfraGate-compatible freshness artifact. Helm install explicitly uses DryRun = false.
- read_only filters on ReadOnlyHint and currently blocks built-in writes. disable_destructive is not a write barrier: additive/execution writes such as pods_run, helm_install, and several Tekton tools have DestructiveHint = false and remain enabled.
- Confirmation rules are MCP elicitation with an allow fallback when elicitation is unavailable. They do not bind a Plan Identifier, digests, actor, grant, expiry, reuse policy, or durable outcome and cannot replace InfraGate approval.
- Generic apply can target arbitrary namespaced or cluster-scoped resources permitted by Kubernetes RBAC. pods_exec is arbitrary non-interactive command execution. Helm accepts mutable chart sources unless a registry allow-list and immutable artifact binding are added.
- Upstream has no durable approval-aware audit outbox, tamper-evident audit chain, external policy callback, or signed grant verification. Its telemetry is supplemental only.

### Current verdict

Directly routing v0.0.66 writes or deleting InfraGate.McpServer is **no-go**. The released binary cannot satisfy the existing evidence-parity contract because it has no genuine dry-run/diff path, and several current operations need safer primitives than force-applying a reconstructed full Deployment.

The plan below first obtains those missing primitives in a released upstream artifact, or stops. It does not permit a compatibility shim to fabricate evidence, reinterpret annotations as authorization, or skip a pre-execution gate.

## Scope Assumptions

1. “Full integration” means the upstream server becomes InfraGate's Kubernetes read and execution substrate; it does not mean raw upstream write tools become public MCP tools.
2. The first production scope is the upstream core toolset plus the existing five InfraGate operations. Helm follows as a separately gated vertical slice. Kiali, KubeVirt, and Tekton mutations remain disabled until each has its own Domain Adapter evidence/policy contract.
3. One upstream process is bound to one cluster and privilege tier. Multi-cluster auto-discovery remains disabled until Plan Envelopes bind a cluster server and CA fingerprint rather than a mutable context name.
4. Namespace-scoped writer RBAC is the normal profile. Cluster-scoped and sensitive-resource writes require an explicit privileged profile and stronger OAuth authorization; cluster-admin is never the default.
5. pods_exec is a distinct, elevated operation because no meaningful dry-run exists. It remains off by default until its exact command/target grant and non-reversible evidence model are separately approved.
6. The current shared Kubernetes ServiceAccount identity is preserved. Per-user Kubernetes identity through upstream HTTP OAuth/token exchange is a separate future architecture decision.
7. InfraGate.McpServer is removed only after the upstream route passes the full conformance and safety suites. Rollback after removal is deployment rollback to the prior release image, not a hidden runtime bypass.

## Success Criteria

- A released, checksum-pinned upstream artifact passes every applicable row of docs/mutation-evidence-parity-contract.md on real Kubernetes and real OAuth/approval infrastructure.
- Every upstream tool is classified by an InfraGate-owned capability manifest containing the exact name, input schema hash, annotations, toolset, risk class, allowed process role, intent codec, evidence strategy, authorization scopes, and output bounds. Unknown or changed tools fail closed.
- No agent or human MCP client can directly call an upstream write. All writes originate from stored, digest-bound Plan Envelopes and are reconstructed from approved data after pre-execution checks.
- The viewer and executor use separate kubeconfigs/ServiceAccounts, single contexts, explicit namespaces, disjoint RBAC, isolated environment variables, fixed TOML, and no public upstream listener.
- Apply, delete, scale, restart, and set-image retain their existing dry-run, diff, resourceVersion/live-drift, policy, audit, and Single-Execution behavior before broader resource operations are enabled.
- Partial upstream execution is never reported as success. Per-step outcomes and the final blocked/failed/succeeded result are persisted through the existing audit outbox.
- Destructive core capabilities are enabled only through explicit Run Profiles and OAuth scopes, with Kubernetes RBAC as an independent hard boundary.
- The final release contains no InfraGate.McpServer runtime, contract-test, Docker publish, solution, CI, or documentation references except historical ADR context.

## Capability Treatment

| Upstream capability | InfraGate treatment |
| --- | --- |
| Bounded core reads | Proxy only through the viewer process after source policy, scope checks, namespace normalization, sanitization, and response bounds. |
| resources_create_or_update / resources_delete / resources_scale | Convert to deterministic Kubernetes Mutation Intents; require genuine plan-only dry-run/diff and pre-execution freshness; call only through the hidden executor. |
| pods_delete / pods_run | Separate intent builders. Bind pod UID/resourceVersion for delete; require deterministic names and dry-run all generated Pod/Service/Route objects for run. |
| pods_exec | Separate elevated, short-lived exact-command grant; bind cluster, namespace, pod UID, container identity, image, argv, timeout, and output cap; default disabled. |
| helm_install / helm_uninstall | Separate Helm intent/evidence slice with allowlisted registries, immutable chart digest, deterministic release name, rendered manifest/hook/CRD evidence, and genuine dry-run. |
| Kiali / KubeVirt / Tekton mutations | Disabled by default. Each needs its own adapter-owned policy, evidence, freshness, failure, and audit slice; upstream annotations are insufficient. |
| configuration_view, node proxy reads, Secrets, broad exports | Separately privileged or denied; never added merely because upstream advertises them. |
| Multi-cluster context arguments | Disabled in the first release. A later slice must bind the API server and CA fingerprint and prevent context remapping between approval and execution. |

## Phase 0: Upstream Enablement and Go/No-Go Proof

### Task 1: Freeze the released capability contract

**Description:** Capture v0.0.66 tools/list output and create the repository-owned classification that distinguishes reads, finite mutations, destructive operations, command execution, external-system writes, and unsupported capabilities. This is an admission contract, not documentation generated from annotations.

**Acceptance criteria:**

- All 52 released tools are classified; no tool inherits permission from ReadOnlyHint or DestructiveHint alone.
- Exact input-schema hashes and the binary version/checksum are recorded, including the fact that disable_destructive still permits some writes.
- Public viewer, hidden executor, elevated-exec, and disabled roles are explicit for every tool.

**Verification:**

- Run a tag-pinned tools/list snapshot test against the installed binary.
- Mutate a fixture tool name, schema, or annotation and prove admission/readiness fails closed.
- Compare the captured release version with scripts/kubernetes-mcp-server.manifest.json.

**Dependencies:** None.

**Files likely touched:**

- scripts/kubernetes-mcp-server.manifest.json
- src/InfraGate.McpGateway/McpTransport/Dispatch/KubernetesMcpServerCapabilityManifest.cs (new)
- tests/InfraGate.McpGateway.Tests/ContractTests/KubernetesMcpServerCapabilityManifestTests.cs (new)
- docs/kubernetes-mcp-server-capability-matrix.md (new)

**Estimated scope:** Medium.

### Task 2: Add genuine plan-only primitives upstream

**Description:** Contribute upstream-first changes, or make an explicit fork-ownership decision, so a released artifact can produce real planning evidence without persisting writes. Do not begin privileged InfraGate routing against an unreleased main commit.

Required upstream behavior:

- Server-side dry-run for generic create/update, delete, and scale, returning structured admission-mutated objects, object identities, UID/resourceVersion where applicable, warnings, and stable failure classification.
- Safe patch primitives or equivalent exact operations for restart and set-image; force-applying a reconstructed full Deployment is not accepted without proof that unrelated fields/ownership cannot change.
- Plan-only Helm rendering/dry-run with immutable chart identity, hooks/CRDs represented, and no release creation.
- Deterministic names for pods_run and any Helm operation before approval.
- Multi-document plan-only calls validate every document without persistence; execution reports per-document outcomes and never implies transactionality.

**Acceptance criteria:**

- The upstream project's tests prove plan-only calls produce zero persisted changes, including failure on later documents.
- Structured outputs are versioned and sufficient to compute InfraGate evidence artifacts without fabricated fields.
- A tagged release and official checksums are available; InfraGate pins that release before continuing.

**Verification:**

- Run upstream unit/integration tests against a disposable cluster.
- Compare live object state before and after every plan-only success and failure.
- Verify restart/set-image touch only the intended fields.

**Dependencies:** Task 1.

**Files likely touched outside this repository:**

- pkg/kubernetes/resources.go
- pkg/kubernetes/pods.go
- pkg/helm/helm.go
- pkg/mcp/testdata/toolsets-*-tools.json
- upstream integration tests and configuration docs

**Estimated scope:** Large, split into upstream PRs by operation family.

### Task 3: Build the real-cluster evidence-parity conformance suite

**Description:** Add a non-skippable candidate suite using the pinned upstream binary, real Kubernetes, real Keycloak/OAuth, the real Gateway, PostgreSQL approval storage, and browser approval flow. Follow the current Safety E2E tier and use no mocking framework.

**Acceptance criteria:**

- Apply, delete, scale, restart, and set-image pass every matrix cell in docs/mutation-evidence-parity-contract.md, including negative paths and exact Audit Spine order.
- Tampered plan/review digests, expired/mismatched grants, stale resourceVersion/live drift, failed pre-execution dry-run, RBAC denial, schema drift, and replay all fail before mutation.
- The suite fails, rather than skips, when the pinned binary, cluster, OAuth, or PostgreSQL prerequisites are absent in its required CI job.

**Verification:**

    rtk test dotnet test tests/InfraGate.KubernetesMcpServer.ConformanceTests/InfraGate.KubernetesMcpServer.ConformanceTests.csproj

Review Kubernetes state and approval audit rows for each operation, not only MCP response text.

**Dependencies:** Task 2.

**Files likely touched:**

- tests/InfraGate.KubernetesMcpServer.ConformanceTests/ (new project)
- tests/InfraGate.Safety.E2E.Tests/Fixtures/
- .github/workflows/integration-tests.yml
- InfraGate.slnx
- docs/mutation-evidence-parity-contract.md

**Estimated scope:** Large, split into one task per operation family.

### Checkpoint A: Replacement gate

- [ ] A specific released artifact passes Task 3 with non-skipped output.
- [ ] The capability manifest matches that exact artifact.
- [ ] A human reviews the evidence-parity report and records go or no-go.
- [ ] If any existing operation lacks genuine evidence or safe execution semantics, stop; do not configure executor credentials or change production routing.

## Phase 1: Architecture and Process Isolation

### Task 4: Record the replacement decision and invariants

**Description:** Add ADR-0034 only after Checkpoint A passes. It should supersede ADR-0033 for the accepted mutation scope without rewriting the historical decision.

**Acceptance criteria:**

- The ADR selects two separately credentialed stdio processes, keeps the executor out of public discovery, and preserves the Generic Approval Core/Kubernetes Adapter ownership split.
- It states which upstream toolsets and privilege tiers are approved, how version/schema/config digests are bound, and why annotations/elicitation are not authority.
- It defines rollout, prior-image rollback, partial-execution semantics, fork ownership if applicable, and the condition for deleting InfraGate.McpServer.

**Verification:**

- Cross-check terminology and ownership against CONTEXT.md, docs/mutation-approval-flow.md, ADR-0001, ADR-0002, ADR-0021, and ADR-0033.
- Run stale-term searches and rtk git diff --check.

**Dependencies:** Checkpoint A.

**Files likely touched:**

- docs/adr/0034-use-kubernetes-mcp-server-as-approval-bound-execution-substrate.md (new)
- docs/mutation-evidence-parity-contract.md
- docs/architecture.md
- CONTEXT.md only if a new canonical term is actually needed

**Estimated scope:** Small.

### Task 5: Generate role-specific upstream configuration and credentials

**Description:** Replace the single read-only secondary profile with explicit diagnostic-viewer and mutation-executor roles, each rendering its own fixed TOML, kubeconfig/context, namespace rules, enabled tools, and RBAC expectations.

**Acceptance criteria:**

- The viewer remains read_only=true with viewer RBAC. The executor enables only the reviewed mutation/evidence tools and has no public listener or catalog membership.
- Both kubeconfigs are single-context and distinct; multi-cluster, drop-in TOML, inherited secrets, dynamic tool expansion, and implicit/wildcard namespaces fail validation.
- Namespace-writer and explicit privileged profiles generate disjoint RBAC/config; cluster-admin is never inherited by default.

**Verification:**

    rtk dotnet run --project src/InfraGate.RunProfiles -- validate
    rtk test dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj

Inspect generated TOML/env files and run Kubernetes auth can-i checks for each ServiceAccount.

**Dependencies:** Task 4.

**Files likely touched:**

- deploy/run-profiles.yaml
- src/InfraGate.RunProfiles/Profiles/KubernetesMcpServerProfile.cs
- src/InfraGate.RunProfiles/Rendering/TomlFileRenderer.cs
- src/InfraGate.RunProfiles/Rendering/EnvFileRenderer.cs
- tests/InfraGate.RunProfiles.Tests/

**Estimated scope:** Medium; split renderer and deployment-RBAC changes if needed.

### Task 6: Isolate and supervise the hidden executor

**Description:** Add an executor-specific process descriptor/client/runner registration that reuses the existing child-process and restart-supervision infrastructure but is never added to GatewayToolDispatcher.ReadOnlySource or DownstreamToolCatalog.

**Acceptance criteria:**

- The gateway launches viewer and executor as independent processes with distinct environments, credentials, lifecycle state, and telemetry.
- Executor readiness is mandatory for write-capable profiles; viewer failure isolation remains defined by the selected Run Profile.
- Public tools/list and direct dispatch cannot resolve any executor tool name, even when upstream annotations or schemas change.

**Verification:**

- DI tests prove exactly one keyed client per role and no executor catalog entry.
- Integration tests kill/restart each process and prove generation isolation plus fail-closed write behavior.
- A direct call to every upstream mutation name returns a Gateway denial without reaching the executor.

**Dependencies:** Tasks 1 and 5.

**Files likely touched:**

- src/InfraGate.McpGateway/Configuration/ConfigurationExtensions.cs
- src/InfraGate.McpGateway/McpTransport/Client/DownstreamProcessDescriptor.cs
- src/InfraGate.McpGateway/McpTransport/Client/DownstreamProcessSupervisor.cs
- src/InfraGate.McpGateway/Endpoints/GatewayReadinessChecker.cs
- tests/InfraGate.McpGateway.Tests/

**Estimated scope:** Medium.

### Task 7: Enforce capability and schema admission at both roles

**Description:** Apply the Task 1 manifest during startup/catalog publication and before every executor call. Bind the accepted binary checksum, tool schema hash, role-specific TOML digest, and cluster fingerprint into adapter-visible execution context.

**Acceptance criteria:**

- Unknown tools, missing tools, schema changes, role changes, version/checksum mismatch, config drift, or cluster fingerprint drift block plan creation/execution with stable reason codes and audit evidence.
- Namespace/context defaults are normalized before intent canonicalization; an empty namespace or implicit current context never changes plan meaning.
- SIGHUP/tool-change notifications cannot expand the accepted surface without a new generation passing admission.

**Verification:**

- Contract tests mutate each bound value and prove fail-closed behavior.
- Restart tests prove a rejected generation does not replace the last admitted viewer generation and never enables an executor generation.

**Dependencies:** Tasks 5 and 6.

**Files likely touched:**

- src/InfraGate.McpGateway/McpTransport/Dispatch/DownstreamToolCatalog.cs
- src/InfraGate.McpGateway/Configuration/KubernetesMcpServerStartupValidator.cs
- src/InfraGate.KubernetesAdapter/KubernetesAdapterConventions.cs
- src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanPayload.cs
- tests/InfraGate.McpGateway.Tests/

**Estimated scope:** Medium.

### Checkpoint B: Isolated substrate

- [ ] Viewer and executor run with separate identities and exact admitted catalogs.
- [ ] No executor tool is visible or directly callable.
- [ ] Version, schema, configuration, namespace, and cluster identity drift fail closed.
- [ ] Existing production mutation routing still points to InfraGate.McpServer.

## Phase 2: Preserve Existing Mutation Semantics

### Task 8: Add upstream evidence/result codecs at the Kubernetes Adapter boundary

**Description:** Translate genuine, versioned upstream plan-only and execution results into Kubernetes Adapter evidence and execution records. Keep Kubernetes tool names and schemas out of the generic gateway and Generic Approval Core.

**Acceptance criteria:**

- Parsing is strict and versioned; missing or malformed evidence blocks plan creation rather than returning an empty/success value.
- Evidence Artifact digests bind admission-mutated objects, semantic diffs, warnings, object identities/versions, policy findings, upstream schema/config identity, and redaction metadata.
- Upstream output is treated as untrusted: bounded, sanitized before model/review visibility, and never copied raw into audit logs.

**Verification:**

- Golden contract tests use real upstream result fixtures captured by Task 3.
- Malformed, oversized, secret-bearing, partial, and unknown-version results fail with stable adapter reason codes.

**Dependencies:** Tasks 2 and 7.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/Evidence/
- src/InfraGate.KubernetesAdapter/Approval/KubernetesApprovalAdapter.cs
- src/InfraGate.KubernetesAdapter/PlanBuilding/KubernetesPlanPayload.cs
- src/InfraGate.McpGateway/Guardrails/SanitizingToolCaller.cs
- tests/InfraGate.KubernetesAdapter.Tests/

**Estimated scope:** Medium.

### Task 9: Route apply and delete through genuine upstream plans

**Description:** Preserve apply/delete manifest policy, dry-run, diff, live-drift, resourceVersion, pre-execution dry-run, review evidence, and audit behavior while changing only the execution substrate.

**Acceptance criteria:**

- Multi-document input is split into deterministic, individually identified plan steps; every step is dry-run before approval and rechecked before execution.
- Force server-side apply ownership changes, pruning/deletion effects, and non-transactional partial-failure risk are explicit in the review snapshot and Review Digest.
- Delete binds UID/resourceVersion preconditions where the API supports them and refuses a changed/recreated target.

**Verification:**

- Run apply/delete builder, executor, policy, contract, and real-cluster conformance tests.
- Force failure on a later document and prove the result is execution.failed/partial with per-step audit, never success.

**Dependencies:** Task 8.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/PlanBuilding/ApplyManifestBuilder.cs
- src/InfraGate.KubernetesAdapter/PlanBuilding/DeleteManifestBuilder.cs
- src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs
- src/InfraGate.KubernetesAdapter/Execution/OperationDispatchMap.cs
- tests/InfraGate.KubernetesAdapter.Tests/

**Estimated scope:** Medium.

### Task 10: Route scale, restart, and set-image through safe upstream operations

**Description:** Map the three narrow Deployment operations to released upstream scale/patch primitives without reconstructing and force-owning unrelated Deployment fields.

**Acceptance criteria:**

- Scale preserves the 0-5 policy and targets the scale subresource with resourceVersion/freshness binding.
- Restart and set-image mutate only their intended fields; set-image policy is rechecked before pre-execution dry-run.
- Existing public request_* and propose_plan operation names, arguments, review content, reason codes, and Audit Spine events remain stable.

**Verification:**

- Compare managedFields and the complete live Deployment before/after; unrelated fields and ownership do not change.
- Run all existing per-operation unit tests plus real-cluster conformance.

**Dependencies:** Task 8.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/PlanBuilding/ScaleDeploymentBuilder.cs
- src/InfraGate.KubernetesAdapter/PlanBuilding/RestartDeploymentBuilder.cs
- src/InfraGate.KubernetesAdapter/PlanBuilding/SetDeploymentImageBuilder.cs
- src/InfraGate.KubernetesAdapter/Execution/OperationDispatchMap.cs
- tests/InfraGate.KubernetesAdapter.Tests/

**Estimated scope:** Medium.

### Task 11: Switch the adapter's hidden execution route

**Description:** After Tasks 9-10 pass, bind the Kubernetes Adapter's IToolCaller/evidence services to the admitted executor client for the five current operations while leaving approval orchestration unchanged.

**Acceptance criteria:**

- Plan creation and execution use only the executor generation admitted for the plan's version/schema/config/cluster bindings.
- Generic gates 1-6 and adapter gates 7-8 remain in their current order; intentional duplicate grant/applied guards remain.
- execution.started is written before the first upstream mutation; one final succeeded, blocked, or failed result is persisted with adapter-owned context.

**Verification:**

    rtk test dotnet test tests/InfraGate.KubernetesAdapter.Tests/InfraGate.KubernetesAdapter.Tests.csproj
    rtk test dotnet test tests/InfraGate.Approvals.Postgres.Tests/InfraGate.Approvals.Postgres.Tests.csproj
    rtk test dotnet test tests/InfraGate.KubernetesMcpServer.ConformanceTests/InfraGate.KubernetesMcpServer.ConformanceTests.csproj

**Dependencies:** Tasks 9 and 10.

**Files likely touched:**

- src/InfraGate.McpGateway/Configuration/ConfigurationExtensions.cs
- src/InfraGate.KubernetesAdapter/KubernetesAdapterServiceCollectionExtensions.cs
- src/InfraGate.KubernetesAdapter/Evidence/KubernetesEvidenceService.cs
- src/InfraGate.KubernetesAdapter/Execution/KubernetesPlanExecutor.cs
- tests/InfraGate.McpGateway.Tests/

**Estimated scope:** Medium.

### Checkpoint C: Existing safety parity

- [ ] All five existing operations pass the old and candidate conformance suites.
- [ ] Review snapshots and Intent/Review Digests are stable and explain substrate/version changes.
- [ ] Direct upstream writes, annotation drift, approval bypass, replay, and stale execution remain impossible.
- [ ] Human review approves enabling the upstream executor outside test environments.

## Phase 3: Expand Destructive Capability

### Task 12: Add generic resource create/update/delete/scale intents

**Description:** Expand beyond Deployment, Service, and ConfigMap using explicit capability and privilege policies, not arbitrary raw tool forwarding.

**Acceptance criteria:**

- Each plan binds exact GVK, scope, cluster fingerprint, namespace/name, normalized desired state, force-SSA behavior, current UID/resourceVersion, admission result, diff, and policy findings.
- Namespace-writer and privileged cluster-scoped authorization are separate OAuth/RBAC checks. Unknown CRDs and cluster-scoped kinds are denied unless explicitly admitted.
- Secret, RBAC, CRD, Namespace, webhook, and other high-impact kinds are separately classified; broad write permission never comes from mcp:tools.write alone.

**Verification:**

- Table-driven tests cover allowed/denied GVKs, namespaced/cluster scope, recreation races, RBAC denial, admission denial, and schema changes.
- Real-cluster tests prove an unapproved or lower-tier identity cannot change any resource.

**Dependencies:** Checkpoint C.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/PlanBuilding/
- src/InfraGate.KubernetesAdapter/Policy/
- src/InfraGate.McpGateway/McpTransport/Dispatch/ToolScopeCatalog.cs
- src/InfraGate.McpGateway.Auth/
- deploy/minikube/ and deployment RBAC manifests

**Estimated scope:** Large; split by ordinary namespaced, sensitive, and cluster-scoped resource tiers.

### Task 13: Handle sensitive and redacted mutation intents

**Description:** Define how Secret-like payloads and other sensitive fields are stored, reviewed, digested, executed, and audited before privileged generic writes may include them.

**Acceptance criteria:**

- Raw secret values never reach model-visible content, logs, email, or audit payloads.
- The Intent Digest still binds exact executable bytes; the Review Digest binds disclosed redaction metadata and evidence digests.
- Pending intent storage is encrypted or uses an immutable external secret reference with integrity binding; plaintext PostgreSQL JSON is not accepted for new secret values.

**Verification:**

- Integration tests inspect MCP responses, approval HTML, logs, audit rows, database rows, and failure messages for seeded canary secrets.
- Tampering with encrypted/reference-bound values blocks grant use.

**Dependencies:** Task 12.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/Approval/
- src/InfraGate.Approvals/Plan/
- src/InfraGate.Approvals.Postgres/
- src/InfraGate.ApprovalUi/
- corresponding PostgreSQL/Testcontainers integration tests

**Estimated scope:** Large; requires a separate security review.

### Task 14: Add pods_delete and deterministic pods_run plans

**Description:** Model pod deletion and run as finite Kubernetes Mutation Intents rather than exposing raw tools.

**Acceptance criteria:**

- Delete binds pod UID/resourceVersion and dry-run evidence and refuses a recreated pod.
- Run assigns deterministic object names during planning, dry-runs every Pod/Service/Route object, records all diffs, and never accepts upstream-generated random identity after approval.
- Partial creation is audited per object and cannot be reported as success.

**Verification:**

- Real-cluster tests cover recreated pods, admission mutation/denial, OpenShift Route variance, service creation failure, and cleanup guidance.

**Dependencies:** Tasks 12 and 13 when sensitive environment values are supported.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/PlanBuilding/
- src/InfraGate.KubernetesAdapter/Execution/
- src/InfraGate.KubernetesAdapter/Policy/
- tests/InfraGate.KubernetesAdapter.Tests/
- tests/InfraGate.KubernetesMcpServer.ConformanceTests/

**Estimated scope:** Medium.

### Task 15: Decide and implement the elevated pods_exec profile

**Description:** Treat non-interactive command execution as a separate high-risk authorization and approval model. Do not group it with ordinary resource writes merely because upstream calls it a core tool.

**Acceptance criteria:**

- An accepted ADR defines whether pods_exec is enabled. If enabled, the plan binds exact argv (not shell text), namespace, pod UID, container/containerID, image digest, cluster fingerprint, timeout, output limit, actor, and short grant expiry.
- Pre-execution rechecks the target identity and authorization. stdin, TTY, interactive sessions, generated shell commands, and implicit containers are rejected.
- The Review Surface clearly states that no dry-run or rollback exists; the operation uses a separately elevated OAuth scope and is default-off.

**Verification:**

- Tests prove argument/target drift, recreated Pods, missing containers, timeout, oversized output, RBAC denial, approval expiry, and direct-call attempts all fail closed.
- Seeded secret output is redacted before model visibility and absent from audit/log storage.

**Dependencies:** Checkpoint C and a human decision.

**Files likely touched:**

- docs/adr/ (separate pods-exec decision)
- src/InfraGate.KubernetesAdapter/PlanBuilding/
- src/InfraGate.KubernetesAdapter/Execution/
- src/InfraGate.McpGateway.Auth/
- tests/InfraGate.KubernetesMcpServer.ConformanceTests/

**Estimated scope:** Large and security-sensitive.

### Task 16: Add Helm as a separate adapter slice

**Description:** Integrate helm_install and helm_uninstall only after immutable artifact and plan-only evidence are available. Optional Kiali, KubeVirt, and Tekton writes stay disabled and follow this same separate-slice rule later.

**Acceptance criteria:**

- Charts come only from allowlisted registries and are bound by OCI/chart digest, version, repository, deterministic release name, namespace, and values digest.
- Review evidence includes rendered manifests, hooks, CRDs, policy findings, existing release state, and uninstall impact; pre-execution re-resolves the immutable artifact and reruns dry-run.
- Helm timeout/partial release outcomes are explicit audit failures; upstream OTel/logs are supplemental.

**Verification:**

- Real Helm integration tests cover mutable tag/repository drift, hook/CRD changes, existing release collision, timeout, RBAC denial, partial failure, uninstall of a changed release, and replay.

**Dependencies:** Task 2's Helm primitives and Checkpoint C.

**Files likely touched:**

- src/InfraGate.KubernetesAdapter/Helm/ (new)
- src/InfraGate.KubernetesAdapter/PlanBuilding/
- src/InfraGate.KubernetesAdapter/Execution/
- src/InfraGate.ApprovalUi/
- tests/InfraGate.KubernetesMcpServer.ConformanceTests/

**Estimated scope:** Large; implement install and uninstall as separate vertical tasks.

### Checkpoint D: Privileged capability review

- [ ] Every enabled capability has its own intent, evidence, freshness, policy, failure, and audit contract.
- [ ] Sensitive, cluster-scoped, exec, and Helm permissions are opt-in and independently authorized.
- [ ] Disabled optional toolsets cannot appear after config reload or upstream upgrade.
- [ ] Security review accepts RBAC, secret handling, non-reversible operations, and partial-failure behavior.

## Phase 4: Production Packaging, Cutover, and Removal

### Task 17: Package and deploy the admitted upstream roles

**Description:** Update the gateway image and Run Profiles to ship only a pinned binary/checksum/configuration for both roles, with explicit persistence, file permissions, and health behavior.

**Acceptance criteria:**

- No latest image/tag or unverified download is used. The release binary, per-platform checksum, tool-schema hash, and fixed configuration are verified at build and startup.
- Kubeconfigs/configs are readable only by the runtime UID as required; viewer and executor credentials cannot be swapped or shared.
- Production readiness fails when the mandatory executor is unavailable or inadmissible for a write-capable profile; health output contains no paths, tokens, arguments, payloads, or raw exceptions.

**Verification:**

    rtk docker build -f deploy/docker/mcp-gateway.Dockerfile .
    rtk dotnet run --project src/InfraGate.RunProfiles -- validate

Run source and published-image smoke paths, process restart tests, and file-permission checks.

**Dependencies:** Checkpoints C and D for the capabilities selected in the release.

**Files likely touched:**

- deploy/docker/mcp-gateway.Dockerfile
- deploy/run-profiles.yaml
- deploy/compose/
- deploy/local-oauth/
- scripts/install-kubernetes-mcp-server.sh and its offline tests

**Estimated scope:** Medium.

### Task 18: Run the complete safety and operational verification

**Description:** Exercise the release candidate through real OAuth, PostgreSQL, Gateway, both upstream processes, and a disposable cluster. No skipped candidate tests count as evidence.

**Acceptance criteria:**

- Existing approval, gateway, adapter, agent, architecture, and safety suites pass with the upstream route.
- New tests cover raw-write denial, catalog/schema drift, process compromise boundaries, annotation changes, wrong privilege tier, cluster/namespace drift, partial failure, output bounds, and every enabled mutation family.
- Observer remains read-only, Planner remains propose-only, and Remediation Executor can execute only approved Plan Identifiers.

**Verification:**

    rtk test dotnet test InfraGate.slnx
    rtk test dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj
    rtk test dotnet test tests/InfraGate.KubernetesMcpServer.ConformanceTests/InfraGate.KubernetesMcpServer.ConformanceTests.csproj

Also run the repository's source and published smoke workflows and record exact non-skipped output in the implementation PR.

**Dependencies:** Task 17.

**Files likely touched:**

- tests/InfraGate.Safety.E2E.Tests/
- tests/InfraGate.McpGateway.Tests/
- tests/InfraGate.Observer.IntegrationTests/
- tests/InfraGate.Planner.IntegrationTests/
- tests/InfraGate.Executor.IntegrationTests/

**Estimated scope:** Large; organize by test tier.

### Task 19: Cut over, remove InfraGate.McpServer, and update source-of-truth docs

**Description:** Make the upstream roles the only Kubernetes subprocesses after Task 18 and explicit human approval. Delete the old runtime and obsolete DTO contract tests in the same change so no hidden fallback remains.

**Acceptance criteria:**

- InfraGate.McpServer, InfraGate.McpServer.Tests, InfraGate.McpServer.ContractTests, project references, Docker publish stages, CI invocations, old downstream assembly/hash configuration, and local DTO compatibility seams are removed.
- Architecture dependency tests and the solution map describe upstream as an execution substrate while the Kubernetes Adapter remains the Domain Adapter and the Generic Approval Core remains unchanged.
- README, architecture, security model, tool permissions, configuration, setup/dev runbooks, MCP compliance, Run Profiles, evidence-parity status, ADR cross-references, and release notes reflect the exact enabled/disabled surface.

**Verification:**

- Search for non-historical InfraGate.McpServer references and review every remaining match.
- Run Task 18 again from a clean build and from the published image.
- Roll back the deployment to the previous release image and prove the old route restores without database or Plan Envelope corruption; do not silently execute plans created for a mismatched substrate/schema.
- Run rtk git diff --check.

**Dependencies:** Task 18 and explicit human cutover approval.

**Files likely touched:**

- src/InfraGate.McpServer/ (delete)
- tests/InfraGate.McpServer.Tests/ and tests/InfraGate.McpServer.ContractTests/ (delete)
- InfraGate.slnx and tests/InfraGate.Architecture.Tests/
- deploy/docker/mcp-gateway.Dockerfile and .github/workflows/
- README.md, CONTEXT.md if needed, docs/, and affected project READMEs

**Estimated scope:** Large; split deletion, build/CI, and documentation while keeping one review checkpoint.

### Final Checkpoint

- [ ] The pinned upstream artifact and schema are reproducible from the release manifest.
- [ ] All enabled reads and writes have explicit InfraGate authorization, bounds, and adapter ownership.
- [ ] Every write is approval-bound and passes all generic and domain pre-execution gates.
- [ ] Full solution, conformance, safety E2E, smoke, container, and rollback checks have non-skipped evidence.
- [ ] No raw upstream endpoint, write catalog entry, annotation-based authorization, or legacy runtime fallback remains.
- [ ] The implementation and updated ADRs have human approval.

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Upstream does not accept/release plan-only primitives | Full replacement remains blocked | Upstream-first PRs; make fork ownership an explicit human decision. Do not route privileged writes to v0.0.66. |
| Pre-1.0 release changes tools or schemas | Silent permission or plan drift | Pin version/checksum/schema/config digests; fail closed and require compatibility review for every upgrade. |
| disable_destructive permits writes | Approval bypass | Ignore it as an authorization boundary; use the InfraGate capability manifest, executor isolation, scopes, and RBAC. |
| Force SSA changes unrelated ownership or multi-document calls partially apply | Unexpected/partial mutation | Exact diff evidence, managedFields review, deterministic plan steps, preconditions, per-step audit, and explicit partial-failure outcome. |
| Privileged executor compromise | Broad cluster mutation | Separate process/credential, no public listener/catalog, minimal enabled tools, namespace/privilege tiers, RBAC, environment isolation, and short-lived secrets. |
| Secrets leak through plan/evidence/logs | Credential disclosure | Default deny; encrypted/reference-bound intent storage, redacted review metadata, bounded/sanitized output, canary leak tests. |
| pods_exec bypasses resource review semantics | Arbitrary workload mutation/exfiltration | Separate default-off scope/ADR, exact argv and target binding, short grant, no stdin/TTY, output bounds/redaction. |
| Helm artifact changes after approval | Review/execution mismatch | Allowlisted immutable registry/chart digest, rendered evidence digest, pre-execution re-resolution. |
| Kiali/external toolsets bypass Kubernetes RoundTripper policy | Policy gap | Keep disabled until a separate adapter-level contract and real integration suite exist. |
| Removing the old server makes rollback hard | Availability risk | Cut over only after non-skipped evidence; rollback by prior release image; bind plans to substrate/schema so cross-version execution fails closed. |

## Open Questions Requiring Human Decisions

1. **Upstream ownership:** If upstream does not merge/release the plan-only primitives, is the project willing to own and patch an Apache-2.0 fork? Recommended: no production fork without named maintenance and security-update ownership.
2. **Initial breadth:** Does “full” initially mean core Kubernetes writes only, or core plus Helm/Kiali/KubeVirt/Tekton? Recommended: core first, Helm second, each optional write toolset as a later adapter slice.
3. **Privilege profile:** Is namespace-scoped writer RBAC sufficient, or is an explicit cluster-scoped privileged profile required? Recommended: ship namespace writer first and make cluster-scoped capability a separate reviewed profile.
4. **Command execution:** Should pods_exec be enabled at all? Recommended: keep it default-off and require its own ADR/elevated scope.
5. **Sensitive resources:** May plans contain new Secret values, or should they only reference an external secret system? Recommended: external immutable references where possible; otherwise finish Task 13 before enabling Secret writes.
6. **Identity model:** Is shared ServiceAccount execution acceptable, or must Kubernetes see each end-user identity? Recommended for current architecture: retain shared service identity over stdio; design protected internal HTTP/token exchange separately if per-user identity becomes a requirement.

## Reference Evidence

- Upstream release: https://github.com/containers/kubernetes-mcp-server/releases/tag/v0.0.66
- Upstream tool inventory: https://github.com/containers/kubernetes-mcp-server/blob/v0.0.66/README.md
- Upstream generic resource execution: https://github.com/containers/kubernetes-mcp-server/blob/v0.0.66/pkg/kubernetes/resources.go
- Upstream tool filters and annotations: https://github.com/containers/kubernetes-mcp-server/blob/v0.0.66/pkg/mcp/mcp.go
- Upstream confirmation behavior: https://github.com/containers/kubernetes-mcp-server/blob/v0.0.66/pkg/confirmation/confirmation.go
- Upstream configuration: https://github.com/containers/kubernetes-mcp-server/blob/v0.0.66/docs/configuration.md
- InfraGate mutation evidence contract: docs/mutation-evidence-parity-contract.md
- InfraGate current read-only decision: docs/adr/0033-kubernetes-mcp-server-readonly-secondary-downstream.md
- InfraGate replacement prerequisite: docs/adr/0021-mcpserver-local-dto-copies-over-shared-contracts.md
- Canonical approval flow: CONTEXT.md and docs/mutation-approval-flow.md
