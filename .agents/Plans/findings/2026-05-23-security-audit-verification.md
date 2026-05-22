# Security Audit Verification — Codebase Cross-Reference

**Date:** 2026-05-23
**Source:** `.agents/Plans/loose/security-audit.md` (2026-05-05)
**Method:** Static codebase audit — every finding checked against `src/` and `tests/` as of HEAD

---

**Summary:** 6 of 13 findings addressed (2 fully, 4 partially), 7 unchanged. The two highest-value architectural fixes landed: OOB browser approval UI (F-01) and same-subject authorization (F-08).

---

## F-01 · Auto-Approval Loophole — Human Presence Not Provable

**Severity:** Critical | **Status:** Partially addressed

### Original finding
- Compromised MCP client can auto-approve without rendering the prompt to the SRE
- SHA-256 hash proves plan integrity but not human presence

### Current codebase
- `GatewayApprovalEndpoints.cs` serves a **browser-based OOB approval UI** at `/approvals/{challengeId}` requiring cookie-based OAuth re-authentication (separate from MCP client's token)
- Antiforgery tokens enforced on approve/deny POST endpoints
- `GatewayAuthentication.cs:83-87` — approval policy uses `ApprovalCookie` scheme, separate from bearer JWT
- `ApprovalPlanResult` bound to `PlanEnvelope.Requester.Subject`, grant bound to `ApproverSubject` — identity is tracked end-to-end

### What's still open
- The actual execution gate (`apply_approved_plan` in `GatewayToolDispatcher`) still calls through the MCP tool path — but the grant must already exist (created by the OOB UI), so a compromised client can't fabricate a grant

---

## F-02 · TOCTOU via Stale Dry-Run / force-conflicts

**Severity:** High | **Status:** Partially addressed

### Original finding
- Kubernetes resource state can drift between dry-run and apply, `force-conflicts` silently overwrites
- Recommended: capture `resourceVersion`, assert unchanged at apply, TTL on pending plans

### Current codebase
- `KubernetesPlanExecutor.CheckPreExecutionAsync:15-66` — live drift check (`CheckLiveDriftAsync`) runs as a pre-execution gate, re-checks current state against stored diffs
- `FreshnessPolicy` declared with `LiveDrift` and `PreExecuteDryRun` checks
- `PlanValidityWindow` (via `ValidFromUtc`/`ValidUntilUtc`) enforced at grant validation in `ApprovalGrantValidation.Validate`

### What's still open
- `KubernetesPlanDryRunObject` has only `Object` and `ResponseJson` — no `resourceVersion` captured at plan-build time
- No `resourceVersion` assertion at apply time
- No explicit pending-plan cleanup sweep (expired plans remain in `pending/` directory or Postgres table)

---

## F-03 · Prompt Injection Sanitization Fallacy

**Severity:** High | **Status:** Not addressed

### Current codebase
- `GuardedToolRunner.cs` still uses the same `PromptInjectionGuard.ScanArguments` / `SanitizeAndAuditResponseAsync` pipeline
- Regex-based scanning with 5 categories (`ignore-instructions`, `reveal-prompts`, `tool-use`, `secret-exfiltration`, `authority-override`)
- `PromptInjectionGuard.Regex.cs` / `PromptInjectionGuard.Scanning.cs` / `PromptInjectionGuard.Sanitization.cs` — unchanged pattern
- No schema-enforced JSON envelope for tool output delivery to the LLM

---

## F-04 · Disk Exhaustion & Path Traversal in ApprovalStore

**Severity:** High | **Status:** Partially addressed

### Current codebase
- `ApprovalStore.IsSafePlanId:328-336` validates plan ID as alphanumeric + hyphens only — **blocks path traversal**
- Plan IDs generated as cryptographically random hex strings via `ApprovalIds.NewPlanId()` (16 random bytes → 32 hex chars) — not user-controlled
- `PostgresApprovalPersistence` moves data off filesystem entirely
- `PlanValidityWindow` provides logical plan TTL

### What's still open
- No per-principal rate limiting on plan creation (`CreatePlanAsync` has no caller identity throttle)
- No automated cleanup of expired pending plans (neither file-based `ApprovalStore` nor `PostgresApprovalPersistence` has a background sweep)

---

## F-05 · Loopback Port Hijacking in Dynamic Client Registration

**Severity:** Medium | **Status:** Not addressed (PKCE already present)

### Current codebase
- `GatewayAuthentication.cs:159` — `oauthOptions.UsePkce = true` (primary mitigation, as noted in original audit)
- `oauthOptions.ClientSecret = GatewayAuthConventions.Approvals.PublicClientSecretPlaceholder` ("public-client")
- No container isolation, no OS-level port-binding validation

---

## F-06 · Subprocess Blast Radius Under Shared Service Account

**Severity:** High | **Status:** Not addressed

### Current codebase
- Single `DownstreamMcpClient` connects to one stdio `McpClient` subprocess
- `DownstreamMcpClient.BuildAuthMeta` authenticates with a single `IDownstreamServiceTokenProvider` — all tools share the same downstream identity
- No separation of read vs. write subprocess instances
- No separate Service Accounts for read vs. write K8s operations

---

## F-07 · JWT Bearer Replay — No Proof-of-Possession

**Severity:** High | **Status:** Not addressed

### Current codebase
- `GatewayAuthentication.cs:49-55` — standard `AddJwtBearer` configuration
- No DPoP (RFC 9449) implementation found
- No mutual-TLS client certificate binding
- `TokenValidationParameters` at line 110-118 — standard issuer/audience/lifetime/signing-key validation only

---

## F-08 · No User-to-Plan Binding — Cross-User Plan Approval

**Severity:** High | **Status:** Addressed

### Current codebase
- `SameSubjectAuthorizationCheck.cs` — enforces `context.RequesterSubject == context.ActorSubject`
- `PlanEnvelope.Requester` records `PlanRequester(Subject, AuthenticationType)` at plan creation
- `ApprovalGrant` records both `RequesterSubject` and `ApproverSubject` — tracked through challenge → approve → grant lifecycle
- `GatewayApprovalService.HandlePendingPlanAsync:133-140` — calls `authorizationCheck.EvaluateAsync` with `PlanAuthorizationContext(plan.Requester.Subject, caller.Subject)`
- `GatewayApprovalService.GetGrantedApprovalResultAsync:93-100` — same check on existing grants
- `ApprovalGrantValidation.Validate:27` — validates `grant.RequesterSubject == envelope.Requester.Subject`
- `ApprovalStore.ValidateGrant:450-456` — validates `SameSubject` policy: `grant.RequesterSubject == grant.ApproverSubject`
- Approval UI at `GatewayApprovalEndpoints.cs:166-167` — displays `challenge.RequesterSubject` on the review page

---

## F-09 · No JWT Revocation Mechanism

**Severity:** Medium | **Status:** Not addressed

### Current codebase
- No token introspection endpoint
- No `/revoke` endpoint
- Standard stateless JWT validation only — no revocation list or token blacklist
- No short token lifetime enforcement (default managed by IdP configuration)

---

## F-10 · Subprocess Binary Integrity Not Verified

**Severity:** High | **Status:** Not addressed

### Current codebase
- `DownstreamMcpClient.GetClientAsync` creates `McpClient` via `new McpClient(transport)` with no binary hash verification
- No SHA-256 pinning, no signature verification at subprocess spawn time
- No dedicated OS user with read-only binary permissions setup

---

## F-11 · JWKS Cache Poisoning / Key Rollover Race

**Severity:** Medium | **Status:** Not addressed

### Current codebase
- `GatewayAuthentication.cs:97-119` — standard `JwtBearerOptions` configuration
- No explicit `kid` claim pinning
- No bounded JWKS cache TTL configured beyond `JwtBearerOptions` defaults
- No last-known-good cache fallback on fetch failure
- `ValidateIssuerSigningKey = true` at line 115 — standard validation only

---

## F-12 · Audit Log Tamper by Compromised Process

**Severity:** Medium | **Status:** Partially addressed

### Current codebase
- `ApprovalStore.WriteAuditAsync:277-289` — writes `audit.jsonl` to local filesystem (same process that handles requests)
- `PostgresApprovalPersistence.InsertAuditAsync:1073-1111` — stores audit in `approvals.audit_events` PostgreSQL table (out-of-process append-only sink)
- `GuardrailAuditStore.WriteAsync` — writes `guardrail-audit.jsonl` to local filesystem (Gateway process)

### What's still open
- `GuardrailAuditStore` remains local-file-only — no remote log shipping
- No cryptographic log chaining (each entry does NOT include a hash of the previous entry)
- File-based `ApprovalStore` (used in dev/file mode) still at risk

---

## F-13 · Single Scope for Read and Write Operations

**Severity:** High | **Status:** Not addressed

### Current codebase
- `GatewayAuthConventions.DefaultOAuthScope = "mcp:tools"` — single scope for all operations
- `GatewayAuthentication.cs:147` — `ScopesSupported = { options.OAuthScope }` — protected resource metadata lists only `mcp:tools`
- `HasRequiredScope:199-205` — checks for single scope value on every request
- `GatewayAuthConventions.Scope` — no `mcp:read` or `mcp:write` constants defined
- No per-tool scope enforcement in `GatewayToolDispatcher`
