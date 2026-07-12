# Verification Report and Remediation Plan: F-07 JWT Bearer Replay Mitigation

## Verification Report

### Completeness
| Plan item | Status | Evidence |
|---|---|---|
| Task 1: Add F-07 authentication regression tests | Done | `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayAuthenticationTests.cs`, `DpopProofTestFactory.cs` |
| Task 2: Add DPoP validation contract and replay-store contract | Done | `DpopProofValidator.cs`, `IDpopProofReplayStore.cs`, `InMemoryDpopProofReplayStore.cs` |
| Task 3: Wire DPoP validation into Gateway bearer authentication | Done | `GatewayAuthentication.cs`, `GatewayAuthOptions.cs`, `GatewayAuthConventions.cs` |
| Task 4: Add DPoP support to controlled client-credentials callers | Done | `ClientCredentialsTokenProvider.cs`, `ClientCredentialsDpopProofFactory.cs` |
| Task 5: Configure Keycloak DPoP enforcement for controlled clients | Done | `infra-gate-realm.json`, `KeycloakIntegrationTests.cs` |
| Task 6: Decide and implement external MCP client compatibility behavior | Partial | Code implemented, but documentation (`docs/MCP-compliance.md`, `docs/mcp-clients-quirks.md`) missing |
| Task 7: Remove avoidable token persistence from approval OAuth cookies | Done | `GatewayAuthentication.cs` |
| Task 8: Add production replay-store decision and deployment guardrails | Partial | Code implemented, but documentation missing |
| Task 9: Update audit status and operational documentation | Missing | `.agents/Plans/loose/security-audit.md` and related docs not updated |

### Scope drift
Several unrelated files were modified or created during this feature work:
- `.agents/Plans/loose/rfcs-rag-mcp.md`
- `.agents/Plans/rfc-rag-mcp-plan.md`
- `src/InfraGate.RunProfiles/...`
- `tests/InfraGate.RunProfiles.Tests/...`
- `src/InfraGate.McpGateway/Guardrails/SanitizingToolCaller.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/SanitizingToolCallerTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/ToolScopeCatalogTests.cs`

### Findings
#### Blockers
None.

#### Important
- **Missing Documentation Updates** — The feature was fully implemented in code, but all documentation steps from Tasks 6, 8, and 9 were completely skipped. This leaves the security audit finding technically unmitigated on paper and hides residual risk from operators.
- **Scope Drift** — The commit includes unrelated RAG MCP RFCs, RunProfile changes, and Guardrails updates. These should be separated from the F-07 security mitigation commit.

### Tests
- Ran: All test tiers via `./scripts/run-tests.sh`
- Failures: None. The implementation is functionally sound and passes all existing tests.

---

## Remediation Plan

### Overview
This plan addresses the missing documentation and scope drift identified during the verification of F-07 JWT Bearer Replay Mitigation.

### Task List

#### Phase 1: Documentation and Audit Updates
- [ ] Task 1: Document external MCP client compatibility behavior
- [ ] Task 2: Document production replay-store decision and deployment guardrails
- [ ] Task 3: Update F-07 audit status in `security-audit.md`

#### Phase 2: Scope Drift Cleanup
- [ ] Task 4: Separate or revert unrelated scope drift

## Task 1: Document external MCP client compatibility behavior

**Description:** F-07 Task 6 required updating documentation to reflect which external MCP clients support DPoP and the compatibility behavior implemented.

**Acceptance criteria:**
- [ ] `docs/MCP-compliance.md` is updated.
- [ ] `docs/mcp-clients-quirks.md` is updated.

**Verification:**
- [ ] Review documentation changes for accuracy against the implemented Keycloak realm.

**Dependencies:** None

**Files likely touched:**
- `docs/MCP-compliance.md`
- `docs/mcp-clients-quirks.md`

**Estimated scope:** Small: 2 files

## Task 2: Document production replay-store decision and deployment guardrails

**Description:** F-07 Task 8 required updating architecture and configuration docs to reflect the in-memory DPoP proof replay store decision and its limitations in multi-replica environments.

**Acceptance criteria:**
- [ ] `docs/configuration.md` is updated.
- [ ] `docs/architecture.md` is updated.

**Verification:**
- [ ] Read the documentation to ensure the multi-replica limitation of `InMemoryDpopProofReplayStore` is clearly stated.

**Dependencies:** None

**Files likely touched:**
- `docs/configuration.md`
- `docs/architecture.md`

**Estimated scope:** Small: 2 files

## Task 3: Update F-07 audit status in security-audit.md

**Description:** F-07 Task 9 required updating the security audit finding to reflect the completed scope.

**Acceptance criteria:**
- [ ] `.agents/Plans/loose/security-audit.md` is updated to mark F-07 as mitigated or partially mitigated.
- [ ] `tests/InfraGate.McpGateway.KeycloakTests/README.md` is updated with DPoP verification commands.

**Verification:**
- [ ] Verify `security-audit.md` correctly reflects the implemented state.

**Dependencies:** Tasks 1, 2

**Files likely touched:**
- `.agents/Plans/loose/security-audit.md`
- `tests/InfraGate.McpGateway.KeycloakTests/README.md`

**Estimated scope:** Small: 2 files

## Task 4: Separate or revert unrelated scope drift

**Description:** Unrelated changes to RunProfiles, SanitizingToolCaller, and RAG MCP RFCs were staged. These should be separated from the F-07 mitigation commit.

**Acceptance criteria:**
- [ ] The `f-07-jwt-bearer-replay-dpop-mitigation` commit/PR does not include the drift files.
- [ ] The drift files are moved to a separate branch or un-staged.

**Verification:**
- [ ] `git status` shows only F-07 related files.

**Dependencies:** None

**Estimated scope:** Medium: Git operations
