# Implementation Plan: Reduce Model-Visible Sensitive Output

## Summary
Implement Section 6 by minimizing successful `request_*` MCP responses while preserving full pending-plan data for hash binding, apply-time validation, and browser approval review. The model-visible response will expose only `PlanId`, `Status: pending_gateway_approval`, operation, namespace, object refs, policy summary, risk summary, and next-step guidance.

## Implementation Tasks

### Task 1: Compact Server Request Responses
**Description:** Update `K8sManager.Requests.cs` to remove sensitive/internal fields from successful request-plan responses.

**Acceptance criteria:**
- `request_*` responses do not include `Pending file:`, `Plan hash:`, `Dry-run:`, `Diff:`, raw manifests, or ConfigMap values.
- Response includes `Policy:` and `Risk: medium`.
- Pending plan JSON still stores manifest, hash-relevant data, dry-run, diffs, and policy findings.

**Verification:** `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`

**Dependencies:** None

**Likely files:** `K8sManager.Requests.cs`, `K8sConventions.cs`

### Task 2: Add Policy Summary Helpers
**Description:** Add simple local formatting for policy summaries without introducing a risk engine or new public contract.

**Acceptance criteria:**
- Apply with no warnings returns `Policy: passed`.
- Apply with one or more warnings returns `Policy: passed_with_1_warning` or `passed_with_N_warnings`.
- Delete, scale, restart, and set-image return `Policy: not_applicable`.

**Verification:** focused server request tests pass.

**Dependencies:** Task 1

**Likely files:** `K8sManager.Requests.cs`, `K8sManagerRequestTests.cs`, `K8sManagerSetImageTests.cs`

### Task 3: Harden Gateway Fallback Sanitization
**Description:** Ensure legacy or compromised downstream output cannot pass sensitive metadata through the gateway sanitizer.

**Acceptance criteria:**
- `Pending file:`, `Approval file:`, and `Plan hash:` are removed from `OperationalLinePattern`.
- Lines beginning with those labels are stripped/redacted even if they contain no prompt-injection text.
- Existing manifest-block redaction still works.

**Verification:** gateway sanitizer and guarded-runner tests pass.

**Dependencies:** None

**Likely files:** `PromptInjectionGuard.cs`, `ResponseSanitizationTests.cs`, `GuardedToolRunnerTests.cs`

### Task 4: Update Integration Expectations
**Description:** Update gateway/downstream smoke coverage to assert the compact response and sensitive-field absence.

**Acceptance criteria:**
- Real downstream request-plan smoke test expects `Status: pending_gateway_approval`, `Policy:`, and `Risk: medium`.
- It asserts absence of `Pending file:`, `Plan hash:`, raw manifest blocks, and ConfigMap values.
- Approval-page tests still confirm browser-only plan hash, dry-run, policy findings, and diffs.

**Verification:** `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

**Dependencies:** Tasks 1 and 3

**Likely files:** `GatewayHttpMcpIntegrationTests.cs`

### Task 5: Refresh Narrow Docs
**Description:** Update policy/request documentation only where the old response behavior is now stale.

**Acceptance criteria:**
- Docs say model-visible responses include policy summaries, not full policy warning details.
- Docs still say detailed findings are stored in pending plans and rendered in browser approval.

**Verification:** docs reviewed against implemented response shape.

**Dependencies:** Tasks 1-4

**Likely files:** `src/InfraGate.McpServer/Policy/README.md`

## Checkpoints
- After Tasks 1-2: server tests pass and all request response assertions reflect the compact shape.
- After Tasks 3-4: gateway tests pass and fallback redaction still catches legacy manifest echoes.
- After Task 5: optional full check with `dotnet test InfraGate.slnx`.

## Assumptions
- `GatewayApprovalService.cs` approval-required output stays unchanged; its `Plan hash:` is intentional for out-of-band approval binding.
- `Risk: medium` is fixed for this PR until later roadmap risk-scoring work.
- No MCP tool names, arguments, annotations, pending-plan schema, or approval-browser contracts change.
