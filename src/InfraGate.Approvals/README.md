# InfraGate.Approvals

`InfraGate.Approvals` is the generic approval storage, challenge, grant, audit, and pre-execution gate layer. It defines the on-disk plan lifecycle (pending → granted → applied), generic plan envelopes with adapter payloads, digest-bound evidence artifact summaries, approval challenge records, durable approval grants, audit event conventions, and the strongly-typed audit payload schema.

## Runtime Flow

- `ApprovalStore.cs` persists generic plan envelopes and approval grants under `K8S_MCP_APPROVAL_ROOT`, detects challenge drift by comparing stored plan-file bytes, validates Intent Digest and Review Digest bindings before execution, refuses old raw-plan files with a re-request message, and writes approval audit events to `audit.jsonl`.
- `PlanEnvelope.cs`, `PlanEnvelope.Typed.cs`, and `PlanRequester.cs` model the generic approval envelope and the requester identity bound to it.
- `EvidenceArtifactSummary.cs` records digest-bound references to adapter evidence included in the Review Digest without teaching the generic core Kubernetes semantics.
- `IApprovalPreExecutionGate.cs` / `ApprovalPreExecutionGate.cs` validate generic grant/digest/reuse gates, publish `pre_execution.grant.validated`, and call the domain adapter's pre-execution check before any mutation is executed.
- `IApprovalChallengeStore.cs` / `ApprovalChallengeStore.cs` create, persist, update, and query challenge records — each tied to a plan envelope, expected intent and review digests, a requester subject, and a configurable TTL.
- `ApprovalConventions.cs` holds all shared constants: environment variable names, on-disk directory names, audit event names, challenge statuses, approval source labels, and diff change types.
- `AuditPayloads/PlanAuditPayloads.cs` and `AuditPayloads/ChallengeAuditPayloads.cs` define strongly-typed positional records for every approval-audit event, replacing the old anonymous types and locking the JSON wire shape tested by `AuditPayloadsTests`.
- `FixedTimeStringComparer.cs` provides constant-time SHA-256 comparison for drift detection and digest verification.
- `ApprovalChallenge.cs` models the out-of-band approval ticket (id, plan envelope reference, intent/review digest bindings, requester, expiry, status, approver, challenge outcome). `ApprovalGrant.cs` models durable execution authorization.

## Important Contracts

- The approval root directory (`K8S_MCP_APPROVAL_ROOT`) is the gateway-owned durable approval store for pending plans, grants, applied markers, challenges, and audit events.
- `IApprovalChallengeStore`, `IApprovalPreExecutionGate`, `IDomainPlanBuilder`, `IDomainPlanExecutor`, `IToolCaller`, `PlanBuildResult`, and `DomainPlanExecutionResult` define generic approval seams used by the gateway and the domain adapter execution path.
- Pending plan files are generic envelopes. Domain-specific mutation intent and review evidence live inside the adapter payload; Kubernetes payload types live in `InfraGate.KubernetesAdapter`.
- Audit events use names from `ApprovalConventions.AuditEvents`. Payloads are typed `IPlanAuditPayload` or `IChallengeAuditPayload` records; their JSON keys under `JsonSerializerDefaults.Web` are the contract. Adapter audit details live under nested flexible `adapterPayload` fields.
- Hash and digest comparison uses `FixedTimeStringComparer` where stored integrity values are compared.
- Challenges are ephemeral (default TTL: 15 minutes) and terminal once resolved; approved challenges issue an Approval Grant for the default Single-Execution Plan.
- [Design rationale: why plan and challenge are separate records](../../docs/why-separated-plan-from-challenge.md).

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

The ApprovalStore and challenge lifecycle are tested from the projects that depend on them:

- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` — `ApprovalStoreTests.cs` covers digest-bound envelopes, legacy refusal, grant checks, and plan lifecycle.
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — `GatewayApprovalServiceTests.cs` covers challenge creation, expiry, same-subject binding, challenge outcomes, grants, and hash/digest drift.
- `dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` — opt-in end-to-end tests proving the seven approval-flow safety properties through the full gateway/server stack.
