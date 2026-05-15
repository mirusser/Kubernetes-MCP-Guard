# InfraGate.Approvals

`InfraGate.Approvals` is the generic approval storage, challenge, and audit layer. It defines the on-disk plan lifecycle (pending → approved → applied), generic plan envelopes with adapter payloads, approval challenge records with hash binding and expiry, audit event conventions, and the strongly-typed audit payload schema.

## Runtime Flow

- `ApprovalStore.cs` persists generic plan envelopes under `K8S_MCP_APPROVAL_ROOT`, computes SHA-256 hashes, refuses old raw-plan files with a re-request message, and writes approval audit events to `audit.jsonl`.
- `PlanEnvelope.cs`, `PlanEnvelope.Typed.cs`, and `PlanRequester.cs` model the generic approval envelope and the requester identity bound to it.
- `ApprovalChallengeStore.cs` creates, persists, updates, and queries challenge records — each tied to a plan hash, a requester subject, and a configurable TTL.
- `ApprovalConventions.cs` holds all shared constants: environment variable names, on-disk directory names, audit event names, challenge statuses, approval source labels, and diff change types.
- `AuditPayloads/PlanAuditPayloads.cs` and `AuditPayloads/ChallengeAuditPayloads.cs` define strongly-typed positional records for every approval-audit event, replacing the old anonymous types and locking the JSON wire shape tested by `AuditPayloadsTests`.
- `FixedTimeStringComparer.cs` provides constant-time SHA-256 comparison for drift detection.
- `ApprovalChallenge.cs` models the out-of-band approval ticket (id, plan hash, requester, expiry, status, approver).

## Important Contracts

- The approval root directory (`K8S_MCP_APPROVAL_ROOT`) is shared between the gateway and the downstream server. Every write goes through this directory.
- Pending plan files are generic envelopes. Domain-specific mutation intent and review evidence live inside the adapter payload; Kubernetes payload types live in `InfraGate.KubernetesAdapter`.
- Audit events use names from `ApprovalConventions.AuditEvents`. Payloads are typed `IPlanAuditPayload` or `IChallengeAuditPayload` records; their JSON keys under `JsonSerializerDefaults.Web` are the contract.
- Hash comparison uses `FixedTimeStringComparer` to avoid timing side-channels.
- Challenges are ephemeral (default TTL: 15 minutes) and single-use; a single plan can have many challenge attempts.
- [Design rationale: why plan and challenge are separate records](../../docs/why-separated-plan-from-challenge.md).

## Settings

Runtime environment variables, defaults, examples, and production guidance are documented in [docs/configuration.md](../../docs/configuration.md).

## Verification

The ApprovalStore and challenge lifecycle are tested from the projects that depend on them:

- `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj` — `ApprovalStoreTests.cs` covers hash matching, change protection, and plan lifecycle.
- `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj` — `GatewayApprovalServiceTests.cs` covers challenge creation, expiry, same-subject binding, and hash drift.
- `dotnet test tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj` — opt-in end-to-end tests proving the seven approval-flow safety properties through the full gateway/server stack.
