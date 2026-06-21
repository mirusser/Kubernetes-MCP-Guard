# Security Audit Report
## MCP Kubernetes Gateway — OAuth / Gateway / Approval Architecture

**Date:** 2026-05-05  
**Re-assessed:** 2026-06-05 (against current codebase; see Implementation Notes per finding)  
**Scope:** Architecture & request-flow diagrams covering OAuth Login & Authorization, Read-Only Tool Calls, and Approval-Gated Mutations.  
**Method:** Static architectural analysis of Mermaid sequence diagrams and system description.  
**Status:** 13 findings identified — 6 from initial audit (verified & extended), 7 new. **Re-assessment: 6 mitigated, 4 partial, 3 unaddressed.**

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Threat Model Assumptions](#threat-model-assumptions)
3. [Findings](#findings)
   - [F-01 · Auto-Approval Loophole — Human Presence Not Provable](#f-01)
   - [F-02 · TOCTOU via Stale Dry-Run / force-conflicts](#f-02)
   - [F-03 · Prompt Injection Sanitization Fallacy](#f-03)
   - [F-04 · Disk Exhaustion & Path Traversal in ApprovalStore](#f-04)
   - [F-05 · Loopback Port Hijacking in Dynamic Client Registration](#f-05)
   - [F-06 · Subprocess Blast Radius Under Shared Service Account](#f-06)
   - [F-07 · JWT Bearer Replay — No Proof-of-Possession](#f-07)
   - [F-08 · No User-to-Plan Binding — Cross-User Plan Approval](#f-08)
   - [F-09 · No JWT Revocation Mechanism](#f-09)
   - [F-10 · Subprocess Binary Integrity Not Verified](#f-10)
   - [F-11 · JWKS Cache Poisoning / Key Rollover Race](#f-11)
   - [F-12 · Audit Log Tamper by Compromised Process](#f-12)
   - [F-13 · Single Scope for Read and Write Operations](#f-13)
4. [Summary Table](#summary-table)
5. [Recommended Prioritization](#recommended-prioritization)

---

## Executive Summary

The architecture demonstrates a strong security foundation: OAuth 2.0 with PKCE S256, structured JWT scope enforcement, prompt-injection guardrails, an approval-gated mutation path with SHA-256 hash binding, and a structural firewall that terminates OAuth tokens at the Gateway before passing calls to the stdio subprocess. These are not trivial controls and reflect genuine security engineering effort.

However, the audit identified **13 findings** ranging from Medium to Critical severity. The most impactful are:

- The approval flow proves **plan integrity** via hash binding, but cannot prove **human presence** — a compromised MCP client can auto-approve without rendering the prompt to the SRE.
- A five-tier scope model now separates read (`mcp:tools.read`), write (`mcp:tools.write`), observation (`mcp:tools.readonly`), proposal (`mcp:tools.propose`), and execution (`mcp:tools.execute`), with the legacy `mcp:tools` scope retained for backward compatibility. Each tool is mapped to its minimum required scope in `ToolScopeCatalog` and enforced at call-time by `ToolScopeGuard`.
- **No user-to-plan binding** means any authenticated principal can approve and execute a plan they did not create.
- **JWT Bearer tokens are replayable** with no proof-of-possession or revocation mechanism.

> **Re-assessment (2026-06-06):** Of the top 4 concerns above, F-01 (auto-approval) and F-08 (user-to-plan binding) are now **mitigated**. F-13 (scope split) is fully addressed with `mcp:tools.read`/`mcp:tools.write` for human operators and role-based scopes (`mcp:tools.readonly`/`.propose`/`.execute`) for agents. F-07 (JWT replay) is partially mitigated with DPoP for internal clients. See individual findings for detailed Implementation Notes.

---

## Threat Model Assumptions

The following attacker capabilities are assumed for this audit:

| Assumption | Rationale |
|---|---|
| Attacker can compromise the MCP Client process | Prompt injection, malicious package, or supply-chain attack |
| Attacker can read local disk and logs | Shared host or lateral movement from another process |
| Attacker can bind to loopback network interfaces | Any local process can race for a port |
| Kubernetes objects (ConfigMaps, Pod labels) are attacker-influenced | Attacker may have written or deployed a workload into the cluster |
| The stdio subprocess binary could be replaced | Supply-chain or local privilege escalation |

---

## Findings

---

### F-01
### Auto-Approval Loophole — Human Presence Not Provable
**Severity:** Critical  
**Diagram:** Approval-Gated Mutation (Diagram 3)  
**Status:** Original finding — extended after clarification  
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-05)  

#### Description

The approval flow uses MCP elicitation: the Gateway sends a plan summary and SHA-256 hash to the MCP Client, the Client prompts the user (`"Approve this plan?"`), the user answers `Yes`, and the Client forwards the approval response back to the Gateway, which then notifies the subprocess.

After clarification, the implementation does bind the hash to the plan content — the server recomputes the SHA-256 of the pending plan at apply-time and rejects any mismatch. This correctly closes the **bait-and-switch** scenario where a compromised client shows Plan A to the user but forwards approval for Plan B.

#### Residual Risk

The hash proves **plan integrity**, not **human presence**. The entire approval signal — including the correct planId and the correct hash — originates from within the MCP Client, which is itself within the compromised trust boundary in relevant attack scenarios.

A compromised MCP Client can:
1. Receive the elicitation request (it is the transport layer — it sees everything).
2. Never render the prompt to the human SRE.
3. Automatically respond `Yes`, forwarding the correct planId. The hash is fetched by the server from the pending plan file, so the client does not even need to know or forward it independently.

This is structurally equivalent to a malicious browser extension auto-clicking a payment confirmation dialog. The transaction hash on the payment doesn't help if the extension intercepts the click event before the human sees it.

#### Revised Attack Scope

| Scenario | Status |
|---|---|
| Compromised client swaps plan content after approval | ✅ Mitigated by SHA-256 hash recomputation |
| Compromised client auto-approves without prompting human | ❌ Not mitigated |
| Cross-user approval via stolen planId | ❌ Not mitigated (see F-08) |

#### Recommended Fix

Implement **out-of-band (OOB) authorization** for mutation approvals. The Gateway must issue the approval challenge through a channel the MCP Client cannot intercept or forge:

- A separate web UI served by the Gateway itself, requiring re-authentication.
- A push notification to a registered device (e.g., Slack, Teams, or a mobile push-to-approve token).
- At minimum, a short-lived, single-use approval token sent via a second channel (e.g., email or SMS) that must be supplied in the `apply_approved_plan` call.

The approval signal should be cryptographically bound to the approver's identity (see F-08) and originate from outside the client's control plane.

#### Implementation Notes (2026-06-05)

Fully addressed. The approval flow now uses **out-of-band browser-based approval** (`GatewayApprovalService.ApproveChallengeAsync`). When `execute_approved_plan` is called, the gateway creates an `ApprovalChallenge` and returns a browser URL (`/approvals/{challengeId}`). The MCP client cannot auto-approve — the human operator must open the URL in a browser, authenticate via OAuth PKCE, and approve/deny on the Review Surface. SHA-256 digests are recomputed at challenge creation and grant time to prevent tampering. Plan hash is verified via `FixedTimeStringComparer` at apply time (`GatewayApprovalService.cs:612-620`).

---

### F-02
### TOCTOU via Stale Dry-Run / force-conflicts
**Severity:** High  
**Diagram:** Approval-Gated Mutation (Diagram 3)  
**Status:** Original finding — confirmed  
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-05)

#### Description

Plan creation performs a Server-Side Apply (SSA) dry-run without `force-conflicts`. The resulting plan is stored and may sit unexecuted for an arbitrary duration before the user calls `execute_approved_plan`.

During that window, another SRE or automated process may legitimately modify the same Kubernetes resource (e.g., rolling a new image tag to patch a zero-day vulnerability). When the pending plan is applied, the previous normalized-state comparison does not detect metadata-only changes (e.g., a round-trip write that updates `resourceVersion` but doesn't change normalized fields).

The SHA-256 hash mitigates the case where the *pending plan file itself* is tampered with, but without resourceVersion tracking it does not protect against all forms of **Kubernetes resource state** drifting between dry-run and apply.

#### Recommended Fix ✅

- ✅ Capture the `resourceVersion` of every affected Kubernetes resource at plan creation time and store it alongside the plan.
- ✅ At plan execution time, assert the `resourceVersion` is unchanged before proceeding. If any resource has been updated, reject the plan with an explicit error requiring re-planning.
- ✅ Set `resourceVersion` as a server-side precondition on SSA apply objects, making the check atomic.
- ✅ Enforce a configurable TTL on pending plans (e.g., 15 minutes) after which they expire automatically.

#### Implementation Notes (2026-06-05)

**Two-layer defense now implemented:**

**Layer 1 — Pre-execution ResourceVersion freshness check** (covers ALL operations):
- `KubernetesPlanDiff` now carries a `ResourceVersion` field captured from live resource metadata at plan creation time, extracted before normalization via `KubernetesObjectMetadataExtractor` (in `BuildDiff`/`BuildDiffsAsync`).
- Plan builders (`ApplyManifestBuilder`, `DeleteManifestBuilder`, `ScaleDeploymentBuilder`, `RestartDeploymentBuilder`, `SetDeploymentImageBuilder`) add a `ResourceVersionCheck` to the `FreshnessPolicy` when any diff has a captured resourceVersion, storing the object-key → resourceVersion mapping in the check parameters.
- `KubernetesPlanExecutor.CheckResourceVersionAsync` runs before drift detection in `CheckPreExecutionAsync`. It reads the `ResourceVersionCheck` from the freshness policy and compares each expected resourceVersion against current live state. A mismatch blocks execution with `kubernetes.resource_version.mismatch` reason code and an audit entry.

**Layer 2 — Server-side resourceVersion precondition** (covers SSA apply/delete only):
- `KubernetesExecutionService.ExecuteApplyManifestAsync` accepts an optional `resourceVersionsJson` parameter.
- `ApplyResourceVersions` sets `Metadata.ResourceVersion` on each parsed object before the SSA patch, making the Kubernetes API atomically reject with 409 Conflict if any object's resourceVersion changed.
- Resource versions flow from the adapter's `OperationDispatchMap.BuildManifestArgs` through dispatch arguments to the server-side execution service.

**Key files changed:**
- `KubernetesPlanDiff.cs` (adapter + McpServer): added `string? ResourceVersion = null`
- `KubernetesObjectMetadataExtractor.cs` (new): extracts resourceVersion from raw JSON before normalization
- `KubernetesDiffService.cs`: extracts resourceVersion in `BuildDiff`/`BuildDiffsAsync`
- `KubernetesAdapterConventions.cs`: added `ResourceVersionCheck`, `CheckResourceVersion`, `ResourceVersionMismatch`, `ResourceVersions`
- `KubernetesBuilderInfrastructure.cs`: added `BuildFreshnessPolicy` helper
- All 5 plan builders: use `BuildFreshnessPolicy` instead of static check lists
- `KubernetesPlanExecutor.cs`: added `CheckResourceVersionAsync`, integrated before drift check
- `OperationDispatchMap.cs`: `BuildManifestArgs` includes resourceVersions JSON
- `KubernetesExecutionService.cs`: `ApplyResourceVersions` helper, updated `ExecuteApplyManifestAsync` signature
- `KubernetesTools.cs`: `ApplyManifest` accepts optional `resourceVersions` parameter

---

### F-03
### Prompt Injection Sanitization Fallacy
**Severity:** High
**Diagram:** Read-Only Tool Call (Diagram 2)
**Status:** Original finding — confirmed
**Resolution:** ✅ **MITIGATED WITH RESIDUAL RISK** (re-assessed 2026-06-20)

#### Description

The Gateway's `GuardedToolRunner` scans tool call arguments and responses against five named categories: `ignore-instructions`, `reveal-prompts`, `tool-use`, `secret-exfiltration`, and `authority-override`. The response sanitizer redacts matching lines before returning results to the LLM.

Rule-based and regex-based scanning is a known-weak defense against modern LLM prompt injections. An attacker who controls any Kubernetes object readable by the `get_k8s_status` tool (e.g., a ConfigMap value, a Pod label, a Service annotation, or application log output) can embed injection payloads that:

- Are base64-encoded or otherwise encoded and decoded by the LLM but not by the scanner.
- Are split across multiple fields that individually pass the scanner but are semantically meaningful to the LLM in combination.
- Use Unicode homoglyphs, zero-width characters, or non-Latin script that the regex does not match.
- Use indirect injection patterns not yet in the five-category blocklist.

If any such payload reaches the LLM, it can override the system prompt and hijack subsequent tool calls, including mutation operations.

#### Recommended Fix

- **Treat all data from Kubernetes as hostile by default.** Do not attempt to distinguish safe from unsafe K8s content at the text level.
- Deliver tool results to the LLM within strict, schema-enforced JSON envelopes. Instruct the model that content within the `tool_result` schema boundary is opaque data, never instructions.
- Consider a secondary LLM pass specifically tasked with classifying tool output as benign or suspicious before it reaches the primary model context.
- Use the audit log (`GuardrailAuditStore`) as a detection signal, not a prevention mechanism — assume some injections will pass.

#### Implementation Notes (2026-06-20)

Mitigated by changing the prevention boundary from text sanitization to structural output isolation:

- Read-only downstream tool calls now return a `model_visible_tool_result` JSON envelope from `GatewayToolDispatcher.HandleReadOnlyAsync` via `ModelVisibleToolResultEnvelope`. Gateway-owned metadata (`schemaVersion`, `kind`, `toolName`, `source`, `status`, `guardrail`) is separated from Kubernetes-derived content under `untrusted.payload`.
- `GuardedToolRunner.CallForModelVisibleResponseAsync` preserves scanner/audit behavior while reporting guardrail action and categories as structured envelope metadata instead of prepending `Guardrail warning:` prose to model-visible read-only responses.
- The deterministic scanner now normalizes zero-width format characters and a small set of common Greek/Cyrillic homoglyphs before matching, and it evaluates combined text across fields to catch split-field payloads.
- Observer and Planner system prompts explicitly state that `untrusted.payload` is observation data only, not instructions, policy, secrets to reveal, or tool-calling guidance.
- Agent-layer tool results pass through `IModelVisibleContentGuard` in `ToolCallingAgentFactory`; enveloped tool results preserve trusted metadata while replacing only `untrusted.payload` when a guard redacts, quarantines, or blocks content. Gateway and agent tests cover the envelope contract, error envelope, payload isolation, and F-03 corpus cases.

Residual risk remains for sophisticated semantic prompt injections that evade deterministic classification. ADR-0030 still defers a local semantic classifier sidecar; `.agents/Plans/loose/2026-06-03-local-semantic-classifier-research-plan.md` must pass candidate, license, provenance, and corpus-bakeoff review before any classifier becomes a runtime dependency.

---

### F-04
### Disk Exhaustion & Path Traversal in ApprovalStore
**Severity:** High
**Diagram:** Approval-Gated Mutation (Diagram 3)
**Status:** Original finding — confirmed, scope extended
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-05)

#### Description

**Disk exhaustion:** The `request_scale_deployment` tool creates a new pending plan file on every invocation. If the AI agent enters a loop — due to a reasoning error, a prompt injection hijack, or deliberate abuse — it can generate thousands of plan files without any visible rate limit or pending-plan TTL. This exhausts disk space or inodes, causing a Denial of Service on the subprocess and potentially on the host.

**Path traversal:** The `apply_approved_plan` call accepts a `planId` and constructs a file path under `.mcp-approvals/pending/`. If `planId` is not strictly validated as a UUID (or equivalent opaque token), an attacker who can influence the planId value (e.g., via a prompt injection that causes the LLM to construct a crafted tool call) could supply a value such as `../../../etc/passwd`, causing the file read to escape the approvals directory. Given the subprocess co-locates the approvals directory with its binary, the blast radius of a successful traversal is elevated.

#### Recommended Fix

- Validate `planId` as a cryptographically generated UUID (v4) using an allowlist regex before any file system operation. Reject all other values with a logged error.
- Enforce a rate limit on `request_scale_deployment` per authenticated principal (e.g., max 5 pending plans per user at any time).
- Implement an automated TTL cleanup: pending plans older than a configurable threshold (e.g., 30 minutes) are moved to an `expired/` subdirectory or deleted.
- Run the subprocess with an OS-level disk quota to bound worst-case exhaustion.

#### Implementation Notes (2026-06-05)

Both concerns are addressed by the migration from file-based storage to **PostgreSQL persistence** (`PostgresApprovalPersistence.cs`). The legacy `ApprovalStore` is deprecated (`src/InfraGate.Approvals/_Deprecated/`). PlanId validation was added via `IsSafePlanId` (alphanumeric + hyphens only, `PostgresApprovalPersistence.cs:1295-1303`). Plan IDs are generated as cryptographically random hex strings (`ApprovalIds.NewPlanId`). Plan validity window provides TTL-based expiration. Rate limiting on plan creation is still missing.

---

### F-05
### Loopback Port Hijacking in Dynamic Client Registration
**Severity:** Medium
**Diagram:** OAuth Login & Authorization (Diagram 1)
**Status:** Original finding — confirmed, partially mitigated
**Resolution:** ⚠️ **PARTIALLY MITIGATED** (re-assessed 2026-06-05)

#### Description

Dynamic Client Registration (DCR) uses a loopback redirect URI. On the host machine, any local process can attempt to bind to an arbitrary port on the loopback interface. If a malicious process wins the port race and binds before the MCP Client does, it receives the Authorization Code redirect from the Auth Server.

The mandatory PKCE S256 enforcement significantly reduces the exploitability of this: capturing the Authorization Code alone is not sufficient — the attacker also requires the `code_verifier`, which is generated and held only by the MCP Client. However, in a scenario where the malicious process acts as a transparent proxy (binding first, forwarding to the real client, then capturing the code for replay), the window of opportunity exists particularly on slower or loaded systems.

#### Recommended Fix

- PKCE S256 enforcement (already present) is the primary mitigation — maintain it strictly.
- Where the deployment environment permits, run the MCP Client in an isolated container or sandbox so the loopback interface is not shared with untrusted processes.
- Consider binding the redirect listener to an OS-assigned ephemeral port and registering it dynamically, reducing predictability.
- On supported platforms, use OS-level process validation (e.g., verifying the process that bound the port matches the expected binary) as a defense-in-depth measure.

#### Implementation Notes (2026-06-05)

No material change since original audit. PKCE S256 is still enforced (`GatewayAuthentication.cs:164`: `UsePkce = true`). Loopback redirect URIs are constrained to `127.0.0.1`/`localhost` in Keycloak config (`deploy/keycloak/infra-gate-realm.json`). Container isolation added in compose files (`read_only: true`, `tmpfs`, `no-new-privileges`, `cap_drop: ALL`). No OS-level port binding validation implemented.

---

### F-06
### Subprocess Blast Radius Under Shared Service Account
**Severity:** High
**Diagram:** Read-Only Tool Call (Diagram 2)
**Status:** Original finding — confirmed
**Resolution:** ⚠️ **PARTIALLY MITIGATED** (re-assessed 2026-06-05)

#### Description

The Gateway intentionally terminates the OAuth JWT and does not pass it to the stdio subprocess. This is a correct structural firewall design — the subprocess has no user identity context and operates entirely under its own Kubernetes Service Account.

The consequence is that all tool handlers — read-only (`get_k8s_status`) and mutating (`request_scale_deployment`, `apply_approved_plan`) — share a single K8s identity. If the subprocess is compromised (e.g., via a zero-day in the .NET JSON parser, a dependency vulnerability, or a successful prompt injection that achieves code execution), the attacker inherits the full weight of that Service Account, including all mutation permissions.

#### Recommended Fix

- Define separate Kubernetes Service Accounts for read operations and mutation operations, each bound to the minimum necessary RBAC Role.
- Ideally, instantiate separate subprocess instances for read and write paths, each running under its respective Service Account, selected by the Gateway based on the tool being called.
- Periodically rotate Service Account tokens and audit RBAC bindings in CI to prevent permission creep.

#### Implementation Notes (2026-06-05)

RBAC split implemented in `deploy/minikube/rbac.yaml`: `infra-gate-mcp-manager` Role has write verbs (create/update/patch/delete) bound to `infra-gate-mcp` SA; `infra-gate-mcp-viewer` Role has read-only verbs (get/list/watch) bound to `infra-gate-mcp-view` SA. Container isolation added (`read_only`, `tmpfs`, `no-new-privileges`, `cap_drop: ALL` in compose files). Dockerfiles use non-root `USER $APP_UID`. **However:** still a single subprocess instance (`BootstrapStdioClientTransport.cs:32-43`) sharing one K8s identity — no separate subprocess instances per read/write path. The Gateway does not select different subprocess instances based on tool type.

---

### F-07
### JWT Bearer Replay — No Proof-of-Possession
**Severity:** High
**Diagram:** OAuth Login & Authorization (Diagram 1) & Read-Only Tool Call (Diagram 2)
**Status:** New finding
**Resolution:** ⚠️ **PARTIALLY MITIGATED** (re-assessed 2026-06-06)

#### Description

The access token is a standard Bearer JWT with no proof-of-possession binding. Any party that obtains the token can replay it against the Gateway until it expires. There is no mention of DPoP (RFC 9449) or mutual-TLS client certificate binding.

In a local development context — where the DevIssuer is running on the same host — log verbosity is typically high. JWT tokens can leak through:

- Debug log lines in the Gateway or client (e.g., full `Authorization:` header logging).
- Process environment variables or command-line arguments visible via `/proc`.
- Network capture on the loopback interface (unencrypted local traffic).
- Shell history if the token is ever used in a curl invocation during development.

Once leaked, the token is valid for its full lifetime and grants access to all tools within the `mcp:tools` scope, including mutation paths.

#### Recommended Fix

- Implement **DPoP (RFC 9449)**: bind the JWT to the client's ephemeral key pair. The Gateway verifies the DPoP proof on each request, making a stolen token useless without the corresponding private key.
- If DPoP is out of scope, enforce very short token lifetimes (2–5 minutes) with silent refresh via refresh token rotation.
- Ensure all Gateway and client log configurations explicitly scrub `Authorization` headers and Bearer token values.

#### Implementation Notes (2026-06-06)

DPoP (Demonstrating Proof-of-Possession) is now implemented and enforced for controlled internal clients (Planner, Observer, Executor) and the Approval UI. The Gateway verifies the DPoP proof signature, lifetime, `jti` replay, and matches the `jkt` claim against the access token. However, two gaps remain:
1. The `jti` replay store (`InMemoryDpopProofReplayStore`) is in-memory only, meaning replay detection is not distributed across multiple Gateway replicas.
2. External MCP clients (like Claude Code) do not yet broadly support DPoP, so the Keycloak `mcp-client` allows standard bearer tokens as a fallback.
Thus, this finding is downgraded to partially mitigated.

---

### F-08
### No User-to-Plan Binding — Cross-User Plan Approval
**Severity:** High
**Diagram:** Approval-Gated Mutation (Diagram 3)
**Status:** New finding
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-05)

#### Description

When a plan is created via `request_scale_deployment`, there is no evidence that the plan file records the `sub` (subject) claim of the JWT that created it. When `apply_approved_plan(planId)` is called, the server verifies the plan hash but does not appear to check whether the caller is the same principal who created the plan.

This opens two attack paths:

1. **Cross-user execution:** User A creates a plan to scale a production deployment to zero replicas. User A abandons it. User B (who has `mcp:tools` scope) discovers or is social-engineered into calling `apply_approved_plan` with that planId, executing User A's plan under User B's session.
2. **Agent-driven cross-approval:** A compromised AI agent that has obtained any valid `mcp:tools` JWT can enumerate pending planIds (if the IDs are guessable or leaked) and apply plans created by other users.

The SHA-256 hash binding does not address this because it binds content, not caller identity.

#### Recommended Fix

- At plan creation, record the `sub` claim of the creating JWT in the plan file.
- At `apply_approved_plan` time, compare the caller's `sub` claim against the stored creator `sub`. Reject mismatches with a logged audit entry.
- Display the creator's identity in the elicitation approval prompt so the approving human sees who originated the plan.
- Consider whether cross-user approval should ever be permitted, and if so, implement an explicit delegation mechanism rather than relying on ambient scope.

#### Implementation Notes (2026-06-05)

Fully addressed. Creator `sub` claim is resolved from the JWT (`GatewayApprovalIdentityResolver.Resolve`), recorded in `PlanRequester.Subject`, persisted to `approvals.plan_envelopes.requester_subject` column (`PostgresApprovalPersistence.cs:91`). Two policies enforce binding:
- **SameSubject** (default for human-driven plans): `ApprovalPolicyAuthorizationCheck.SameSubject` verifies `context.ActorSubject == context.RequesterSubject`.
- **OperatorApproval** (Planner-originated plans): `IsActorAuthorizedForChallengeOutcome` checks actor belongs to configured operator group.
Grant validation (`ApprovalGrantValidation.Validate`) cross-checks requester subject at apply time.

---

### F-09
### No JWT Revocation Mechanism
**Severity:** Medium
**Diagram:** OAuth Login & Authorization (Diagram 1) & Read-Only Tool Call (Diagram 2)
**Status:** New finding
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-21)

#### Description

JWTs are stateless and validated solely by signature and claims (issuer, audience, lifetime, scope). There is no token introspection endpoint, no revocation list, and no mention of short token lifetimes. If a token is compromised — via any of the leak vectors described in F-07 — there is no mechanism to invalidate it before expiry.

This means a compromised token remains fully operational for its entire lifetime. Every tool call, including mutations, will succeed for any bearer of the token regardless of whether the legitimate user has ended their session.

#### Recommended Fix

- Implement token introspection at the Gateway: on each inbound request, call `POST /introspect` on the DevIssuer to validate that the token is still active. Cache introspection results for a short window (e.g., 30 seconds) to avoid per-request latency.
- Alternatively, enforce very short JWT lifetimes (2–5 minutes) combined with refresh token rotation and a refresh token revocation list. Revoking the refresh token effectively invalidates the session.
- Expose a `POST /revoke` endpoint on the Gateway that an SRE can call to immediately blacklist a token by `jti` claim.

#### Implementation Notes (2026-06-21)

Mitigated with standards-based issuer introspection and short access-token lifetime enforcement. The gateway does not own a `/revoke` endpoint; the IdP remains the revocation source of truth.

- `GatewayAuthentication.cs` rejects tokens whose `exp - iat` or `exp - nbf` exceeds `InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds` (default 300 seconds) and rejects tokens missing both baseline claims when the check is enabled.
- `HttpTokenIntrospectionClient` posts validated JWTs to the configured/discovered OAuth introspection endpoint. Only `active: true` succeeds; inactive, malformed, HTTP failure, or unavailable endpoint responses fail closed.
- `TokenIntrospectionActivityValidator` caches only successful active introspection results, keyed by a SHA-256 hash of the token and capped by `TokenIntrospectionCacheSeconds`, JWT `exp`, and introspection `exp`. Expired entries are pruned when the cache exceeds an internal threshold so high-cardinality token streams do not grow memory unbounded.
- `TokenClaimDates.TryGetUnixTimeClaim` returns `false` for out-of-range Unix-time claims instead of throwing, so malformed tokens fail the lifetime checks cleanly.
- Production safety validation now requires `InfraGate__Auth__TokenIntrospectionEnabled=true`, a dedicated introspection client id/secret, and a maximum accepted token lifetime of 300 seconds or less.
- The local/test Keycloak realm includes a dedicated `infra-gate-token-introspection` confidential client and keeps `accessTokenLifespan` at 300 seconds.
- Run profiles can express the production introspection settings; the production profile emits the required enabled flag, client id/secret placeholder, endpoint, cache TTL, and max accepted token lifetime.
- Documentation in `src/InfraGate.McpGateway.Auth/README.md`, `docs/configuration.md`, and `docs/production-oidc.md` describes introspection, cache behavior, max token lifetime, Keycloak endpoint path, and the fact that approval UI logout clears only the gateway cookie.
- The Keycloak integration test README documents that the Testcontainers setup proves active-token introspection but does not reliably prove session-based revocation, because Keycloak's default self-contained access tokens remain introspectable as `active` until expiry unless the realm is configured to check session state at introspection time.

Tests: `GatewayAuthenticationTests`, `HttpTokenIntrospectionClientTests`, `TokenIntrospectionActivityValidatorTests`, `TokenClaimDatesTests`, `GatewayAuthOptionsTests`, `InfraGateAuthSettingsTests`, `McpGatewayOptionsTests`, `RunProfileCliTests`, `RunProfileDocumentReaderTests`, `EnvFileRendererTests`, and `KeycloakIntegrationTests` cover active, inactive/revoked, introspection failure, malformed responses, caching, cache pruning, out-of-range claim handling, max lifetime rejection, missing baseline claims, real Keycloak active-token introspection, production run-profile generation, and existing valid JWT behavior.

---

### F-10
### Subprocess Binary Integrity Not Verified
**Severity:** High
**Diagram:** Read-Only Tool Call (Diagram 2) & Approval-Gated Mutation (Diagram 3)
**Status:** New finding
**Resolution:** ❌ **NOT MITIGATED** (re-assessed 2026-06-05)

#### Description

The Gateway spawns the MCP Server as a `stdio` subprocess via `StdioClientTransport`. There is no documented check that the subprocess binary has not been tampered with before spawning. The binary path is presumably configured at startup and then trusted implicitly on every invocation.

A supply-chain attack targeting the MCP Server package, or a local privilege escalation that replaces the binary on disk, would be entirely transparent to the Gateway. The Gateway would continue forwarding authenticated tool calls to the malicious binary, which inherits the Kubernetes Service Account and can perform any K8s operation the RBAC permits.

This is especially relevant because the subprocess binary is co-located with the `.mcp-approvals/` directory, which it also writes to — a compromised binary has write access to its own audit store.

#### Recommended Fix

- At application startup, compute the SHA-256 hash of the subprocess binary and compare it against a pinned expected value stored outside the subprocess's directory (e.g., in a Gateway configuration file or environment variable).
- Refuse to start if the hash does not match.
- Run the subprocess under a dedicated OS user that has no write permissions to its own binary path.
- Where possible, sign the subprocess binary and verify the signature using a trusted key at spawn time.

#### Implementation Notes (2026-06-05)

No startup binary hash verification found in `src/InfraGate.McpGateway` or `src/InfraGate.McpServer`. Startup safety checks exist for URLs/directories (`McpGatewayOptions.cs:99-154`) but not binary attestation. Subprocess spawning uses standard `ProcessStartInfo` with `UseShellExecute = false` (`BootstrapStdioClientTransport.cs:32-43`). Non-root Docker user (`USER $APP_UID`) provides some isolation but no integrity verification.

---

### F-11
### JWKS Cache Poisoning / Key Rollover Race
**Severity:** Medium
**Diagram:** OAuth Login & Authorization (Diagram 1)
**Status:** New finding
**Resolution:** ❌ **NOT MITIGATED** (re-assessed 2026-06-05)

#### Description

The Gateway validates inbound JWTs against the issuer's JWKS (JSON Web Key Set). Two failure modes exist:

**Stale trust window:** During key rollover, the old key may remain in the Gateway's JWKS cache for the duration of the cache TTL. Tokens signed with a revoked key continue to be accepted during this window.

**Key ID (kid) acceptance:** If the Gateway accepts any key present in the JWKS response without strictly matching the `kid` header claim in the JWT, a JWKS endpoint compromise (e.g., via DNS poisoning or a compromise of the DevIssuer host) allows an attacker to inject a new key that signs arbitrary tokens with full Gateway trust.

**DoS via rapid rotation:** If the DevIssuer rotates keys more frequently than the cache TTL, or if cache invalidation triggers a synchronous JWKS fetch on every validation failure, the Gateway becomes vulnerable to a DoS via cache-busting.

#### Recommended Fix

- Enforce strict `kid` matching: the JWT's `kid` header claim must match a specific key in the JWKS response. Reject tokens with unknown or missing `kid`.
- Set a bounded JWKS cache TTL (e.g., 5 minutes) with a background refresh job, not on-demand refresh per validation failure.
- On JWKS fetch failure, use the last-known-good cached key set rather than failing open or fetching synchronously per request.
- Pin the JWKS endpoint URI in configuration and validate the TLS certificate against a pinned CA, even in local/dev deployments.

#### Implementation Notes (2026-06-05)

Gateway auth uses standard `AddJwtBearer` with IdentityModel's default `ConfigurationManager<OpenIdConnectConfiguration>` for OIDC discovery (`GatewayAuthentication.cs:104-127`). No custom cache TTL, no background refresh configuration, no explicit `kid` matching code found in gateway auth path. Downstream server auth (`DownstreamTokenValidator.cs:22-40`) uses similar defaults. No last-known-good cache fallback on fetch failure.

---

### F-12
### Audit Log Tamper by Compromised Process
**Severity:** Medium
**Diagram:** Read-Only Tool Call (Diagram 2) & Approval-Gated Mutation (Diagram 3)
**Status:** New finding
**Resolution:** ⚠️ **PARTIALLY MITIGATED** (re-assessed 2026-06-05)

#### Description

Both audit stores — `GuardrailAuditStore` (JSONL in the Gateway process) and `.mcp-approvals/audit.jsonl` (in the subprocess) — are written to disk by the same processes that handle requests. Each process has the filesystem permissions necessary to write to its own audit file, which implicitly includes the ability to truncate, delete, or overwrite it.

A compromised Gateway or subprocess can modify or delete audit entries to conceal malicious activity. There is no mention of append-only file semantics, remote log shipping, cryptographic chaining of log entries, or out-of-process log validation.

This means the audit trail, which is the primary forensic record for detecting prompt injection, unauthorized approvals, and hash mismatches, cannot be trusted if either process is compromised.

#### Recommended Fix

- Ship audit log entries to an **out-of-process sink immediately on write**: a remote syslog server, a SIEM, or an append-only object store (e.g., S3 with Object Lock).
- The process handling requests should not have `unlink`, `truncate`, or overwrite permissions on its own audit files. Use a separate log-shipping agent with dedicated write credentials.
- Consider cryptographically chaining log entries (each entry includes a hash of the previous entry) so tampering with any entry breaks the chain and is detectable.
- Treat the local JSONL files as a write-ahead buffer only, not as the authoritative audit record.

#### Implementation Notes (2026-06-05)

PostgreSQL audit outbox implements **cryptographic hash chaining** (`PostgresAuditOutboxCore.cs:13-45`): `previous_event_hash` + canonical JSON + SHA-256, providing tamper-evident audit for approval streams. `AuditOutboxConventions.cs` defines canonical input and `previous_event_hash`/`event_hash` columns. **However:** the local guardrail JSONL audit (`GuardrailAuditStore.cs:10-37`) is plain `File.AppendAllTextAsync` with no chaining and no append-only enforcement. No remote syslog/SIEM shipping found. `JsonFileAnomalyHandoffSink.cs` writes local temp-file + rename, not an append-only audit stream.

---

### F-13
### Single Scope for Read and Write Operations
**Severity:** High
**Diagram:** All three diagrams
**Status:** New finding
**Resolution:** ✅ **MITIGATED** (re-assessed 2026-06-05)

#### Description

The architecture originally used a single `mcp:tools` OAuth scope to gate all operations — read-only diagnostics (`get_k8s_status`, log streaming) and destructive mutations (`request_scale_deployment`, `apply_approved_plan`).

**This is now mitigated.** A five-tier scope model replaces the single-scope approach:

- `mcp:tools.read` — human operators: read-only inspection tools, evidence tools, `get_plan_status`
- `mcp:tools.write` — human operators: all tools including plan mutation and execution
- `mcp:tools.readonly` — agent service identity: Observer read path
- `mcp:tools.propose` — agent service identity: Planner plan proposal (combined with `mcp:tools.readonly` for read access)
- `mcp:tools.execute` — agent service identity: Executor plan execution
- `mcp:tools` — legacy scope for backward compatibility (equivalent to full access)

#### Recommended Fix

- ~~Define at minimum two scopes: `mcp:read` and `mcp:write`.~~ → Implemented as `mcp:tools.read` / `mcp:tools.write`
- ~~Issue `mcp:read`-only tokens for read-only sessions.~~ → Done
- ~~Enforce scope per tool at the Gateway.~~ → Done via `ToolScopeCatalog` + `ToolScopeGuard`
- ~~Advertise scopes in protected-resource metadata.~~ → Done

#### Implementation Notes (2026-06-05)

Fully addressed. Six scope constants are defined in both `GatewayAuthConventions.cs:5-11` and `McpGatewayConventions.cs:177-185`: `mcp:tools` (legacy), `mcp:tools.readonly`, `mcp:tools.propose`, `mcp:tools.execute`, `mcp:tools.read`, `mcp:tools.write`. All six are registered in `AcceptedScopes` (`GatewayAuthentication.cs:74-82`) so the authorization policy accepts them.

Per-tool enforcement is handled by `ToolScopeCatalog` (`src/InfraGate.McpGateway/McpTransport/Dispatch/ToolScopeCatalog.cs`) — a single source of truth mapping each tool to its minimum required scope(s):
- `request_*` tools → `mcp:tools` or `mcp:tools.write`
- `execute_approved_plan` / `wait_for_plan_approval` → `mcp:tools` or `mcp:tools.execute` or `mcp:tools.write`
- `get_plan_status` → `mcp:tools` or `mcp:tools.readonly` or `mcp:tools.read`
- `propose_plan` → `mcp:tools` or `mcp:tools.propose` or `mcp:tools.write`
- Downstream ReadOnly tools → `mcp:tools` or `mcp:tools.readonly` or `mcp:tools.read`
- Downstream Destructive tools → `mcp:tools` or `mcp:tools.write`

`ToolScopeGuard` (`ToolScopeGuard.cs`) enforces these at call-time in `GatewayToolDispatcher.CallToolAsyncCore` (lines 96 and 167), returning an error result and writing a `scope.denied` audit event on mismatch. `ToolScopeCatalog.IsVisibleTo` also filters the `tools/list` response so clients only see tools their scope permits.

Agent service identities use their appropriate scopes: Observer → `mcp:tools.readonly` (`ObserverConventions.cs:17`), Planner → `mcp:tools.propose mcp:tools.readonly` (`PlannerConventions.cs:11`), Executor → `mcp:tools.execute` (`ExecutorConventions.cs:11`). Human operators use `mcp:tools.read` or `mcp:tools.write` as documented in `docs/tool-permissions.md` and the README.

The naming follows `mcp:tools.{role}` (e.g., `mcp:tools.read`) rather than the shorter `mcp:read` originally recommended — this is a consistent namespace choice, not a functional gap. All tool scopes live under the `mcp:tools` prefix.

---

## Summary Table

| ID | Finding | Severity | Source | Affected Diagram | Resolution (2026-06-05) |
|---|---|---|---|---|---|
| F-01 | Auto-approval loophole — human presence not provable | **Critical** | Original (extended) | Diagram 3 | ✅ Mitigated |
| F-02 | TOCTOU via stale dry-run / force-conflicts | **High** | Original | Diagram 3 | ✅ Mitigated |
| F-03 | Prompt injection sanitization fallacy | **High** | Original | Diagram 2 | ✅ Mitigated with residual risk |
| F-04 | Disk exhaustion & path traversal in ApprovalStore | **High** | Original | Diagram 3 | ✅ Mitigated |
| F-05 | Loopback port hijacking in DCR | **Medium** | Original | Diagram 1 | ⚠️ Partial |
| F-06 | Subprocess blast radius under shared Service Account | **High** | Original | Diagram 2 | ⚠️ Partial |
| F-07 | JWT Bearer replay — no proof-of-possession | **High** | New | Diagrams 1 & 2 | ⚠️ Partial |
| F-08 | No user-to-plan binding — cross-user plan approval | **High** | New | Diagram 3 | ✅ Mitigated |
| F-09 | No JWT revocation mechanism | **Medium** | New | Diagrams 1 & 2 | ✅ Mitigated |
| F-10 | Subprocess binary integrity not verified | **High** | New | Diagrams 2 & 3 | ❌ Not mitigated |
| F-11 | JWKS cache poisoning / key rollover race | **Medium** | New | Diagram 1 | ❌ Not mitigated |
| F-12 | Audit log tamper by compromised process | **Medium** | New | Diagrams 2 & 3 | ⚠️ Partial |
| F-13 | Single scope for read and write operations | **High** | New | All | ✅ Mitigated |

---

## Recommended Prioritization

### Immediate (before any production use)

| Priority | Finding | Reason | Status |
|---|---|---|---|
| 1 | ✅ **F-13** — Split `mcp:read` / `mcp:write` scopes | Architectural change required; blocks all other scope-based controls | ✅ Done — `mcp:tools.read`/`.write` for human operators + role-based scopes for agents |
| 2 | **F-08** — Bind plans to creator `sub` claim | Prevents cross-user approval with minimal implementation effort | ✅ Done — SameSubject + OperatorApproval policies implemented |
| 3 | **F-04** — UUID validation + rate limit on planId | Low-effort fix with high DoS / traversal impact | ✅ Done — PostgreSQL migration + IsSafePlanId validation |
| 4 | ✅ **F-02** — Capture and assert `resourceVersion` | Prevents silent overwrite of human-made changes | ✅ Mitigated — Two-layer defense: pre-execution freshness check + SSA server-side precondition |

### Short-term (within one sprint)

| Priority | Finding | Reason | Status |
|---|---|---|---|
| 5 | **F-07** — DPoP or short-lived tokens + log scrubbing | Significantly reduces replay window | ⚠️ Partial — DPoP for internal clients, in-memory replay store |
| 6 | ✅ **F-09** — Token revocation / introspection | Enables incident response to stolen tokens | ✅ Done — issuer introspection + short max access-token lifetime enforced |
| 7 | **F-10** — Binary hash pinning at startup | Low-cost, high-value supply-chain control | ❌ Open |
| 8 | **F-06** — Split Service Accounts by read/write | Reduces blast radius of subprocess compromise | ⚠️ Partial — RBAC split exists, single subprocess instance |

### Medium-term (hardening pass)

| Priority | Finding | Reason | Status |
|---|---|---|---|
| 9 | **F-01** — OOB approval channel | Full fix requires external infrastructure | ✅ Done — browser-based approval with OAuth PKCE |
| 10 | **F-12** — Remote audit log shipping | Requires out-of-process log sink | ⚠️ Partial — PG audit has hash chaining, no SIEM shipping |
| 11 | ✅ **F-03** — Schema-enforced tool output isolation | Requires LLM prompt engineering + testing | ✅ Done — read-only tool-result envelope + model-visible guard path; semantic classifier remains deferred by ADR-0030 |
| 12 | **F-11** — JWKS `kid` pinning + bounded cache TTL | Configuration-level fix once token plumbing is stable | ❌ Open |
| 13 | **F-05** — Subprocess / container isolation | Deployment environment dependent | ⚠️ Partial — container isolation added, no OS-level checks |

---

*This report is based solely on static analysis of the provided architecture diagrams and system description. A full dynamic security assessment, including code review, penetration testing, and runtime analysis, is recommended before production deployment.*
