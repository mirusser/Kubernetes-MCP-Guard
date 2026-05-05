# Plan: Bind Elicitation Approval to Plan Hash

## Problem

The approval-gated mutation flow uses MCP elicitation to request user confirmation before applying a Kubernetes plan. The SHA-256 hash of the pending plan is displayed in the elicitation prompt message, but the `PlanApprovalInput` structured response schema only contains `Approve` (bool) and `PlanId` (string) — the hash is not echoed back.

This means:
1. The hash shown to the user is purely informational; there is no cryptographic binding between what the user approves and the actual plan content.
2. A compromised or prompt-injected MCP client could approve any plan by sending `{ approve: true, planId: "..." }` while ignoring the hash.
3. The server-side hash verification protects against file TOCTOU, but not against a client that never validates the hash.

## Mitigation

### 1. Add `PlanHash` to `PlanApprovalInput` and validate server-side

**File:** `src/InfraGate.McpServer/K8sManager.Apply.cs`

- Add `PlanHash` property to `PlanApprovalInput` with `[Description("Echo the exact Plan hash from the approval prompt.")]`.
- Update `ApproveMatchingPlanAsync` to validate `approval.PlanHash` against the expected `hash` parameter using `StringComparison.Ordinal`.
- Reject with a clear denial message: `"Plan '{planId}' approval did not echo the matching plan hash."`.
- Update `FormatApprovalRequest` to explicitly instruct: *"Respond with approve=true, the exact PlanId, and the exact Plan hash shown above."*

### 2. Replace `StringComparer.OrdinalIgnoreCase` with constant-time comparison

**File:** `src/InfraGate.McpServer/ApprovalStore.cs`

- Add `FixedTimeEquals(string left, string right)` helper using `CryptographicOperations.FixedTimeEquals`.
- Replace both hash comparisons in `ApprovePendingPlanAsync` and `GetApprovedPlanAsync` with `FixedTimeEquals`.

### 3. Update gateway integration test elicitation content

**File:** `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs`

- Add `PlanHashPattern` generated regex (`@"Plan hash:\s+(?<hash>[0-9a-f]+)"`) alongside existing `PlanIdPattern`.
- Update `ElicitResult.Content` in `ApplyApprovedPlan_ForwardsAcceptedAndDeclinedElicitationThroughGateway` to include `planHash` extracted via the new regex.

### 4. Add unit test for hash mismatch

**File:** `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs`

- Add `ApprovePendingPlanAsync_DeniesWrongHash` test that passes a deliberately wrong hash string to verify `FixedTimeEquals` rejection.

### 5. Update architecture diagram

**File:** `docs/full-architecture-diagram.md`

- Update the Approval-Gated Mutation sequence diagram (elicitation step) to show the hash is echoed back in the approval response and validated server-side.

## Verification

```bash
dotnet build InfraGate.slnx
dotnet test InfraGate.slnx --no-build
dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --no-build \
  --filter "FullyQualifiedName~ApplyApprovedPlan_ForwardsAcceptedAndDeclinedElicitationThroughGateway"
```

## Files Touched

| File | Change |
|------|--------|
| `src/InfraGate.McpServer/K8sManager.Apply.cs` | Add `PlanHash` to `PlanApprovalInput`, validate in `ApproveMatchingPlanAsync`, update `FormatApprovalRequest` |
| `src/InfraGate.McpServer/ApprovalStore.cs` | Add `FixedTimeEquals` helper, replace both hash comparisons |
| `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs` | Add `PlanHashPattern`, include `planHash` in elicitation content |
| `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs` | Add `ApprovePendingPlanAsync_DeniesWrongHash` test |
| `docs/full-architecture-diagram.md` | Update elicitation step to show hash echo and validation |
