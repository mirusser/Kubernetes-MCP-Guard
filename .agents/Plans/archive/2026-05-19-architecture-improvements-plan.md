# Implementation Plan: Mutation Approval Architecture Improvements

## Overview

Five deepening opportunities identified in the 2026-05-18 drift audit and grilled against the domain model. This plan implements four structural changes and one documentation-only change. All decisions were resolved in a grilling session against CONTEXT.md, mutation-approval-profile.md, and the improve-codebase-architecture framework.

ADR 0007 (lazy-only challenge expiration) was already written and requires no code change.

## Architecture Decisions

- **Plan Validity Window**: enforced in `GatewayApprovalService.EnsureApprovedOrCreateChallengeAsync` as a convention (not in the store). Challenge TTL is capped to `min(configuredTtl, envelope.ValidUntilUtc - now)`. `PlanValidityWindow` wrapper is not enriched — raw `DateTimeOffset` fields stay on `PlanEnvelope`.
- **Authorization Check**: extracted as `IAuthorizationCheck` / `IAuthorizationContext` (with both subjects on the context) in `InfraGate.Approvals`. `SameSubjectAuthorizationCheck` lives in `InfraGate.McpGateway`. Approval Policy enforcement in `ApprovalStore` is a separate concern and is not touched.
- **Pre-Execution Gate**: two-bucket model (generic core owns gates 1–6, domain adapter owns gates 7–8) is correct as-is. Documentation only.
- **IDomainAdapter**: composed interface in `InfraGate.Approvals`. `KubernetesDomainAdapter` is an internal proxy in `InfraGate.KubernetesAdapter` holding the four narrow interfaces. `AddKubernetesAdapter` extension method is the single registration contract. `KubernetesApprovalAdapter` made `internal static`. `GatewayToolDispatcher` collapses to one `IDomainAdapter` dependency; `GatewayApprovalService` keeps its narrow interfaces.

## Dependency Graph

```
[Phase 1] IAuthorizationContext, IAuthorizationCheck, AuthorizationResult (InfraGate.Approvals)
              └── [Phase 2] SameSubjectAuthorizationCheck, PlanAuthorizationContext (InfraGate.McpGateway)
                                └── [Phase 3] GatewayApprovalService — replace copy-paste checks
                                                └── Program.cs — register IAuthorizationCheck

[Phase 1] IDomainAdapter (InfraGate.Approvals)
              └── [Phase 2] KubernetesDomainAdapter + AddKubernetesAdapter (InfraGate.KubernetesAdapter)
                                └── [Phase 3] GatewayToolDispatcher — collapse to IDomainAdapter
                                                └── Program.cs — call AddKubernetesAdapter()

[Phase 2] Plan Validity Window enforcement (independent of above)

[Phase 4] Documentation — independent, can run any time
```

---

## Phase 1: New Types in InfraGate.Approvals

Tasks 1 and 2 are independent and can run in parallel.

---

### Task 1: Authorization Check types

**Description:** Add `IAuthorizationContext`, `IAuthorizationCheck`, and `AuthorizationResult` to `InfraGate.Approvals`. These are generic approval-core types — no Kubernetes or gateway specifics. Follow existing gate-result patterns (`ApprovalGateResult`, `PreExecutionGateResult`).

**Acceptance criteria:**
- [ ] `IAuthorizationContext` exposes `string RequesterSubject` and `string ActorSubject`
- [ ] `IAuthorizationCheck` declares `Task<AuthorizationResult> EvaluateAsync(IAuthorizationContext context, CancellationToken cancellationToken)`
- [ ] `AuthorizationResult` is a `sealed record` with `bool IsAuthorized`, `string? Reason`, and static factories `Authorized()` / `Denied(string reason)`
- [ ] All three are in separate files, file-scoped namespace `InfraGate.Approvals`
- [ ] `dotnet build` for `InfraGate.Approvals` passes with no warnings

**Verification:**
- `dotnet build src/InfraGate.Approvals/InfraGate.Approvals.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/IAuthorizationContext.cs` (new)
- `src/InfraGate.Approvals/IAuthorizationCheck.cs` (new)
- `src/InfraGate.Approvals/AuthorizationResult.cs` (new)

**Estimated scope:** S

---

### Task 2: IDomainAdapter composed interface

**Description:** Add `IDomainAdapter` to `InfraGate.Approvals` as a composed interface inheriting all four narrow domain adapter interfaces. This is a contract type only — no implementation, no DI registration yet.

**Acceptance criteria:**
- [ ] `IDomainAdapter : IDomainPlanBuilder, IDomainPlanExecutor, IPlanReviewAdapter, IPlanReviewRenderer`
- [ ] Single file, file-scoped namespace `InfraGate.Approvals`
- [ ] No implementation class references `IDomainAdapter` yet
- [ ] `dotnet build` for `InfraGate.Approvals` passes with no warnings

**Verification:**
- `dotnet build src/InfraGate.Approvals/InfraGate.Approvals.csproj`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/IDomainAdapter.cs` (new)

**Estimated scope:** XS

---

### Checkpoint: Phase 1

- [ ] `dotnet build src/InfraGate.Approvals/InfraGate.Approvals.csproj` — no errors, no warnings

---

## Phase 2: Implementations

Tasks 3, 4, and 5 are independent and can run in parallel.

---

### Task 3: SameSubjectAuthorizationCheck and PlanAuthorizationContext

**Description:** Add the concrete authorization check implementation and its context type to `InfraGate.McpGateway`. These replace the copy-pasted `SameSubject()` logic that currently lives in two places in `GatewayApprovalService`. Do not wire them into `GatewayApprovalService` yet — that is Task 6.

**Acceptance criteria:**
- [ ] `PlanAuthorizationContext : IAuthorizationContext` with `RequesterSubject` and `ActorSubject` as positional record properties
- [ ] `SameSubjectAuthorizationCheck : IAuthorizationCheck` — returns `AuthorizationResult.Authorized()` when subjects match (ordinal), `AuthorizationResult.Denied(reason)` when they do not
- [ ] Both are `sealed`, file-scoped namespace `InfraGate.McpGateway`
- [ ] Unit tests: `SameSubjectAuthorizationCheck_WhenSubjectsMatch_ReturnsAuthorized`, `SameSubjectAuthorizationCheck_WhenSubjectsDiffer_ReturnsDeniedWithReason`
- [ ] Tests pass

**Verification:**
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "SameSubjectAuthorizationCheckTests"`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpGateway/PlanAuthorizationContext.cs` (new)
- `src/InfraGate.McpGateway/SameSubjectAuthorizationCheck.cs` (new)
- `tests/InfraGate.McpGateway.Tests/UnitTests/SameSubjectAuthorizationCheckTests.cs` (new)

**Estimated scope:** S

---

### Task 4: KubernetesDomainAdapter, AddKubernetesAdapter, and KubernetesApprovalAdapter internalization

**Description:** Add the internal `KubernetesDomainAdapter` proxy and `AddKubernetesAdapter` extension method to `InfraGate.KubernetesAdapter`. Make `KubernetesApprovalAdapter` `internal static`. Add `[InternalsVisibleTo]` for test projects that currently reference internal types. Do not update `Program.cs` or `GatewayToolDispatcher` yet — that is Task 7.

**Acceptance criteria:**
- [ ] `KubernetesDomainAdapter` is `internal sealed`, implements `IDomainAdapter` by holding `IDomainPlanBuilder`, `IDomainPlanExecutor`, `IPlanReviewAdapter`, `IPlanReviewRenderer` via primary constructor and delegating all members
- [ ] `KubernetesAdapterServiceCollectionExtensions` is `public static`, provides `AddKubernetesAdapter(this IServiceCollection services)` that registers:
  - `IDomainPlanBuilder` → `KubernetesPlanBuilder` (singleton)
  - `IDomainPlanExecutor` → `KubernetesPlanExecutor` (singleton)
  - `IPlanReviewAdapter` → `KubernetesPlanReviewAdapter` (singleton)
  - `IPlanReviewRenderer` → `KubernetesPlanReviewRenderer` (singleton)
  - `IDomainAdapter` → `KubernetesDomainAdapter` (singleton)
- [ ] `KubernetesApprovalAdapter` changed from `public static` to `internal static` — no other change
- [ ] `[InternalsVisibleTo]` added to `InfraGate.KubernetesAdapter.csproj` for all test projects that reference internal types
- [ ] `dotnet build` for all projects passes

**Verification:**
- `dotnet build`

**Dependencies:** Task 2

**Files likely touched:**
- `src/InfraGate.KubernetesAdapter/KubernetesDomainAdapter.cs` (new)
- `src/InfraGate.KubernetesAdapter/KubernetesAdapterServiceCollectionExtensions.cs` (new)
- `src/InfraGate.KubernetesAdapter/KubernetesApprovalAdapter.cs` (access modifier only)
- `src/InfraGate.KubernetesAdapter/InfraGate.KubernetesAdapter.csproj` (InternalsVisibleTo)

**Estimated scope:** M

---

### Task 5: Plan Validity Window enforcement

**Description:** Add the window-active check and TTL cap to `GatewayApprovalService.EnsureApprovedOrCreateChallengeAsync`, immediately before the call to `challengeStore.CreateAsync`. Add a convention comment to `IApprovalChallengeStore.CreateAsync` noting that callers are responsible for validating the plan validity window before creating a challenge.

**Acceptance criteria:**
- [ ] If `now < pending.Envelope.ValidFromUtc` — return `ApprovalGateResult.RequiresApproval` with a clear message that the plan validity window has not started
- [ ] If `now >= pending.Envelope.ValidUntilUtc` — return `ApprovalGateResult.RequiresApproval` with a clear message that the plan has expired
- [ ] `effectiveTtl = TimeSpan` from `min(options.ApprovalChallengeTtl, pending.Envelope.ValidUntilUtc - now)` passed to `challengeStore.CreateAsync`
- [ ] `IApprovalChallengeStore.CreateAsync` has a convention comment: callers must verify the plan validity window before calling
- [ ] Unit tests cover: window not started, window expired, TTL capped to remaining window, TTL unchanged when remaining window exceeds configured TTL
- [ ] Tests pass

**Verification:**
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "GatewayApprovalServiceTests"`

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- `src/InfraGate.Approvals/IApprovalChallengeStore.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`

**Estimated scope:** S

---

### Checkpoint: Phase 2

- [ ] `dotnet build` — no errors, no warnings
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — all pass

---

## Phase 3: Wire Callers

Tasks 6 and 7 are independent and can run in parallel. Both touch `Program.cs` — coordinate to avoid conflicts.

---

### Task 6: Wire IAuthorizationCheck into GatewayApprovalService

**Description:** Replace the two copy-pasted `SameSubject()` calls in `GatewayApprovalService` with `IAuthorizationCheck`. Inject `IAuthorizationCheck` via constructor. Register `IAuthorizationCheck` → `SameSubjectAuthorizationCheck` as singleton in `Program.cs`. Remove the private `SameSubject(string, string)` static helper if it becomes unused.

**Acceptance criteria:**
- [ ] `GatewayApprovalService` takes `IAuthorizationCheck` via constructor — no `SameSubjectAuthorizationCheck` reference
- [ ] Both call sites (grant path at ~line 58 and pending path at ~line 91) use `await authorizationCheck.EvaluateAsync(new PlanAuthorizationContext(...), ct)`
- [ ] `Program.cs` registers `IAuthorizationCheck` → `SameSubjectAuthorizationCheck` as singleton
- [ ] `SameSubject(string, string)` private static removed if unused
- [ ] `GatewayDiWiringTests` updated for the new constructor parameter
- [ ] Existing `GatewayApprovalServiceTests` still pass (authorization behavior unchanged)

**Verification:**
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "GatewayApprovalServiceTests|GatewayDiWiringTests"`

**Dependencies:** Tasks 1, 3

**Files likely touched:**
- `src/InfraGate.McpGateway/GatewayApprovalService.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayDiWiringTests.cs`

**Estimated scope:** S

---

### Task 7: Wire IDomainAdapter into GatewayToolDispatcher and Program.cs

**Description:** Replace the four separate constructor parameters in `GatewayToolDispatcher` with a single `IDomainAdapter`. Update `Program.cs` to call `services.AddKubernetesAdapter()` and remove the four individual Kubernetes adapter registrations. `IGatewayApprovalService` keeps its two narrow interface dependencies unchanged.

**Acceptance criteria:**
- [ ] `GatewayToolDispatcher` constructor takes `IDomainAdapter` — removes `IDomainPlanBuilder`, `IDomainPlanExecutor`, `IPlanReviewAdapter`, `IPlanReviewRenderer` parameters
- [ ] `GatewayToolDispatcher` internal field references replaced with `IDomainAdapter` calls
- [ ] `Program.cs` calls `builder.Services.AddKubernetesAdapter()` — no direct references to `KubernetesPlanBuilder`, `KubernetesPlanExecutor`, `KubernetesPlanReviewAdapter`, `KubernetesPlanReviewRenderer` remain
- [ ] `GatewayDiWiringTests` updated for the new constructor signature
- [ ] `GatewayToolDispatcherTests` updated — mock `IDomainAdapter` instead of four separate mocks
- [ ] All tests pass

**Verification:**
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter "GatewayToolDispatcherTests|GatewayDiWiringTests"`

**Dependencies:** Tasks 2, 4

**Files likely touched:**
- `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayToolDispatcherTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayDiWiringTests.cs`

**Estimated scope:** M

---

### Checkpoint: Phase 3

- [ ] `dotnet build` — no errors, no warnings
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — all pass
- [ ] `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj` — all pass (opt-in, skip if Keycloak not available)
- [ ] No reference to `KubernetesPlanBuilder`, `KubernetesPlanExecutor`, `KubernetesPlanReviewAdapter`, `KubernetesPlanReviewRenderer` exists outside `InfraGate.KubernetesAdapter`
- [ ] No reference to `KubernetesApprovalAdapter` exists outside `InfraGate.KubernetesAdapter`

---

## Phase 4: Documentation

Tasks 8 and 9 are independent and can run at any point.

---

### Task 8: Pre-Execution Gate two-bucket documentation

**Description:** Add a comment to `ApprovalPreExecutionGate.EvaluateAsync` naming the two ownership buckets and their gate ranges. Add a note to `mutation-approval-flow.md` explaining that the profile's 8 sequential gates map onto two implementation buckets divided by ownership boundary.

**Acceptance criteria:**
- [ ] `ApprovalPreExecutionGate.EvaluateAsync` has a concise comment stating: generic core owns gates 1–6 via `ApprovalStore.ValidateGrant` (grant validity, plan window, grant expiry, authorization, intent digest, review digest, reuse policy); domain adapter owns gates 7–8 via `domainExecutor.CheckPreExecutionAsync` (freshness policy, domain policy checks)
- [ ] `mutation-approval-flow.md` Pre-Execution Gate Flow section has a note that the 8 flowchart gates are implemented as two ownership buckets

**Verification:**
- Read both files to confirm clarity

**Dependencies:** None

**Files likely touched:**
- `src/InfraGate.Approvals/ApprovalPreExecutionGate.cs`
- `docs/mutation-approval-flow.md`

**Estimated scope:** XS

---

### Task 9: IApprovalChallengeStore.CreateAsync convention comment

**Description:** Add a single-line convention comment to `IApprovalChallengeStore.CreateAsync` noting that callers are responsible for validating the plan validity window before calling. This makes the convention explicit at the definition site.

**Acceptance criteria:**
- [ ] `IApprovalChallengeStore.CreateAsync` has a comment noting caller responsibility for window validation
- [ ] No other changes to the interface

**Verification:**
- Read the file to confirm placement

**Dependencies:** None (but logically pairs with Task 5)

**Files likely touched:**
- `src/InfraGate.Approvals/IApprovalChallengeStore.cs`

**Estimated scope:** XS

---

### Checkpoint: Complete

- [ ] `dotnet build` — no errors, no warnings
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — all pass
- [ ] `dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` — all pass (opt-in)
- [ ] No `KubernetesApprovalAdapter`, `KubernetesPlanBuilder`, `KubernetesPlanExecutor`, `KubernetesPlanReviewAdapter`, `KubernetesPlanReviewRenderer` referenced outside `InfraGate.KubernetesAdapter`
- [ ] No copy-pasted `SameSubject(string, string)` pattern in `GatewayApprovalService`
- [ ] Challenge creation in `EnsureApprovedOrCreateChallengeAsync` enforces plan validity window and caps TTL

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `GatewayDiWiringTests` reflects constructor signatures — both Task 6 and Task 7 touch it | Med | Coordinate or merge tasks 6+7 into one session to resolve `Program.cs` and DI test conflicts together |
| `KubernetesApprovalAdapter` internal-ization breaks test projects that reference it directly | Low | Add `[InternalsVisibleTo]` in Task 4 before any test project fails |
| `KubernetesDomainAdapter` delegation boilerplate — each of the four interfaces must be fully delegated | Low | Verify all interface members are delegated; build failure catches any missed members |
| Plan Validity Window capping produces a near-zero TTL for plans close to expiry | Low | TTL cap is behavioral-correct per profile; challenge creation with a very short TTL is valid — the approver will see an immediate expiry |

## Open Questions

- None. All design decisions were resolved in the grilling session.
