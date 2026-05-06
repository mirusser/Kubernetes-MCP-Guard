# MCP Spec Compliance: Authorization (2025-11-25) & Elicitation (draft)

**Date:** 2026-05-06
**Scope:** Gateway auth flow + approval plan flow against two MCP specifications.
**Related:** [security-audit.md](security-audit.md) (pre-existing findings), [from-elicitation-to-oob-approval.md](../../articles/from-elicitation-to-oob-approval.md) (design rationale for removing elicitation).

---

## 1. Authorization Spec — `2025-11-25/basic/authorization`

**Overall verdict: compliant on all MUST requirements. One SHOULD gap and one non-implemented optional feature.**

### Compliance Table

| Requirement | Level | Status | Evidence |
|---|---|---|---|
| Protected Resource Metadata (RFC 9728) at `/.well-known/oauth-protected-resource` | **MUST** | ✅ | `GatewayAuthentication.cs:140-150` — `.AddMcp()` serves `Resource`, `AuthorizationServers`, `ScopesSupported` |
| `resource_metadata` in 401 `WWW-Authenticate` | **MUST** (one of two discovery mechanisms) | ✅ | SDK's `McpAuthenticationDefaults.AuthenticationScheme` includes `resource_metadata` URL. Verified by test at `GatewayAuthenticationTests.cs:33-36`. |
| `scope` in 401 `WWW-Authenticate` for initial discovery | **SHOULD** | ⚠️ Missing | 401 contains only `resource_metadata` — no `scope`. Scope guidance only appears in 403 `insufficient_scope` responses. |
| 403 `error="insufficient_scope"` + `scope` + `resource_metadata` | **SHOULD** | ✅ | `GatewayAuthentication.cs:124-134` — `OnForbidden` handler calls `BuildInsufficientScopeChallenge` (line 224-230). Verified by test at line 119-138. |
| Authorization Server metadata (RFC 8414 or OIDC Discovery) | **MUST** (at least one) | ✅ | DevIssuer serves both `/.well-known/oauth-authorization-server` and `/.well-known/openid-configuration` (`DevIssuerApplication.cs:19-24`) |
| Resource parameter in auth & token requests (RFC 8707) | **MUST** | ✅ | DevIssuer validates and binds `resource` param; JWT `aud` claim is bounded to the exact resource URI |
| PKCE with S256 | **MUST** | ✅ | DevIssuer enforces `code_challenge_method=S256`; metadata advertises `code_challenge_methods_supported: ["S256"]` |
| Bearer token in `Authorization` header (not query string) | **MUST** | ✅ | ASP.NET Core JWT bearer middleware |
| Token audience validation | **MUST** | ✅ | `ValidateAudience = true`, custom `AudienceValidator` with `TrimTrailingSlash` comparison (`GatewayAuthentication.cs:209-220`) |
| Token lifetime validation | **MUST** | ✅ | `ValidateLifetime = true` |
| Token signature validation | **MUST** | ✅ | `ValidateIssuerSigningKey = true` |
| No token passthrough to downstream | **MUST NOT** | ✅ | Gateway terminates JWT, starts stdio `DownstreamMcpClient` — no token forwarding to subprocess |
| Reject tokens issued for other resources | **MUST** | ✅ | Audience validator ensures `aud` matches configured resource |
| Client ID Metadata Documents | **SHOULD** | ❌ | DevIssuer does not support URL-based `client_id` resolution. Uses dynamic registration (`POST /register`) or static client IDs instead. |
| Dynamic Client Registration (RFC 7591) | **MAY** | ✅ | DevIssuer has `/register` endpoint with loopback redirect URI validation |
| HTTPS for authorization server endpoints | **MUST** | ⚠️ Dev exception | DevIssuer uses HTTP (localhost-only). Deliberate dev exception via `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`. |
| Redirect URI validation (open redirection) | **MUST** | ✅ | `DevIssuerStore.ClientAllowsRedirectUri` validates loopback HTTP URIs only (`IsLoopbackHttpUri`) |
| State parameter in auth code flow | **SHOULD** | ✅ | Standard OAuth state parameter used |
| Token revocation / introspection | Not required by spec | ❌ | Not implemented (see F-09 in security audit) |

### Gap: `scope` Missing from 401 WWW-Authenticate

The spec says (Protected Resource Metadata Discovery):

> MCP servers **SHOULD** include a `scope` parameter in the `WWW-Authenticate` header [...] to indicate the scopes required for accessing the resource.
> If the `scope` parameter is absent, clients **SHOULD** apply the fallback behavior [fetching `scopes_supported` from the metadata document].

The gateway's 401 response includes only `resource_metadata` — `scope` appears only in 403 `insufficient_scope` responses. This is handled by the SDK's `McpAuthenticationDefaults.AuthenticationScheme`; adding `scope` to 401 would require a custom authentication handler override.

**Impact:** Low. MCP clients following the spec's fallback strategy will fetch `scopes_supported` from the metadata endpoint. No known client breaks on this.

### Gap: Client ID Metadata Documents

The DevIssuer accepts only statically registered or dynamically registered (`POST /register`) client IDs. URL-based client IDs (where `client_id` is an HTTPS URL pointing to a metadata JSON document) are not supported. This is a SHOULD, not a MUST.

**Impact:** Does not affect Codex or current clients. Would be needed for interoperability with MCP clients that use URL-based client identification.

### Auth Flow Verification

The full auth chain works correctly:

```
devissuer:3011/.well-known/oauth-authorization-server
  → authorization_code + PKCE S256 + resource parameter
  → token endpoint → JWT with bounded aud claim
  → Gateway JWT validation (issuer, audience, lifetime, signature)
  → scope check via authorization policy (scope or scp claim)
  → tool execution (or 403 insufficient_scope if missing)
```

Key files:
- `GatewayAuthentication.cs:124-134` — 403 step-up challenge
- `GatewayAuthentication.cs:136-151` — `.AddMcp()` metadata setup
- `GatewayAuthConventions.cs:32-36` — well-known path
- `DevIssuerApplication.Metadata.cs:5-24` — AS metadata document

---

## 2. Elicitation Spec — `draft/client/elicitation`

**Status: Not implemented — deliberately removed and replaced with out-of-band browser approval.**

### Background

The project originally used MCP elicitation (`elicitation/create`) for plan approvals. A security audit (F-01 in [security-audit.md](security-audit.md)) identified that elicitation-based approval cannot prove human presence because the approval signal and the approval UI both live within the MCP client's control plane. A compromised MCP client can silently auto-approve.

The fix — documented in [from-elicitation-to-oob-approval.md](../../articles/from-elicitation-to-oob-approval.md) — was to:
1. **Remove all MCP elicitation** from the codebase (confirmed: zero references to `Elicit`, `elicitation`, `elicitation/create`, `elicitationId` in `*.cs`).
2. **Replace it with an out-of-band browser approval flow** using the Gateway's own endpoints.

### The Replacement Flow

| Step | What happens |
|---|---|
| 1. MCP client calls `apply_approved_plan(planId)` | Tool handler in `K8sGatewayTools.cs:235-256` |
| 2. Gateway checks for existing approval | `GatewayApprovalService.EnsureApprovedOrCreateChallengeAsync` (line 24-72) |
| 3. If not approved: creates `ApprovalChallenge` | 32-byte random ID, plan hash, requester subject, 15-min TTL |
| 4. Returns plain text with approval URL | `FormatApprovalRequiredMessage` (line 282-301) — not a JSON-RPC elicitation |
| 5. Human opens URL in browser | `GET /approvals/{challengeId}` — separate OAuth cookie session |
| 6. Gateway renders plan from disk | Not from MCP client — structural guarantee |
| 7. Human clicks Approve | `POST /approvals/{challengeId}/approve` — validates: anti-forgery, same OAuth subject, challenge `pending`, not expired, plan hash unchanged |
| 8. MCP client retries `apply_approved_plan(planId)` | Gateway finds approved challenge → forwards to downstream server → Kubernetes apply |

### Comparison with Standard URL Mode Elicitation

| Property | MCP Elicitation (URL mode) | InfraGate OOB Approval |
|---|---|---|
| Protocol mechanism | `elicitation/create` → `action: "accept"` response | Plain text tool response with URL |
| User opens URL in browser | Yes | Yes |
| Separate auth from MCP bearer token | Not required by spec | Cookie OAuth, separate scheme (`InfraGateApprovalCookie`) |
| Server verifies user identity | Via MCP authorization | Same-subject check: `RequesterSubject == ApproverSubject` (`ValidatePendingChallengeAsync`, line 229) |
| Client cannot auto-approve | Client MUST get user consent | No protocol mechanism for client to submit approval — `apply_approved_plan` accepts only `planId` |
| Phishing protection | Server MUST verify user identity | Challenge hash comparison + anti-forgery token + same-subject binding |
| Plan integrity (content binding) | Not in elicitation spec | SHA-256 hash binding + constant-time comparison at approval and apply time |
| Challenge expiry | Not in elicitation spec | 15-minute TTL on approval challenges |
| Completion notification | `notifications/elicitation/complete` | User retries `apply_approved_plan` — Gateway finds approved challenge |

### Why This Is Not `URLElicitationRequiredError`

The `URLElicitationRequiredError` (code `-32042`) is the standard mechanism for servers needing URL mode elicitation. It causes the MCP client to initiate the elicitation flow and retry the original request after completion. InfraGate does **not** use this because:

1. The approval URL is not exposed to the MCP client through a protocol-level elicitation message — it's plain text in a tool response.
2. There is no standardized retry-after-elicitation-complete pattern. The user must manually retry `apply_approved_plan`.
3. The Gateway's tool handler does not return JSON-RPC errors for "approval needed" — it returns a success response containing the approval URL as tool output.

### Verdict

The elicitation spec is an **optional draft extension**, not a required part of the core MCP specification. Non-implementation is therefore not a compliance gap. The replacement out-of-band flow achieves the same security property (separate browser channel) and, for the specific threat model of a compromised MCP client, provides a **stronger** guarantee: the MCP client has no JSON-RPC method to even acknowledge an approval request, let alone auto-respond to one.

---

## 3. Summary

| Spec | Status | Gaps |
|---|---|---|
| Authorization `2025-11-25/basic` | ✅ Compliant on all MUSTs | 1 SHOULD gap: `scope` missing from 401 WWW-Authenticate (clients fall back to metadata endpoint). 1 non-implemented SHOULD: Client ID Metadata Documents. Dev-only HTTP exception for DevIssuer. |
| Elicitation `draft/client` | N/A — not implemented (optional draft) | Deliberately removed. Replaced by stronger out-of-band browser approval flow. No action needed. |

### Recommendations

1. **Add `scope` to 401 `WWW-Authenticate`** — implement a custom auth handler override on the SDK's `McpAuthenticationDefaults.AuthenticationScheme` to include `scope` alongside `resource_metadata` in the 401 challenge. Low priority.

2. **Client ID Metadata Documents** — extend DevIssuer to support URL-based `client_id` resolution if interoperability with newer MCP clients is desired. Not urgent.

3. **Document the OOB flow as the intended approval pattern** — the project's `MCP-compliance.md` already documents the auth path, but could add a section explaining that the OOB browser flow is the intentional replacement for MCP elicitation, with a cross-reference to the security rationale in the migration article.
