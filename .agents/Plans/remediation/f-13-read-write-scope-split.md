# Remediation Plan: F-13 — Read / Write Scope Split

**Source:** [Security Audit F-13](/.agents/Plans/loose/security-audit.md#f-13)
**Date:** 2026-06-05
**Severity:** High
**Current Status:** ⚠️ Partially mitigated — agent-role scopes exist, no human read/write split

---

## Overview

The security audit correctly identifies that the gateway has a single monolithic `mcp:tools` scope granting access to ALL operations — read-only inspection AND destructive mutations. While agent-role scopes (`mcp:tools.readonly`, `mcp:tools.propose`, `mcp:tools.execute`) provide proper segregation for the Observer/Planner/Executor agent path, the human operator path still relies on `mcp:tools` as a master key. The README quickstart explicitly tells users to request `scopes = ["mcp:tools"]`, giving every human operator full mutation power even when they only want to inspect the cluster.

This plan introduces two new scopes: `mcp:tools.read` (all read-only tools) and `mcp:tools.write` (all mutation tools), enabling operators to choose least-privilege access. These follow the existing `mcp:tools.*` naming convention used by the agent scopes (`mcp:tools.readonly`, `mcp:tools.propose`, `mcp:tools.execute`). The existing `mcp:tools` scope and agent-role scopes remain for backward compatibility.

### What the Investigation Found

**Working correctly:**
- Four scope constants defined in `GatewayAuthConventions.cs` and `McpGatewayConventions.ToolScopeRequirements`
- `ToolScopeCatalog` correctly maps each tool to required scopes
- `ToolScopeGuard` enforces scope at runtime with audit logging
- `GatewayToolDispatcher` filters tool listing and checks scope before execution
- Protected resource metadata (`/.well-known/oauth-protected-resource`) advertises all scopes
- Keycloak realm defines all scopes and assigns them to agent clients
- Agent-role assignment: Observer → `mcp:tools.readonly`, Planner → `mcp:tools.propose + mcp:tools.readonly`, Executor → `mcp:tools.execute`

**The gap — `mcp:tools` as master key:**

| Tool | Currently Accepts |
|---|---|
| All 8 read-only diagnostic tools | `mcp:tools` OR `mcp:tools.readonly` |
| All 5 `request_*` mutation tools | `mcp:tools` *only* |
| `propose_plan` | `mcp:tools` OR `mcp:tools.propose` |
| `execute_approved_plan` | `mcp:tools` OR `mcp:tools.execute` |
| `wait_for_plan_approval` | `mcp:tools` OR `mcp:tools.execute` |
| `get_plan_status` | `mcp:tools` OR `mcp:tools.readonly` |

A `mcp:tools` token can do **everything**. A human who only wants to run `get_k8s_status` cannot be scoped down — they must take `mcp:tools` which also grants `request_scale_deployment` and `execute_approved_plan`.

---

## Architecture Decisions

- **AD-1: Additive, not breaking.** Introduce `mcp:tools.read` and `mcp:tools.write` alongside existing scopes, following the `mcp:tools.*` naming convention. `mcp:tools` retains full power for backward compatibility; deprecate over time, not immediately.
- **AD-2: `mcp:tools.write` covers all mutations.** `mcp:tools.write` grants access to `request_*`, `propose_plan`, `execute_approved_plan`, and `wait_for_plan_approval`. It does not replace the agent-role scopes (`mcp:tools.propose`, `mcp:tools.execute`) — those remain as narrower alternatives for service identities.
- **AD-3: `mcp:tools.read` is the human inspection scope.** `mcp:tools.read` grants access to all 8 diagnostic tools + `get_plan_status`. Read-only downstream tools already accept `mcp:tools.readonly`; `mcp:tools.read` is added as an alternative.
- **AD-4: Keep `ToolScopeRequirements` convention class as single source of truth.** All scope string constants live in `McpGatewayConventions.ToolScopeRequirements`. No scattering.
- **AD-5: Protected resource metadata advertises both new scopes.** The `/.well-known/oauth-protected-resource` endpoint must include `mcp:tools.read` and `mcp:tools.write` so MCP clients can discover and request them.
- **AD-6: No client-side enforcement change needed for agents.** Observer/Planner/Executor continue using their existing agent-role scopes. The `mcp:tools.read` and `mcp:tools.write` scopes are additive infrastructure, not a replacement.

---

## Task List

### Phase 1: Define the New Scopes

- [ ] **Task 1:** `src/InfraGate.McpGateway/McpGatewayConventions.cs` — Add `mcp:tools.read` and `mcp:tools.write` constants to `ToolScopeRequirements` nested class:
  ```csharp
  public const string ReadScope = "mcp:tools.read";
  public const string WriteScope = "mcp:tools.write";
  ```
- [ ] **Task 2:** `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs` — Add `DefaultReadToolsOAuthScope = "mcp:tools.read"` and `DefaultWriteToolsOAuthScope = "mcp:tools.write"` constants. Add both to `AcceptedScopes` in `GatewayAuthentication.cs`.

### Checkpoint: Constants Defined
- [ ] `ToolScopeRequirements.ReadScope` exists and equals `"mcp:tools.read"`
- [ ] `ToolScopeRequirements.WriteScope` exists and equals `"mcp:tools.write"`
- [ ] `GatewayAuthConventions.DefaultReadToolsOAuthScope` and `DefaultWriteToolsOAuthScope` exist
- [ ] `AcceptedScopes` includes both new scopes
- [ ] Build passes: `dotnet build src/InfraGate.McpGateway/`

### Phase 2: Update Tool Scope Mappings

- [ ] **Task 3:** `src/InfraGate.McpGateway/McpTransport/Dispatch/ToolScopeCatalog.cs` — Update `GetSynthesizedScopes` and `GetRequiredScopes` to accept `mcp:tools.read` for read-only tools and `mcp:tools.write` for mutation tools.
  - `request_*` tools → add `WriteScope`
  - `execute_approved_plan` → add `WriteScope`
  - `get_plan_status` → add `ReadScope`
  - `wait_for_plan_approval` → add `WriteScope`
  - `propose_plan` → add `WriteScope`
  - Downstream read-only tools (via `GetRequiredScopes` with `hasReadOnlyHint=true`) → add `ReadScope`
  - Downstream destructive tools (via `GetRequiredScopes` with `hasReadOnlyHint=false`) → add `WriteScope`

### Checkpoint: Scope Mappings Updated
- [ ] `get_k8s_status` (and all read-only tools) accessible with `mcp:tools.read` token
- [ ] `request_scale_deployment` (and all `request_*` tools) accessible with `mcp:tools.write` token
- [ ] `mcp:tools` still works everywhere (backward compatibility)
- [ ] Build passes

### Phase 3: Keycloak Realm Configuration

- [ ] **Task 4:** `deploy/keycloak/infra-gate-realm.json` — Add `mcp:tools.read` and `mcp:tools.write` as new client scopes with appropriate audience mappers (targeting `http://127.0.0.1:3001/mcp`).
  - `mcp:tools.read`: description "Read-only MCP tool access for human operators inspecting the cluster — follows the mcp:tools.* naming convention"
  - `mcp:tools.write`: description "Mutation MCP tool access for human operators performing changes — follows the mcp:tools.* naming convention"
  - Both: `include.in.token.scope = true`, `display.on.consent.screen = true`
  - Both: audience mapper → `http://127.0.0.1:3001/mcp`
- [ ] **Task 5:** `deploy/keycloak/infra-gate-realm.json` — Add both scopes to the `infra-gate-mcp-client` client's `allowed-client-scopes` and `defaultOptionalClientScopes`.

### Checkpoint: Keycloak Configured
- [ ] `mcp:tools.read` and `mcp:tools.write` exist as client scopes
- [ ] Both are available to the MCP public client
- [ ] Keycloak realm JSON is valid (no duplicate names)

### Phase 4: Protected Resource Metadata

- [ ] **Task 6:** `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs` — Verify `AcceptedScopes` already feeds into `ProtectedResourceMetadata` via `ConfigureMcpOptions`. Confirm no code change needed (the `AcceptedScopes` array already includes all new scopes from Task 2, and `DistinctValues` in `ConfigureMcpOptions` iterates them into `metadata.ScopesSupported`).

### Checkpoint: Metadata Endpoint
- [ ] `GET /.well-known/oauth-protected-resource` returns `mcp:tools.read` and `mcp:tools.write` in `scopes_supported`
- [ ] Existing scopes (`mcp:tools`, `mcp:tools.readonly`, etc.) still present

### Phase 5: Documentation and Quickstart

- [ ] **Task 7:** `docs/tool-permissions.md` — Replace the note on line 76 with a summary listing what `mcp:tools.read` and `mcp:tools.write` cover. Update the Common Properties section.
- [ ] **Task 8:** `README.md` — Update the Codex CLI quickstart to show `mcp:tools.read` as the recommended scope for read-only inspection:
  ```toml
  [mcp_servers.infra-gate]
  url = "http://127.0.0.1:3001/mcp"
  oauth_resource = "http://127.0.0.1:3001/mcp"
  scopes = ["mcp:tools.read"]
  ```
  Add a note: "Use `mcp:tools.write` for sessions where you intend to create and apply mutation plans."
- [ ] **Task 9:** `src/InfraGate.McpGateway.Auth/README.md` — Update the auth README to document the new scopes and their intended use.

### Checkpoint: Documentation Updated
- [ ] `docs/tool-permissions.md` reflects the read/write split
- [ ] `README.md` quickstart recommends least-privilege scopes
- [ ] `src/InfraGate.McpGateway.Auth/README.md` documents new scopes
- [ ] No stale references to "single scope" or "no read/write split" remain in docs

### Phase 6: Verification

- [ ] **Task 10:** Run the full test suite to ensure backward compatibility:
  ```bash
  dotnet test InfraGate.slnx --filter "FullyQualifiedName~ToolScopeGuard|FullyQualifiedName~ToolScopeCatalog|FullyQualifiedName~GatewayAuth"
  ```
- [ ] **Task 11:** `tests/InfraGate.McpGateway.Tests/UnitTests/ToolScopeGuardTests.cs` — Add or extend tests:
  - `RequireAnyScopeAsync_AllowsMcpToolsRead_ForReadOnlyTool` — `mcp:tools.read` token passes read-only tool check
  - `RequireAnyScopeAsync_DeniesMcpToolsRead_ForRequestTool` — `mcp:tools.read` token is denied for `request_*`
  - `RequireAnyScopeAsync_AllowsMcpToolsWrite_ForRequestTool` — `mcp:tools.write` token passes `request_*` check
  - `RequireAnyScopeAsync_AllowsMcpToolsWrite_ForExecuteApprovedPlan` — `mcp:tools.write` token passes execute check
  - `IsVisibleTo_HidesMutationTools_WithMcpToolsRead` — tool listing hides mutation tools for `mcp:tools.read` caller
- [ ] **Task 12:** Manual smoke test — start the stack with `make quickstart`, authenticate with `mcp:tools.read`, verify `get_k8s_status` works and `request_scale_deployment` is denied.

### Checkpoint: Complete
- [ ] All tests pass
- [ ] Build succeeds
- [ ] `mcp:tools.read` token can call read-only tools but NOT mutation tools
- [ ] `mcp:tools.write` token can call all tools
- [ ] `mcp:tools` token still works everywhere (backward compatibility)
- [ ] Observer/Planner/Executor continue working with their existing scopes

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Breaking existing MCP client configs that hardcode `mcp:tools` | High | `mcp:tools` is NOT removed — only new scopes are added. No existing config breaks. |
| Keycloak realm change conflicts with local setup | Low | Back up realm config; `make quickstart` rebuilds Keycloak from scratch if needed. |
| Missed a tool in scope mapping | Medium | `ToolScopeCatalog` is centralized; all tools flow through `GetSynthesizedScopes` or `GetRequiredScopes`. Tests cover the matrix. |
| Protected resource metadata doesn't auto-update | Low | Task 6 verifies `AcceptedScopes` feeds `metadata.ScopesSupported` — confirmed in code review. |

## Open Questions

- Should `mcp:tools` eventually be deprecated and removed? If so, what's the migration window? (Proposal: 2 releases after `mcp:tools.read`/`mcp:tools.write` introduction.)
- Should `mcp:tools.write` be split further into `mcp:tools.write.plan` (create plans) and `mcp:tools.write.execute` (execute plans), or is the existing `mcp:tools.propose` / `mcp:tools.execute` split sufficient for the agent path?

## Files to Touch

| Phase | File | Change |
|---|---|---|
| 1 | `src/InfraGate.McpGateway/McpGatewayConventions.cs` | Add `ReadScope = "mcp:tools.read"`, `WriteScope = "mcp:tools.write"` |
| 1 | `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs` | Add `DefaultReadToolsOAuthScope`, `DefaultWriteToolsOAuthScope` |
| 1 | `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs` | Add to `AcceptedScopes` array |
| 2 | `src/InfraGate.McpGateway/McpTransport/Dispatch/ToolScopeCatalog.cs` | Add `mcp:tools.read`/`mcp:tools.write` to all relevant scope arrays |
| 3 | `deploy/keycloak/infra-gate-realm.json` | Add `mcp:tools.read`, `mcp:tools.write` client scopes |
| 5 | `docs/tool-permissions.md` | Update scope documentation |
| 5 | `README.md` | Update quickstart scopes |
| 5 | `src/InfraGate.McpGateway.Auth/README.md` | Document new scopes |
| 6 | `tests/InfraGate.McpGateway.Tests/UnitTests/ToolScopeGuardTests.cs` | Add scope tests |

## References

- [Security Audit F-13](/.agents/Plans/loose/security-audit.md#f-13)
- [Tool Permissions Matrix](/docs/tool-permissions.md)
- [Code Standards](/.agents/skills/code-standards/SKILL.md)
- [Repo Onboarding](/.agents/skills/repo-onboarding/SKILL.md)
