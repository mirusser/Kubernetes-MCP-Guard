# Implementation Plan: Gateway / Domain Adapter Separation + Findings Fixes

Date: 2026-05-17

## Overview

Restructure the codebase to align with ADR 0001 and ADR 0006. McpGateway becomes a pure Generic Approval Core with no Kubernetes domain knowledge. McpServer becomes a plan-unaware execution substrate. The Kubernetes Domain Adapter (KubernetesAdapter + KubernetesPlanBuilder/Executor) owns all K8s-specific plan building, evidence gathering, and approval-bound execution gates. Simultaneously closes the two HIGH-severity findings from the 2026-05-17 mutation-approval-flow review (FreshnessPolicy type missing, FreshnessPolicy absent from PlanEnvelope) and the one MEDIUM-severity finding (DomainPolicyCheck model absent).

## Architecture Decisions

- **IToolCaller, IDomainPlanBuilder, IDomainPlanExecutor** — three new seams defined in `InfraGate.Approvals`. Gateway calls them; KubernetesAdapter implements them.
- **IToolCaller** — wraps downstream MCP tool calls; implemented by `DownstreamMcpClient` in Gateway process. KubernetesPlanBuilder and KubernetesPlanExecutor call evidence and execution tools through it.
- **IDomainPlanBuilder.BuildAsync** — returns `PlanBuildResult` (Plan Envelope + plan ID) without storing it. Caller (Gateway) stores via ApprovalStore.
- **IDomainPlanExecutor.ExecuteAsync** — takes a decoded Plan Envelope; validates domain-specific Freshness Checks (drift, pre-execute dry-run) and dispatches to the right raw McpServer tool via IToolCaller. Returns execution result string.
- **Gateway dynamic dispatch** — uses `WithListToolsHandler` + `WithCallToolHandler` to: (a) forward ReadOnly tools as-is, (b) expose `request_[tool]` wrappers for each Destructive=true downstream tool, (c) own `apply_approved_plan` statically.
- **McpServer evidence tools** — new ReadOnly tools per mutation operation (dry_run_*, diff_*). McpServer retains internal KubernetesAdapter data-type reference for K8s object/diff/dry-run types; plan-building code is removed.
- **FreshnessPolicy** — new generic type in InfraGate.Approvals, added to PlanEnvelope. KubernetesPlanBuilder populates it. KubernetesPlanExecutor evaluates it.
- **DomainPolicyCheck** — new model type in InfraGate.Approvals.

## Task List

### Phase 1: Approval Core Contracts

#### Task 1: Add IToolCaller, IDomainPlanBuilder, IDomainPlanExecutor to InfraGate.Approvals

**Description:** Define the three new seams the Gateway will call and the Domain Adapter will implement. Also add `PlanBuildResult` return type for `IDomainPlanBuilder`.

**Acceptance criteria:**
- [ ] `IToolCaller` — `Task<string> CallAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)`
- [ ] `IDomainPlanBuilder` — `Task<PlanBuildResult> BuildAsync(string mutationToolName, IReadOnlyDictionary<string, object?> arguments, PlanRequester requester, CancellationToken ct)`
- [ ] `IDomainPlanExecutor` — `Task<string> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct)`
- [ ] `PlanBuildResult` — record with `PlanEnvelope Envelope` and `string PlanId`
- [ ] All types are in `InfraGate.Approvals`, public, nullable-annotated

**Verification:**
- `dotnet build src/InfraGate.Approvals/InfraGate.Approvals.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/IToolCaller.cs` (new)
- `src/InfraGate.Approvals/IDomainPlanBuilder.cs` (new)
- `src/InfraGate.Approvals/IDomainPlanExecutor.cs` (new)
- `src/InfraGate.Approvals/PlanBuildResult.cs` (new)

**Estimated scope:** Small

---

#### Task 2: Add FreshnessPolicy and FreshnessCheck types; add FreshnessPolicy to PlanEnvelope

**Description:** Formalize FreshnessPolicy as a first-class generic type in InfraGate.Approvals (HIGH severity finding). PlanEnvelope gains a required FreshnessPolicy property. FreshnessPolicy wraps zero or more FreshnessCheck declarations; the check type and parameters are adapter-defined.

**Acceptance criteria:**
- [ ] `FreshnessCheck` — record with `string Type` and `IReadOnlyDictionary<string, string> Parameters` (adapter-defined, may be empty)
- [ ] `FreshnessPolicy` — record with `IReadOnlyList<FreshnessCheck> Checks`
- [ ] `PlanEnvelope` gains `FreshnessPolicy FreshnessPolicy { get; init; }` with a sensible default (empty policy)
- [ ] Existing unit tests still pass (PlanEnvelope construction must not break)
- [ ] `PlanEnvelopeFactory` updated to include FreshnessPolicy in review digest canonicalization (it must be digest-bound per the profile)

**Verification:**
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/FreshnessCheck.cs` (new)
- `src/InfraGate.Approvals/FreshnessPolicy.cs` (new)
- `src/InfraGate.Approvals/PlanEnvelope.cs` (add property)
- `src/InfraGate.Approvals/PlanEnvelopeFactory.cs` (canonicalization update)

**Estimated scope:** Medium

---

#### Task 3: Add DomainPolicyCheck model to InfraGate.Approvals

**Description:** Formalize DomainPolicyCheck as a generic model in InfraGate.Approvals (MEDIUM severity finding). The type records an adapter-defined code, message, severity, and optional object reference — consistent with the existing K8sPlanPolicyFinding shape but generic.

**Acceptance criteria:**
- [ ] `DomainPolicyCheck` — record with `string Code`, `string Message`, `string Severity`, `string? ObjectRef`
- [ ] `ApprovalConventions` gets a `PolicySeverities` section with at least `Information`, `Warning`, `Error` constants
- [ ] No existing code is changed (additive only)

**Verification:**
- `dotnet build src/InfraGate.Approvals/InfraGate.Approvals.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/DomainPolicyCheck.cs` (new)
- `src/InfraGate.Approvals/ApprovalConventions.cs` (add severity constants)

**Estimated scope:** Small

---

### Checkpoint 1
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

---

### Phase 2: McpServer Evidence Tools

#### Task 4: Add ReadOnly evidence tools to McpServer

**Description:** Add the dry-run and diff tools the KubernetesPlanBuilder will call during plan creation. These are ReadOnly by annotation and return structured JSON data (serialized dry-run results, diff results) without any plan or approval vocabulary. K8sManager gains dedicated methods for each evidence operation extracted from the existing request flow.

**Acceptance criteria:**
- [ ] New tools in `K8sTools.cs`, all `ReadOnly = true`:
  - `dry_run_apply_manifest(namespace, manifest)` — returns JSON-serialized dry-run result
  - `dry_run_delete_manifest(namespace, manifest)` — returns JSON-serialized dry-run result
  - `dry_run_scale_deployment(namespace, name, replicas)` — returns JSON-serialized dry-run result
  - `dry_run_restart_deployment(namespace, name)` — returns JSON-serialized dry-run result
  - `dry_run_set_deployment_image(namespace, name, container, image)` — returns JSON-serialized dry-run result
  - `diff_manifest(namespace, manifest)` — returns JSON-serialized diff result
- [ ] Existing `Request*` tools are untouched (not removed yet)
- [ ] Evidence tools are callable via DownstreamMcpClient

**Verification:**
- `dotnet build src/InfraGate.McpServer/InfraGate.McpServer.csproj`
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

**Dependencies:** None (parallel with Phase 1)

**Files likely touched:**
- `src/InfraGate.McpServer/K8sTools.cs`
- `src/InfraGate.McpServer/K8sManager.cs`
- `src/InfraGate.McpServer/K8sManager.DryRun.cs` (extract evidence methods)
- `src/InfraGate.McpServer/K8sConventions.cs` (add new tool name constants)

**Estimated scope:** Medium

---

### Checkpoint 2
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

---

### Phase 3: Kubernetes Domain Adapter — Plan Building

#### Task 5a: Add KubernetesPlanBuilder for manifest operations (apply, delete)

**Description:** Implement `IDomainPlanBuilder` for the apply and delete manifest operations in `InfraGate.KubernetesAdapter`. `KubernetesPlanBuilder` injects `IToolCaller` to call the McpServer's dry-run and diff evidence tools, parses their JSON responses into plan evidence, runs K8s policy checks, and assembles a Plan Envelope via `KubernetesApprovalAdapter`. Populates `FreshnessPolicy` with the appropriate Freshness Check declarations.

**Acceptance criteria:**
- [ ] `KubernetesPlanBuilder` implements `IDomainPlanBuilder`
- [ ] `BuildAsync("apply_manifest", ...)` calls `dry_run_apply_manifest` + `diff_manifest` via `IToolCaller`, parses results, runs policy, returns `PlanBuildResult`
- [ ] `BuildAsync("delete_manifest", ...)` calls `dry_run_delete_manifest`, parses, returns `PlanBuildResult`
- [ ] Returned `PlanEnvelope.FreshnessPolicy` contains declared Freshness Checks for drift and pre-execute dry-run
- [ ] Returns descriptive error string (not exception) if evidence gathering fails
- [ ] Unit tests cover happy path and evidence failure for both operations

**Verification:**
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` (no regressions)
- Unit tests for KubernetesPlanBuilder apply/delete pass

**Dependencies:** Tasks 1, 2, 4

**Files likely touched:**
- `src/InfraGate.KubernetesAdapter/KubernetesPlanBuilder.cs` (new)
- `src/InfraGate.KubernetesAdapter/KubernetesPlanBuilderResult.cs` (new, if needed)
- `src/InfraGate.KubernetesAdapter/KubernetesAdapterConventions.cs` (new tool name / freshness check type constants)
- Tests in appropriate test project

**Estimated scope:** Medium

---

#### Task 5b: Add KubernetesPlanBuilder for deployment operations (scale, restart, set-image)

**Description:** Extend `KubernetesPlanBuilder` to handle the three deployment mutation operations. These share a simpler evidence path (dry-run only, no diff) but have distinct argument shapes.

**Acceptance criteria:**
- [ ] `BuildAsync("scale_deployment", ...)` calls `dry_run_scale_deployment`, returns `PlanBuildResult`
- [ ] `BuildAsync("restart_deployment", ...)` calls `dry_run_restart_deployment`, returns `PlanBuildResult`
- [ ] `BuildAsync("set_deployment_image", ...)` calls `dry_run_set_deployment_image`, returns `PlanBuildResult`
- [ ] Unsupported tool name returns an error `PlanBuildResult` (not exception)
- [ ] Unit tests cover happy path for all three operations

**Verification:**
- Unit tests for KubernetesPlanBuilder deployment operations pass
- `dotnet build InfraGate.slnx`

**Dependencies:** Task 5a

**Files likely touched:**
- `src/InfraGate.KubernetesAdapter/KubernetesPlanBuilder.cs`
- Tests

**Estimated scope:** Small

---

### Phase 4: Kubernetes Domain Adapter — Approval-Bound Execution

#### Task 6: Add KubernetesPlanExecutor implementing IDomainPlanExecutor

**Description:** Implement `IDomainPlanExecutor` in `InfraGate.KubernetesAdapter`. `KubernetesPlanExecutor` decodes the Plan Envelope via `KubernetesApprovalAdapter`, evaluates domain-specific Freshness Checks (live drift detection, pre-execute dry-run), re-evaluates Domain Policy Checks, then calls the appropriate raw McpServer execution tool via `IToolCaller`. This is the Domain Adapter's participation in the Pre-Execution Gate sequence.

**Acceptance criteria:**
- [ ] `KubernetesPlanExecutor` implements `IDomainPlanExecutor`
- [ ] Decodes Plan Envelope; returns error string if decode fails
- [ ] Runs drift check (live K8s state vs. approved diff); blocks with message if drifted
- [ ] Runs pre-execute dry-run via `IToolCaller`; blocks if dry-run fails
- [ ] Re-evaluates domain policy; blocks on error-severity findings
- [ ] Dispatches to correct raw McpServer tool based on decoded operation:
  - apply → `apply_manifest`
  - delete → `delete_manifest`
  - scale → `scale_deployment`
  - restart → `restart_deployment`
  - set-image → `set_deployment_image`
- [ ] Unit tests cover: happy path, drift detected, dry-run failure, policy block

**Verification:**
- Unit tests for KubernetesPlanExecutor pass
- `dotnet build InfraGate.slnx`

**Dependencies:** Tasks 1, 2, 3, 4, 5a, 5b

**Files likely touched:**
- `src/InfraGate.KubernetesAdapter/KubernetesPlanExecutor.cs` (new)
- `src/InfraGate.KubernetesAdapter/KubernetesAdapterConventions.cs`
- Tests

**Estimated scope:** Medium

---

### Checkpoint 3
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] KubernetesPlanBuilder and KubernetesPlanExecutor unit tests pass

---

### Phase 5: McpServer Restructuring — Add Raw Execution Tools, Remove Plan-Aware Tools

#### Task 7: Add raw execution tools to McpServer K8sTools/K8sManager

**Description:** Add the raw, plan-unaware execution tools that `KubernetesPlanExecutor` will call. These tools take only the operational parameters (namespace, manifest/name/replicas/image) — no `planId`, no `requesterSubject`. K8sManager gains matching execute methods that perform the raw K8s mutation (no plan lookup, no grant validation, no drift check — those now live in the Domain Adapter layer).

**Acceptance criteria:**
- [ ] New tools in `K8sTools.cs`, all `Destructive = true`:
  - `apply_manifest(namespace, manifest)`
  - `delete_manifest(namespace, manifest)`
  - `scale_deployment(namespace, name, replicas)`
  - `restart_deployment(namespace, name)`
  - `set_deployment_image(namespace, name, container, image)`
- [ ] Existing `Request*` and `ApplyApprovedPlan` tools are untouched (not removed yet)
- [ ] New tools are callable and produce correct K8s mutations

**Verification:**
- `dotnet build src/InfraGate.McpServer/InfraGate.McpServer.csproj`
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

**Dependencies:** None (parallel with Phases 3-4)

**Files likely touched:**
- `src/InfraGate.McpServer/K8sTools.cs`
- `src/InfraGate.McpServer/K8sManager.cs`
- `src/InfraGate.McpServer/K8sManager.Apply.cs` (extract raw apply logic)
- `src/InfraGate.McpServer/K8sConventions.cs`

**Estimated scope:** Medium

---

#### Task 8: Remove plan-aware tools and plan-building code from McpServer

**Description:** Remove `Request*` tools, `ApplyApprovedPlan` tool, requesterSubject parameters, and plan-building logic from the McpServer. Remove K8sManager.Requests.cs plan-building usage of `KubernetesApprovalAdapter`. Update tests accordingly. McpServer retains the KubernetesAdapter project reference only for K8s data types (K8sObjectRef, K8sPlanDiff, K8sPlanDryRun, etc.) used internally.

**Acceptance criteria:**
- [ ] `K8sTools.cs` has no `Request*` or `ApplyApprovedPlan` methods
- [ ] `K8sManager` has no `RequestApplyManifestAsync`, `RequestDeleteManifestAsync`, etc.
- [ ] `K8sManager` has no `ApplyApprovedPlanAsync`
- [ ] No `requesterSubject` or `requesterAuthenticationType` parameters anywhere in McpServer
- [ ] `K8sManager.Requests.cs` is deleted or contains only helpers used by evidence tools
- [ ] `K8sConventions.ToolNames` loses `Request*` and `ApplyApprovedPlan` constants
- [ ] All McpServer tests pass (update tests that covered removed behavior)

**Verification:**
- `dotnet build InfraGate.slnx`
- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

**Dependencies:** Task 7, Tasks 5a/5b/6 (KubernetesPlanBuilder/Executor must be ready before removing McpServer plan path)

**Files likely touched:**
- `src/InfraGate.McpServer/K8sTools.cs`
- `src/InfraGate.McpServer/K8sManager.Requests.cs` (delete or gut)
- `src/InfraGate.McpServer/K8sManager.cs`
- `src/InfraGate.McpServer/K8sManager.Apply.cs`
- `src/InfraGate.McpServer/K8sConventions.cs`
- `tests/InfraGate.McpServer.Tests/*` (update)

**Estimated scope:** Medium

---

### Checkpoint 4
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
- [ ] McpServer tool list contains only ReadOnly + ReadOnly-evidence + Destructive-raw tools
- [ ] No plan vocabulary in McpServer

---

### Phase 6: Gateway Transformation

#### Task 9: Add downstream tool registry to Gateway

**Description:** Add `DownstreamToolRegistry` (or equivalent) to McpGateway. It connects to the McpServer on startup (via `DownstreamMcpClient`), calls `tools/list`, and caches the tool metadata (name, description, annotations: ReadOnly, Destructive). The registry is the Gateway's only source of truth for what tools the downstream exposes.

**Acceptance criteria:**
- [ ] `DownstreamToolRegistry` or equivalent type in McpGateway
- [ ] Calls McpServer's `tools/list` at startup and caches result
- [ ] Provides: `IReadOnlyList<DownstreamTool> GetReadOnly()`, `IReadOnlyList<DownstreamTool> GetDestructive()`
- [ ] `DownstreamTool` record: `string Name`, `string Description`, `bool IsReadOnly`, `bool IsDestructive`, `JsonElement InputSchema`
- [ ] `DownstreamMcpClient` implements `IToolCaller` (adds `IToolCaller` to its interface implementations)

**Verification:**
- `dotnet build src/InfraGate.McpGateway/InfraGate.McpGateway.csproj`

**Dependencies:** Task 1 (IToolCaller)

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamToolRegistry.cs` (new)
- `src/InfraGate.McpGateway/DownstreamTool.cs` (new)
- `src/InfraGate.McpGateway/DownstreamMcpClient.cs` (implement IToolCaller)
- `src/InfraGate.McpGateway/IDownstreamMcpClient.cs`

**Estimated scope:** Small

---

#### Task 10: Replace K8sGatewayTools with dynamic WithListToolsHandler / WithCallToolHandler

**Description:** Replace `WithToolsFromAssembly()` and `K8sGatewayTools.cs` with custom `WithListToolsHandler` and `WithCallToolHandler` registrations. `ListTools` returns: ReadOnly tools from registry (as-is) + `request_[tool]` entries for each Destructive tool + `apply_approved_plan`. `CallTool` dispatches: ReadOnly → `GuardedToolRunner.CallAsync`; `request_[tool]` → `GuardedToolRunner.CallWithRequesterAsync` + `IDomainPlanBuilder.BuildAsync` + ApprovalStore; `apply_approved_plan` → existing `GatewayApprovalService` gate + `IDomainPlanExecutor.ExecuteAsync`.

**Acceptance criteria:**
- [ ] `K8sGatewayTools.cs` is deleted
- [ ] `WithToolsFromAssembly()` call removed from `Program.cs`
- [ ] `ListTools` response contains correct tool set (ReadOnly forwarded, Destructive hidden, request_* generated, apply_approved_plan static)
- [ ] Calling a ReadOnly tool invokes `GuardedToolRunner.CallAsync` and returns downstream result
- [ ] Calling `request_apply_manifest` invokes `IDomainPlanBuilder.BuildAsync`, stores plan, returns planId message
- [ ] Calling `apply_approved_plan(planId)` invokes approval gate then `IDomainPlanExecutor.ExecuteAsync`
- [ ] `requesterSubject` injection for request_* tools uses HTTP context identity (no injection into McpServer args)

**Verification:**
- `dotnet build InfraGate.slnx`
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` (update tests)

**Dependencies:** Tasks 6, 9

**Files likely touched:**
- `src/InfraGate.McpGateway/K8sGatewayTools.cs` (delete)
- `src/InfraGate.McpGateway/Program.cs`
- `src/InfraGate.McpGateway/GatewayToolDispatcher.cs` (new — houses list + call handler logic)
- `src/InfraGate.McpGateway/GatewayApprovalService.cs` (minor: remove K8s-naming assumptions if any)
- `tests/InfraGate.McpGateway.Tests/*` (update)

**Estimated scope:** Large — split further during implementation if GatewayToolDispatcher grows beyond ~5 files of change

---

#### Task 11: Clean up McpGatewayConventions and Program.cs wiring

**Description:** Remove K8s tool name constants from `McpGatewayConventions.ToolNames`, add generic convention constants (e.g., `RequestToolPrefix = "request_"`). Update `Program.cs` DI registrations: add `IDomainPlanBuilder` → `KubernetesPlanBuilder`, `IDomainPlanExecutor` → `KubernetesPlanExecutor`, `IToolCaller` → `DownstreamMcpClient`. Verify the McpGateway project no longer uses any K8s-specific type in its logical layer (only in `Program.cs` DI bootstrapping).

**Acceptance criteria:**
- [ ] `McpGatewayConventions.ToolNames` contains no K8s tool names
- [ ] `Program.cs` registers `IDomainPlanBuilder`, `IDomainPlanExecutor`, `IToolCaller`
- [ ] `InfraGate.McpGateway.csproj` still references `InfraGate.KubernetesAdapter` (for DI wiring) but no Gateway source file outside `Program.cs` imports `InfraGate.KubernetesAdapter`
- [ ] `dotnet build InfraGate.slnx` clean

**Verification:**
- `dotnet build InfraGate.slnx`
- Grep: `grep -rn "KubernetesAdapter\|KubernetesPlan" src/InfraGate.McpGateway/ --include="*.cs" | grep -v Program.cs` → zero results

**Dependencies:** Task 10

**Files likely touched:**
- `src/InfraGate.McpGateway/McpGatewayConventions.cs`
- `src/InfraGate.McpGateway/Program.cs`

**Estimated scope:** Small

---

### Checkpoint 5
- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`
- [ ] Gateway tool list: ReadOnly tools forwarded, Destructive hidden, request_* generated, apply_approved_plan present
- [ ] End-to-end flow: `request_apply_manifest` → approval → `apply_approved_plan` works locally
- [ ] Grep check from Task 11 passes (no K8s imports outside Program.cs)

---

### Phase 7: RunProfiles and Configuration Alignment

#### Task 12: Update RunProfiles and any references to removed tool names

**Description:** The RunProfiles YAML and any scripts, docs, or config that reference the old `request_*` tool names from the McpServer side (if any) should be updated. The Gateway-generated `request_*` tools have the same names as before from the AI client's perspective, so MCP client config is unaffected.

**Acceptance criteria:**
- [ ] `deploy/run-profiles.yaml` still validates cleanly
- [ ] No script or workflow references `apply_approved_plan` as a McpServer tool name
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate` passes

**Verification:**
- `dotnet run --project src/InfraGate.RunProfiles -- validate`

**Dependencies:** Checkpoint 5

**Files likely touched:**
- `deploy/run-profiles.yaml` (if affected)
- Scripts (if affected)

**Estimated scope:** Small

---

### Final Verification

- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`
- [ ] `dotnet run --project src/InfraGate.RunProfiles -- validate`
- [ ] E2E: full plan → approve → execute flow works
- [ ] `grep -rn "KubernetesAdapter\|KubernetesPlan" src/InfraGate.McpGateway/ --include="*.cs" | grep -v Program.cs` → zero results
- [ ] McpServer has no `Request*`, `ApplyApprovedPlan`, or `requesterSubject` identifiers

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| MCP SDK `WithListToolsHandler` / `WithCallToolHandler` bypasses route-level auth | Low | Route-level auth (`MapMcp(...).RequireAuthorization(...)`) is ASP.NET Core middleware and fires before any MCP handler body — unaffected by handler vs. `WithToolsFromAssembly`. Tool-level `[Authorize]` filtering (`AddAuthorizationFilters`) is not used by the gateway (no per-tool attributes). Verify with one integration test: unauthenticated `CallTool` via handler path must return 401, not dispatch into `IDomainPlanBuilder`. |
| McpServer tool discovery requires live McpServer at Gateway startup | Medium | Cache tools on first request instead of startup; fail gracefully if McpServer unreachable |
| Evidence tool JSON output schema changes break KubernetesPlanBuilder parsing | Medium | Define explicit JSON contracts for evidence tools; add parsing tests in Task 5a |
| KubernetesPlanExecutor re-evaluates policy but policy rules changed since plan creation | Low | Document this as a known behavior; policy re-evaluation is a feature (prevents stale approval grants) |
| E2E tests reference old tool names or old McpServer plan flow | Medium | Update tests in Tasks 8 and 10 before final checkpoint |

## Open Questions

- Should `DownstreamToolRegistry` refresh its tool cache if the McpServer restarts mid-session? (Current DownstreamMcpClient reconnects lazily — same behavior applies.)
- `apply_approved_plan` renamed to `execute_approved_plan` (2026-05-17 remediation). Resolved.
- **Gateway → Domain Adapter auth (deferred, do not include in this plan):** McpGateway ↔ McpServer communication will use Client Credentials / OIDC flow in a future phase. Currently the channel is stdio with no authentication. Any design decisions in this plan that touch `DownstreamMcpClient` or the transport layer must not assume the channel stays stdio or stays anonymous — leave the transport pluggable.
