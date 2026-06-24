# Mutation Approval Codebase Drift Audit

**Date:** 2026-05-18  
**Scope:** Full codebase cross-reference against CONTEXT.md, mutation-approval-profile.md, mutation-approval-flow.md  
**Method:** Line-by-line verification of every domain concept, relationship, pre-execution gate, audit event, digest computation, and lifecycle state

---

## Behavioral Gaps

### 1. Challenge TTL not bounded by Plan Validity Window

- **CONTEXT.md:222** states: _"An Approval Challenge has one Challenge TTL bounded by the Plan Validity Window."_
- **Profile doc:94** says: `| Approval Challenge | has one | Challenge TTL |`
- **Reality:** No enforcement. `GatewayApprovalService.cs:117-125` passes `options.ApprovalChallengeTtl` directly to `challengeStore.CreateAsync()` with no comparison against `envelope.ValidFromUtc` or `envelope.ValidUntilUtc`. The string `ValidFromUtc` appears zero times in the entire `src/InfraGate.McpGateway/` directory.
- **Impact:** A challenge with TTL exceeding the plan's remaining validity can be created. The constraint is enforced only indirectly at pre-execution time (grant validation at `ApprovalStore.cs:374` blocks when `envelope.ValidUntilUtc <= now`). The challenge remains "pending" after the plan itself has expired.
- **Files:** `src/InfraGate.McpGateway/GatewayApprovalService.cs`

### 2. Plan Validity Window unchecked at challenge creation

- **CONTEXT.md:203-204** says: _"A Plan Envelope may produce one or more Approval Challenges"_
- **Profile doc:** _"Create one or more short-lived approval challenges while the plan remains valid."_
- **Reality:** `EnsureApprovedOrCreateChallengeAsync` (`GatewayApprovalService.cs:117`) creates a challenge with no temporal gate. It does not check whether `envelope.ValidUntilUtc > now` or `envelope.ValidFromUtc <= now`. A challenge can be created for a plan whose validity window has already expired or hasn't started yet.
- **Location:** `GatewayApprovalService.cs:117` — `challengeStore.CreateAsync(...)` called without any plan-window comparison against `DateTimeOffset.UtcNow`.
- **Note:** `GetPlanReadinessRefusal` (`GatewayApprovalService.cs:143-151`) checks only `HasReviewEvidence` — it performs zero plan validity window checks.

### 3. No background challenge expiration sweep

- **Profile doc:157** lists `challenge.expired` as a required Audit Spine event.
- **Reality:** Expiration is **lazy only**. It triggers only when a challenge is explicitly accessed by challenge ID (e.g., someone bookmarks the approval URL or calls the cancel endpoint). `ApprovalChallengeStore.FindPendingAsync` (line 196) filters expired challenges by `ExpiresAtUtc > now` but never transitions them to `"expired"` status. If no one ever visits the challenge URL, the challenge file remains on disk in `"pending"` status indefinitely.
- **No** `BackgroundService`, `IHostedService`, `Timer`, `PeriodicTimer`, sweep, or cleanup mechanism exists anywhere in `src/`.
- **Files:** `src/InfraGate.Approvals/ApprovalChallengeStore.cs`, `src/InfraGate.McpGateway/GatewayApprovalService.cs`

### 4. Redaction metadata schema exists but never populated

- **CONTEXT.md:129-131** defines **Redacted Evidence**: *"Plan Evidence that intentionally hides sensitive parts of a Mutation Intent while disclosing that hiding to the Approver."*
- **CONTEXT.md:201**: _"Plan Evidence may be Redacted Evidence."_
- **CONTEXT.md:197**: The **Review Digest** covers *"redaction metadata"*.
- **Profile doc:81-95**: The Plan Envelope schema includes `RedactionMetadata`.
- **Reality:** `EvidenceArtifactSummary.RedactionMetadata` exists as a `Dictionary<string,string>`. The review digest canonicalization includes it (`PlanEnvelopeFactory.ComputeReviewDigest`, lines 76-110). A test (`PlanEnvelopeFactoryTests.Create_WhenEvidenceArtifactRedactionMetadataChanges_ChangesReviewDigest`) proves the cryptographic binding works. But **`KubernetesApprovalAdapter.BuildEvidenceArtifacts` always passes `[]`** (lines 135-170 of `KubernetesApprovalAdapter.cs`). No production code path ever populates redaction metadata.
- **Files:** `src/InfraGate.Approvals/EvidenceArtifactSummary.cs`, `src/InfraGate.KubernetesAdapter/KubernetesApprovalAdapter.cs`

---

## Architectural Seams (Documented, Not Coded)

### 5. Authorization Check — no distinct module

- **CONTEXT.md:157-159**: _"A separate decision that an actor or system is permitted to request or execute a class of operation."_
- **CONTEXT.md:218-219**: _"An Authorization Check is separate from an Approval Policy. An Authorization Check may gate creation of a Plan Envelope or execution."_
- **Reality:** No `AuthorizationCheck` class, interface, record, or method. The concept is implicit in ASP.NET `RequireAuthorization()` middleware (`Program.cs:67`) plus same-subject checks in `GatewayApprovalService.cs:58-61` and `ApprovalStore.cs:394-398`. There is no seam where authorization logic could be swapped or extended independently of the approval flow.
- **Files:** No dedicated code exists. Distributed across `Program.cs`, `GatewayApprovalService.cs`, `ApprovalStore.cs`.

### 6. No `IPreExecutionGate` interface

- **CONTEXT.md:161-163**: defines **Pre-Execution Gate** as _"A required check evaluated immediately before an Execution Attempt may mutate the target system."_
- **Profile doc:35-36**: _"Generic Approval Core ... owns ... pre-execution gate orchestration."_
- **Reality:** `ApprovalPreExecutionGate` is a `sealed class` with no interface. It is constructed directly in `GatewayToolDispatcher.cs:40`:
  ```csharp
  preExecutionGate = new ApprovalPreExecutionGate(approvalStore, auditPublisher);
  ```
  No seam for alternate gate implementations, no interface for testing with mock gates, no way to add adapter-specific gates without modifying the generic core.
- **Files:** `src/InfraGate.Approvals/ApprovalPreExecutionGate.cs`, `src/InfraGate.McpGateway/GatewayToolDispatcher.cs`

### 7. No Reusable Plan code path

- **CONTEXT.md:93-95**: _"A Plan Envelope whose approval may authorize more than one successful execution under an explicit Execution Reuse Policy."_
- **CONTEXT.md:244**: _"A Reusable Plan is an explicit opt-in exception to Single-Execution Plan."_
- **Reality:** `IsSupportedReusePolicy` (`ApprovalStore.cs:363-364`) hardcodes rejection of anything not `"single-execution"`:
  ```csharp
  private static bool IsSupportedReusePolicy(ExecutionReusePolicy policy) =>
      string.Equals(policy.Type, ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution, StringComparison.Ordinal);
  ```
  There is no `"reusable"` constant in `ApprovalConventions.cs`. No counter for successful executions. No opt-in path. The `ExecutionReusePolicy` record exists but only `single-execution` is accepted at the behavior level.
- **Files:** `src/InfraGate.Approvals/ApprovalStore.cs`, `src/InfraGate.Approvals/ApprovalConventions.cs`

### 8. No `IApprovalAuthority` interface

- **CONTEXT.md:49-51**: _"The participant that creates Approval Challenges, enforces Approval Policies, records Challenge Outcomes, and issues or exposes Approval Grants for execution."_
- **Profile doc:39**: Names it explicitly as a role: _"In this repository, that role is currently implemented by the gateway plus approval store."_
- **Reality:** Split across three concrete classes with no unifying abstraction:
  - `GatewayApprovalService` — challenge creation, outcome recording, grant issuance
  - `ApprovalChallengeStore` — challenge CRUD and persistence
  - `ApprovalStore` — grant management and validation
- The only type named `ApprovalAuthority` is `ApprovalAuthorityProfile` in `src/InfraGate.RunProfiles/`, which is OAuth endpoint configuration — not the functional role described in the docs.
- **Files:** `src/InfraGate.McpGateway/GatewayApprovalService.cs`, `src/InfraGate.Approvals/ApprovalChallengeStore.cs`, `src/InfraGate.Approvals/ApprovalStore.cs`

### 9. No `IDomainAdapter` interface

- **CONTEXT.md:41-43**: defines **Domain Adapter** as _"The domain-specific participant that defines, explains, and executes Mutation Intents for one target system."_
- **Reality:** Split across four separate interfaces:
  - `IDomainPlanBuilder` — builds mutation plans
  - `IDomainPlanExecutor` — pre-execution checks + execution
  - `IPlanReviewAdapter` — decodes plans for human review
  - `IPlanReviewRenderer` — renders review content and approval messages
- No single `IDomainAdapter` interface ties them together. Adding a second domain adapter requires implementing four disjoint interfaces with no contract that they belong together.

### 10. No `ExecutionAttempt` type

- **CONTEXT.md:169-171**: _"One attempt by a Domain Adapter to execute an approved Mutation Intent."_
- **Reality:** Tracked entirely through audit event payloads (`execution.started`, `execution.succeeded`, `execution.failed`, `execution.blocked`). No `ExecutionAttempt` record or class exists. No `execution.attempted` event. The concept exists in the domain glossary but has no code-level representation.

---

## Code Quality / Consistency

### 11. PlanValidityWindow wrapper used inconsistently

- `PlanValidityWindow` sealed record exists at `src/InfraGate.Approvals/PlanValidityWindow.cs`.
- But `PlanEnvelope` stores `ValidFromUtc` and `ValidUntilUtc` as raw `DateTimeOffset` properties — the wrapper is unwrapped before assignment.
- The wrapper is only used transiently in `PlanEnvelopeFactory` (creation and review digest computation).
- **Impact:** The domain term has a type, but the authoritative storage ignores it. If `PlanValidityWindow` ever gained behavior (e.g., `Contains(DateTimeOffset)`, `Remaining`), the envelope could not use it.
- **Files:** `src/InfraGate.Approvals/PlanEnvelope.cs`, `src/InfraGate.Approvals/PlanValidityWindow.cs`, `src/InfraGate.Approvals/PlanEnvelopeFactory.cs`

---

## Doc-to-Code Imprecisions

### 12. Flow chart shows 8 sequential gates; code bundles them

- **Flow doc:166-200** shows 8 gates evaluated in sequence, each with its own pass/fail branch.
- **Code reality:** Gates 1-6 are bundled into a single `ValidateGrant` method (`ApprovalStore.cs:366-407`). Gates 7-8 are bundled into a single `CheckPreExecutionAsync` call (`ApprovalPreExecutionGate.cs:38` → `KubernetesPlanExecutor.cs:15-66`).
- **Impact:** The flowchart suggests independent, separable checks. The code has two coarse-grained buckets. Adding a new gate (e.g., cost estimate check) would require editing `ValidateGrant` or `CheckPreExecutionAsync` rather than registering a new gate.

### 13. "Recompute Intent Digest" wording vs stored-digest comparison

- **Flow doc:173**: _"Recompute and compare Intent Digest."_
- **Code reality:** The generic core (`ApprovalStore`) does **not** recompute the intent digest. It only compares the stored digest from the grant against the stored digest in the envelope (`SameDigest` at line 386). The adapter-owned `KubernetesApprovalAdapter.Decode()` is the only place that recomputes the intent digest from raw payload (lines 104-118), and it's called from `KubernetesPlanExecutor`, not from the generic gate.

---

## Legacy Naming Drift

All audit event **string values** match the profile doc. The **C# constant names** are legacy. This is harmless but worth noting for code navigation.

| C# Constant | String Value | Profile Doc | Matches? |
|---|---|---|---|
| `PlanRequested` | `"plan.created"` | `plan.created` | String ✅ |
| `PlanApplied` | `"execution.succeeded"` | `execution.succeeded` | String ✅ |
| `ApplyDenied` | `"execution.blocked"` | `execution.blocked` | String ✅ |
| `DryRunFailed` | `"execution.blocked"` | `execution.blocked` | String ✅ |
| `DiffFailed` | `"execution.blocked"` | `execution.blocked` | String ✅ |
| `ApplyDriftDetected` | `"execution.blocked"` | `execution.blocked` | String ✅ |
| `ApplyFailed` | `"execution.failed"` | `execution.failed` | String ✅ |

Multiple C# constants resolving to `"execution.blocked"` is intentional — different internal triggers share the same profile event name. The four constants `ApplyDenied`, `DryRunFailed`, `DiffFailed`, `ApplyDriftDetected` carry different audit payload shapes.

---

## Verified Correct

For completeness, the following claims were verified as accurate against the codebase:

- All 14 Audit Spine event strings match the profile doc (See `ApprovalConventions.AuditEvents` at `ApprovalConventions.cs:63-82`, pinned by `ApprovalConventionsTests.cs:8-27`)
- All 8 pre-execution gates are enforced: grant validity (`ApprovalPreExecutionGate.cs:16-20`), plan validity window (`ApprovalStore.cs:369-377`), grant expiry (`ApprovalStore.cs:379-382`), authorization/same-subject (`GatewayApprovalService.cs:58-61` + `ApprovalStore.cs:394-398`), intent digest (`ApprovalStore.cs:386`), review digest (`ApprovalStore.cs:387` + recomputation at lines 400-404), execution reuse (`ApprovalStore.cs:106-109`), freshness/domain policy (`KubernetesPlanExecutor.cs:26-48`)
- Review Digest covers 13 fields matching the profile spec: id, profile, adapterId, operation, createdAtUtc, validFromUtc/validUntilUtc, requester, approvalPolicy, executionReusePolicy, freshnessPolicy, reviewSurfaceContext, evidenceArtifacts (including RedactionMetadata), intentDigest. Verified by `PlanEnvelopeFactory.ComputeReviewDigest` (lines 76-110)
- Intent Digest uses adapter-owned canonicalization `"infra-gate.kubernetes.intent.v1"` (`KubernetesAdapterConventions.Canonicalizations.IntentV1`), computed over operation, namespace, parameters, objects, manifest (`KubernetesApprovalAdapter.cs:121-133`)
- Approval Grant binds 11 fields matching CONTEXT.md:210: PlanId, RequesterSubject, ApproverSubject, SourceChallengeId, IntentDigest, ReviewDigest, ApprovalPolicy, ExecutionReusePolicy, IssuedAtUtc, ExpiresAtUtc (`ApprovalStore.CreateGrantAsync`, lines 186-223)
- Review Surface renders digest-bound content on the approval page — both digests are shown to the human approver as `<code>` elements (`GatewayApprovalEndpoints.cs:163-164`); content is rendered from the store-loaded, decode-verified payload only (`GatewayApprovalService.ValidatePendingChallengeAsync`, lines 339-452)
- All 6 challenge states are implemented with corresponding audit event emission: pending (implicit via `challenge.created`), approved, denied, expired, rejected, canceled. Rejected is system-internal (no HTTP endpoint); expired is lazy-only.
- Challenge deduplication works: `GatewayApprovalService.cs:102-115` reuses existing pending challenge URL for same plan/hash/subject/digests; `ApprovalChallengeStore.FindPendingAsync` (`ApprovalChallengeStore.cs:103-135`) matches on planId, pendingPlanHash, subject, intentDigest, reviewDigest, with status="pending" AND not expired.
- Single-execution reuse enforced via filesystem sentinel: if `{ApprovalRoot}/applied/{planId}.json` exists, execution is blocked (`ApprovalStore.cs:106-109`)
- Adapter audit payloads (`adapterPayload` nested `JsonElement`) correctly separate generic spine from adapter-specific context: `PreExecutionCheckedPayload.AdapterPayload`, `ExecutionStartedPayload.AdapterPayload`
