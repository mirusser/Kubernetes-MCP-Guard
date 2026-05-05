# Plan Verification: 21-Strong-Out-of-Band-Approval-Flow

Generated: 2026-05-05 | Source plan: `21-strong-out-of-band-approval-flow.md`

## Core Architecture

| Requirement | Status | Details |
|---|---|---|
| Create `InfraGate.Approvals` shared library | ✅ | `src/InfraGate.Approvals/` with 9 source files |
| Included in solution | ✅ | `InfraGate.slnx` line 4 |
| Move `K8sPlan` into shared library | ✅ | `src/InfraGate.Approvals/K8sPlan.cs` |
| Move `K8sObjectRef` into shared library | ✅ | `src/InfraGate.Approvals/K8sObjectRef.cs` |
| Move `ApprovalStore` into shared library | ✅ | `src/InfraGate.Approvals/ApprovalStore.cs` |
| Move approval result records into shared library | ✅ | `ApprovalPlanResult`, `ApprovedPlanResult`, `PendingPlanResult` |
| Move `FixedTimeStringComparer` into shared library | ✅ | `src/InfraGate.Approvals/FixedTimeStringComparer.cs` |
| Move approval storage conventions/constants into shared library | ✅ | `src/InfraGate.Approvals/ApprovalConventions.cs` |
| `InfraGate.McpServer` references `InfraGate.Approvals` | ✅ | `<ProjectReference>` in `.csproj` |
| `InfraGate.McpGateway` references `InfraGate.Approvals` | ✅ | `<ProjectReference>` in `.csproj` |
| Keep Kubernetes execution in McpServer | ✅ | `K8sManager.Apply.cs`, `K8sManager.Requests.cs` |

## Auth Changes

| Requirement | Status | Details |
|---|---|---|
| Remove static bearer authentication from Gateway | ✅ | Zero static-bearer/token code in source |
| `/mcp` uses OAuth/JWT only | ✅ | `RequireAuthorization` with JWT policy; `ForwardedAuthenticationScheme` returns JWT only |
| Add browser cookie auth for approval pages | ✅ | `InfraGateApprovalCookie` scheme + `InfraGateApprovalOAuth` OAuth/PKCE scheme |
| Use DevIssuer for local approval UI auth | ✅ | Pre-registered `infra-gate-approval-ui` client with redirect URI |
| External issuer must configure approval UI public client | ✅ | Configurable via `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID` and related env vars |

## `apply_approved_plan` Behavior

| Requirement | Status | Details |
|---|---|---|
| Approved hash exists → forward to downstream | ✅ | `EnsureApprovedOrCreateChallengeAsync` checks challenge store, then delegates |
| Approval missing → read pending plan and current hash from approval store | ✅ | Via `approvalStore.GetPendingPlanAsync()` |
| Create short-lived, single-use approval challenge bound to planId, planHash, requester | ✅ | `ApprovalChallengeStore.CreateAsync()` with 32 random byte hex ID + TTL |
| Return approval URL instead of MCP elicitation | ✅ | `FormatApprovalRequiredMessage()` returns approval URL |
| Client cannot approve, deny, or provide hash through MCP | ✅ | No MCP approval schema; no elicitation APIs used |

## Gateway Approval Endpoints

| Requirement | Status | Details |
|---|---|---|
| `GET /approvals/{challengeId}` shows pending plan | ✅ | `GatewayApprovalEndpoints` line ~25; requires browser auth |
| `POST /approvals/{challengeId}/approve` validates and writes approval | ✅ | Anti-forgery + auth validation + hash verification |
| `POST /approvals/{challengeId}/deny` records denial | ✅ | Anti-forgery + same-subject check |
| Challenges stored under `{root}/challenges/{id}.json` | ✅ | `ApprovalChallengeStore.ChallengeDirectory` |

## Challenge Record Fields

| Field | Status |
|---|---|
| challenge id | ✅ |
| plan id | ✅ |
| plan hash | ✅ |
| requester subject | ✅ |
| requester client id/auth context | ✅ (as `RequesterAuthenticationType`) |
| created/expiry timestamps | ✅ |
| status | ✅ (`pending`/`approved`/`denied`/`expired`) |
| approver subject | ✅ |
| decision timestamp | ✅ |

## Same-Subject Approval

| Requirement | Status | Details |
|---|---|---|
| Browser approver subject must match MCP requester subject | ✅ | `SameSubject()` check in `ValidatePendingChallengeAsync` |
| Reject if subject differs | ✅ | Returns error, audits `approval_challenge_rejected` |
| v1 same-user only (two-person deferred) | ✅ | No delegation/approval-chain logic present |

## Server-Side Changes

| Requirement | Status | Details |
|---|---|---|
| Remove server-side MCP elicitation for approvals | ✅ | Zero uses of `Elicit`, `elicitation`, `RequestElicitation`, `RequestSampling`, `CreateMessage`, `RequestUserInput` |
| Server applies only when approved hash file exists | ✅ | `GetApprovedPlanAsync()` checks approved file + hash match |
| Server returns approval-required/refused if missing/stale | ✅ | `"Refused: not approved"` or `"Refused: changed after approval"` |
| TOCTOU: recompute pending hash before writing approved hash | ✅ | `ApprovePendingPlanAsync` calls `GetPendingPlanAsync` and compares with `FixedTimeStringComparer` |

## New Gateway Options

| Option | Env Var | Default | Status |
|---|---|---|---|
| Approval UI base URL | `INFRA_GATE_APPROVAL_BASE_URL` | From request | ✅ |
| Approval challenge TTL | `INFRA_GATE_APPROVAL_CHALLENGE_TTL_SECONDS` | 900s | ✅ |
| OAuth authority | `INFRA_GATE_OAUTH_AUTHORITY` | required | ✅ |
| OAuth resource | `INFRA_GATE_OAUTH_RESOURCE` | `http://127.0.0.1:3001/mcp` | ✅ |
| OAuth scope | `INFRA_GATE_OAUTH_SCOPE` | `mcp:tools` | ✅ |
| OAuth authorization endpoint | `INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT` | `{authority}/authorize` | ✅ |
| OAuth token endpoint | `INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT` | `{authority}/token` | ✅ |
| Approval UI client id | `INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID` | `infra-gate-approval-ui` | ✅ |
| Approval callback path | `INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH` | `/approvals/oauth/callback` | ✅ |

## DevIssuer Changes

| Requirement | Status | Details |
|---|---|---|
| Pre-registered approval UI client id | ✅ | `infra-gate-approval-ui` via `INFRA_GATE_DEV_ISSUER_APPROVAL_CLIENT_ID` |
| Pre-registered redirect URI | ✅ | `http://127.0.0.1:3001/approvals/oauth/callback` via `INFRA_GATE_DEV_ISSUER_APPROVAL_REDIRECT_URI` |

---

## Implementation Gaps

### 1. ❌ Expired challenge test not covered

**Plan requires**: "expired challenge cannot approve"

**Current state**: Source code handles expired challenges (`GatewayApprovalService.cs:207` — sets status to `Expired`, returns `"Approval challenge expired."`), but no test creates a challenge with a short/past TTL and verifies it is rejected at approve time.

**What needs to be done**: Add a test in `tests/InfraGate.McpGateway.Tests/UnitTests/GatewayApprovalServiceTests.cs` that:
- Creates a challenge with a very short TTL (e.g., `TimeSpan.Zero` or negative)
- Attempts to approve it via `ApproveChallengeAsync`
- Asserts the result is `Succeeded=false` with a message about expiration
- Verifies the challenge status was set to `"expired"`

---

### 2. ❌ Compromised-client scenario not covered

**Plan requires**: "compromised-client scenario: MCP-provided approval content is ignored because no MCP approval payload is accepted"

**Current state**: No adversarial test simulates a compromised MCP client attempting to manipulate plan IDs, challenge IDs, hashes, or forge approvals outside the normal flow.

**What needs to be done**: Add tests in the gateway integration tests (`tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs`) covering:
- Client calls `apply_approved_plan` with a non-existent planId → returns meaningful refusal
- Client calls `apply_approved_plan` with a planId for a plan they did not create → returns approval URL but same-subject check prevents approval in browser
- Client calls `apply_approved_plan` with a forged/speculative challengeId (not applicable since challenges are server-created)
- Verification that no MCP tool accepts approval payloads (hash, decision, etc.) — confirm no such schema exists on any tool

---

### 3. ❌ `FixedTimeStringComparer` dedicated test not covered

**Plan requires**: "approval store constant-time hash comparison remains covered"

**Current state**: `FixedTimeStringComparer` is exercised indirectly through `ApprovalStore` tests, but has no dedicated unit test verifying:
- Correct comparison for matching strings
- Correct comparison for non-matching strings
- Timing-attack resistance (fixed-time property)

**What needs to be done**: Add tests in `tests/InfraGate.McpServer.Tests/UnitTests/ApprovalStoreTests.cs` (or a new `FixedTimeStringComparerTests.cs`):
- `Equals_ReturnsTrue_ForIdenticalStrings`
- `Equals_ReturnsFalse_ForDifferentStrings`
- `Equals_ReturnsFalse_ForDifferentLengthStrings`

---

### 4. ❌ Static bearer removal negative test not covered

**Plan requires**: "static bearer authentication is removed"

**Current state**: Source code has no static bearer code, but no test explicitly verifies that:
- A request with `Authorization: Bearer change-me` to `/mcp` returns 401
- The old `INFRA_GATE_GATEWAY_BEARER_TOKEN` env var is no longer read
- No static token handler is registered in the auth pipeline

**What needs to be done**: Add a test in `tests/InfraGate.McpGateway.Tests/` that:
- Sends a request with `Authorization: Bearer change-me` to the MCP endpoint
- Asserts 401 Unauthorized response
- Confirms the old static bearer path is absent

---

### 5. ⚠️ `AGENTS.md` references stale "static bearer auth"

**Plan requires**: "docs no longer describe static bearer as a supported Gateway mode"

**Current state**: `AGENTS.md` line 70 reads: *"static bearer auth, OAuth JWT auth, MCP protected-resource metadata, and audit identity resolution"*

**What needs to be done**: Edit `AGENTS.md` line 70 to remove the "static bearer auth," prefix, changing it to: *"OAuth JWT auth, MCP protected-resource metadata, and audit identity resolution"*

---

### 6. ⚠️ Skill file references stale `change-me` token

**Plan requires**: "docs no longer describe static bearer as a supported Gateway mode"

**Current state**: `.agents/skills/infragate-mcp-gateway/SKILL.md` has 4 references to `INFRA_GATE_GATEWAY_BEARER_TOKEN` / `change-me`:
- Line 16: Demo token description
- Line 23: Token preference instructions
- Line 40: Curl example with bearer token
- Line 86: Env var export `INFRA_GATE_GATEWAY_BEARER_TOKEN="change-me"`

**What needs to be done**: Edit `.agents/skills/infragate-mcp-gateway/SKILL.md` to:
- Remove or replace all references to `INFRA_GATE_GATEWAY_BEARER_TOKEN` and `change-me`
- Replace the curl example and env var instructions with OAuth/JWT authentication flow instead
- Update the demo auth description to describe OAuth login, not static bearer tokens

---

## Verdict

**The plan is substantially implemented.** All 11 "Key Changes" and all "Public Interfaces" defined in the plan are in place and functioning. The out-of-band approval flow works end-to-end: Gateway creates challenges, browser OAuth authenticates, approval UI renders pending plans from disk, approve/deny writes expected files, and the Gateway forwards to the downstream server only after an approved challenge is confirmed.

**6 gaps remain** (2 non-blocking documentation items + 4 test gaps): the docs stales are quick text edits; the test gaps require adding dedicated test methods for expired challenge handling, compromised-client adversary scenarios, constant-time hash comparison, and a static-bearer-removal negative test.
