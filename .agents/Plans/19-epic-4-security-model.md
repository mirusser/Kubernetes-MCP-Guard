# Epic 4 — Security Model Documentation

## Context

The k8s-toolkit (public name: **Kubernetes MCP Guard**) is a .NET 10 MCP gateway/server for AI-safe Kubernetes operations. Security properties are currently spread across README, MCP-compliance.md, and per-project READMEs with no consolidated threat model or per-tool RBAC matrix. `SECURITY.md:34` already contains a forward reference to a planned `docs/security-model.md`. Epic 4 creates that doc, a companion tool permissions matrix, and wires both into the README and SECURITY.md.

**Documentation-only epic — no code changes.**

---

## Files to Create / Modify

| Action | File |
|--------|------|
| Create | `docs/security-model.md` |
| Create | `docs/tool-permissions.md` |
| Edit | `README.md` — add 2 links in "Explore The Project" section |
| Edit | `SECURITY.md` — resolve the forward reference at line 34 |

---

## Order of Operations

1. Create `docs/security-model.md` (references only existing files)
2. Create `docs/tool-permissions.md` (cross-links to `security-model.md`)
3. Edit `README.md` — insert two lines after line 223 (MCP compliance notes)
4. Edit `SECURITY.md` — change "is planned for Epic 4 of the roadmap" → link to the new doc

---

## `docs/security-model.md` — Structure and Content

Opening sentence: orient reader and cross-link to `docs/MCP-compliance.md` (owns OAuth 2.1 / PKCE / RFC 8707 details — do not restate them here). Add one-line experimental status note linking to `SECURITY.md`.

```
# Security Model

## 1. Hard Boundaries
### 1.1 Kubernetes RBAC
### 1.2 JWT Validation and Scope Enforcement
### 1.3 Namespace Allow-list
### 1.4 Approval-Gated Mutation Flow

## 2. Defense-in-Depth
### 2.1 Prompt-Injection Guardrails
### 2.2 Response Sanitization
### 2.3 Guardrail Audit Logging
### 2.4 MCP Tool Annotations
### 2.5 Hash-Bound Approvals
### 2.6 Manifest Allow-list
### 2.7 Bounded Observability
### 2.8 No Privileged Operations

## 3. Threat Model
### 3.1 Assumptions
### 3.2 Risks Reduced
### 3.3 Out of Scope

## 4. Non-Goals

## 5. Development-Only Components
### 5.1 DevIssuer
### 5.2 INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false
### 5.3 Static Bearer Token Authentication
### 5.4 scripts/create-demo-kubeconfig.sh
```

### Section 1 — Hard Boundaries

Open section with: "The following controls are hard enforcement boundaries. Violating any of them causes the request to be rejected regardless of other configuration."

**1.1 Kubernetes RBAC**
- Namespace-scoped ServiceAccount + Role + RoleBinding in `deploy/minikube/rbac.yaml`
- The Kubernetes API server enforces these verbs independently of the gateway
- Cross-link to `docs/tool-permissions.md` for the per-tool verb matrix

**1.2 JWT Validation and Scope Enforcement**
- Validated claims: issuer, audience (resource-bound, RFC 8707, trailing-slash normalized), lifetime, signature
- Required scope: `mcp:tools` (configurable via `INFRA_GATE_OAUTH_SCOPE`)
- Insufficient scope → HTTP 403 + `WWW-Authenticate: Bearer error="insufficient_scope"`
- Cross-link to `src/InfraGate.McpGateway.Auth/README.md` for step-up challenge detail
- Cross-link to `docs/MCP-compliance.md` for OAuth 2.1 / PKCE / RFC 8707 — **do not restate**

**1.3 Namespace Allow-list**
- `K8S_MCP_ALLOWED_NAMESPACES` (comma-separated, default `mcp-nginx-demo`)
- Enforced in McpServer before any K8s API call; second containment layer beyond RBAC

**1.4 Approval-Gated Mutation Flow**
- `request_*` tools create a pending plan (no K8s write at request time)
- `apply_approved_plan` returns a Gateway-hosted browser approval URL (single-use challenge with cryptographic random ID and TTL); applies only after explicit out-of-band user approval in a browser with a separate OAuth session bound to the same identity
- SHA-256 of pending plan stored separately; recomputed at both challenge creation and approval time; mismatch → refused + `approval_hash_mismatch` audit entry
- Gateway enforces anti-forgery tokens, same-subject binding, challenge TTL expiry, and plan-hash TOCTOU re-verification at approval time
- Cross-link to `src/InfraGate.McpServer/README.md` for ApprovalStore contract and `src/InfraGate.McpGateway/README.md` for GatewayApprovalService and browser approval endpoints

### Section 2 — Defense-in-Depth

Open section with: "The following mechanisms reduce risk and increase observability, but they are not enforcement hard boundaries." (Directly satisfies acceptance criterion on guardrails framing.)

**2.1 Prompt-Injection Guardrails** — five pattern categories (from `McpGatewayConventions.GuardrailCategories`):
1. `ignore-instructions`
2. `reveal-prompts`
3. `tool-use`
4. `secret-exfiltration`
5. `authority-override`

On detection: finding logged to audit; request still forwarded (not dropped); suspicious values in response redacted.

**2.2 Response Sanitization** — manifest blocks redacted to `[redacted: inspect the pending plan file before approval]`; suspicious lines → `[redacted: prompt-injection-risk]`; clean responses pass through unchanged.

**2.3 Guardrail Audit Logging** — written to `.mcp-guardrails/audit.jsonl` (configurable via `INFRA_GATE_GUARD_AUDIT_ROOT`). Fields: `toolName`, `direction`, `action` (warn/warn_redact/redact_manifest), `categories`, `planId`, `subject`, `authenticationType`. Approval audit is separate: `K8S_MCP_APPROVAL_ROOT/audit.jsonl`.

**2.4 MCP Tool Annotations** — `ReadOnly = true` / `Destructive = true/false` on tool definitions. Compliant clients may use for UI policy; gateway does not rely on these for enforcement.

**2.5 Hash-Bound Approvals** — SHA-256 binding protects integrity of the approval signal after user has approved. (Hard boundary is the approval requirement itself, section 1.4. Hash binding is defense-in-depth on top of it.)

**2.6 Manifest Allow-list** — only `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap` accepted by `K8sManifestParser`. Other kinds rejected before any K8s API call.

**2.7 Bounded Observability** — events: default 50 max 100; pod logs: default 200 lines max 500 lines 65536-byte cap; diagnostics: max 50 related items; scale replicas: 0–5. No exec/attach/port-forward.

**2.8 No Privileged Operations** — no kubectl exec passthrough, shell execution, namespace creation, RBAC manipulation, or cluster-scoped writes.

### Section 3 — Threat Model (use roadmap template verbatim)

**3.1 Assumptions:** K8s API server enforces RBAC correctly; configured IdP issues valid tokens; gateway deployed behind TLS in production; MCP clients may be untrusted or partially trusted; AI-generated suggestions may be incorrect or unsafe.

**3.2 Risks Reduced:** overbroad AI access to Kubernetes; unauthorized mutation attempts; plan tampering after approval; prompt injection influencing responses; accidental unsafe changes.

**3.3 Out of Scope:** compromised K8s cluster; compromised IdP; malicious admin with cluster-admin; compromised gateway host.

### Section 4 — Non-Goals (bulleted list)

- Not a replacement for Kubernetes RBAC
- DevIssuer is not a production IdP — link to `docs/production-oidc.md` (planned, Epic 5)
- Not a full policy engine
- No guarantee that AI-generated actions are correct or safe
- Not production-certified (experimental)
- Prompt-injection guardrails are defense-in-depth, not a guaranteed hard boundary

### Section 5 — Development-Only Components

Open with: "Must not be used in any production or shared environment."

**5.1 DevIssuer** — HTTP only, localhost only, all state in-memory, lost on restart; dynamic client registration loopback-only; PKCE S256 required. Cross-link to `src/InfraGate.DevIssuer/README.md`.

**5.2 INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false** — disables HTTPS check for OIDC discovery metadata; only acceptable pointing at localhost DevIssuer. Cross-link to `src/InfraGate.McpGateway.Auth/README.md`.

**5.3 Static Bearer Token Authentication** — constant-time comparison; no rotation, no expiry, no audience binding; for local demos only. Cross-link to `src/InfraGate.McpGateway.Auth/README.md`.

**5.4 scripts/create-demo-kubeconfig.sh** — generates 24-hour SA token for minikube only; gitignored. Cross-link to `docs/setup-guide.md`.

---

## `docs/tool-permissions.md` — Structure and Content

Opening sentence: "This document lists the 14 MCP tools exposed by Kubernetes MCP Guard and their associated Kubernetes RBAC permissions, required OAuth scope, and approval requirements." Cross-link to `docs/security-model.md`.

```
# Tool Permissions Matrix

## Common Properties

## Read-Only Tools  (8 tools)

## Plan Mutation Tools  (5 tools)

## Mutation Execution Tool  (1 tool)

## Notes
```

**Common Properties section:** All 14 tools require `mcp:tools` scope; all are namespace-scoped; Kubernetes RBAC is enforced independently; `ReadOnly`/`Destructive` are MCP annotations, not RBAC claims.

### Read-Only Tools table (8 rows)

| MCP Tool | MCP Annotation | K8s Verbs | K8s Resources | Approval Required | Bounds / Notes |
|---|---|---|---|---|---|
| `get_allowed_namespaces` | `ReadOnly = true` | none (reads config only) | — | No | Returns configured namespace allow-list; no K8s API call |
| `get_k8s_status` | `ReadOnly = true` | `get`, `list` | Deployments, Services, ConfigMaps, Pods, ReplicaSets | No | Supports optional label selector |
| `get_k8s_events` | `ReadOnly = true` | `list` | events.k8s.io/v1 Events | No | Default 50, max 100 |
| `get_pod_logs` | `ReadOnly = true` | `get` (pods/log subresource) | Pods | No | Default 200 lines, max 500 lines, 65536-byte hard cap |
| `get_k8s_resource` | `ReadOnly = true` | `get` | Deployment, ReplicaSet, Pod, Service, ConfigMap | No | Secret kind explicitly rejected; no raw manifests |
| `get_deployment_diagnostics` | `ReadOnly = true` | `get`, `list` | Deployment, ReplicaSet, Pod, Events | No | Related items capped at 50 |
| `get_pod_diagnostics` | `ReadOnly = true` | `get`, `list` | Pod, Events | No | Related items capped at 50 |
| `get_service_diagnostics` | `ReadOnly = true` | `get`, `list` | Service, Pod, Events | No | Related items capped at 50 |

### Plan Mutation Tools table (5 rows)

All: `Destructive = false`, K8s Verbs at call time: **none** (creates plan only), Scope: `mcp:tools`, Approval Required: No (at request time)

| MCP Tool | Bounds / Notes |
|---|---|
| `request_apply_manifest` | Manifest allow-list: `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap` only; creates SHA-256-bound pending plan |
| `request_delete_manifest` | Same allow-list; creates SHA-256-bound pending plan |
| `request_scale_deployment` | Replicas bounded 0–5; creates SHA-256-bound pending plan |
| `request_restart_deployment` | Creates SHA-256-bound pending plan for rollout restart |
| `request_set_deployment_image` | Creates SHA-256-bound pending plan for container image patch |

### Mutation Execution Tool (1 row)

`apply_approved_plan`: `Destructive = true`, K8s Verbs: depends on approved plan, Approval Required: **Yes** (out-of-band browser approval via Gateway-hosted single-use challenge URL; `scripts/approve-plan.sh` works only with direct stdio server, not through the Gateway), validates SHA-256 hash match and approved challenge record before any K8s call.

Follow with a sub-table of verbs applied by plan type:

| Plan Operation | K8s Verbs | K8s Resources |
|---|---|---|
| apply (from `request_apply_manifest`) | `create`, `update`, `patch` (server-side apply) | Deployment, Service, or ConfigMap |
| delete (from `request_delete_manifest`) | `delete` | Deployment, Service, or ConfigMap |
| scale (from `request_scale_deployment`) | `update`, `patch` | Deployment (scale subresource) |
| restart (from `request_restart_deployment`) | `update`, `patch` | Deployment (annotation patch) |
| set-image (from `request_set_deployment_image`) | `update`, `patch` | Deployment |

### Notes section

- Scope is a single flat `mcp:tools`; no `mcp:read`/`mcp:write` split today. If finer-grained scopes are added, update this matrix and `src/InfraGate.McpGateway.Auth/README.md` together.
- `get_allowed_namespaces` makes no K8s API call — it reads in-process configuration. Still subject to gateway JWT/scope enforcement.
- For plan mutation tools, no K8s write occurs until `apply_approved_plan` is called and user approves.

---

## `README.md` — Edit

Insert two lines after line 223 (`- MCP compliance notes: [docs/MCP-compliance.md](docs/MCP-compliance.md)`):

```markdown
- Security model: [docs/security-model.md](docs/security-model.md)
- Tool permissions matrix: [docs/tool-permissions.md](docs/tool-permissions.md)
```

---

## `SECURITY.md` — Edit

Line 34, change:

```
A full security model document (`docs/security-model.md`) covering hard boundaries, threat model, and non-goals is planned for Epic 4 of the roadmap.
```

to:

```
A full security model document covering hard boundaries, threat model, and non-goals is available at [docs/security-model.md](docs/security-model.md). The per-tool RBAC and scope matrix is at [docs/tool-permissions.md](docs/tool-permissions.md).
```

---

## Doc-Ownership Rules (what NOT to put in new docs)

- Setup commands, env-var tables → belong in `docs/setup-guide.md` / `docs/devs-readme.md`
- OAuth 2.1 / PKCE / RFC 8707 detail → belongs in `docs/MCP-compliance.md` (link instead)
- OIDC provider walkthroughs → belongs in `docs/production-oidc.md` (Epic 5, not yet created)
- Implementation code details → belong in per-project source READMEs

---

## Acceptance Criteria Verification

| Criterion | Where satisfied |
|-----------|----------------|
| Security model has its own document | `docs/security-model.md` |
| Threat model: assumptions, risks reduced, out-of-scope | `security-model.md` §3 (three sub-sections) |
| Per-tool matrix matches 14 shipped tools | `tool-permissions.md` — 3 tables, 14 rows (not 11 as in roadmap template) |
| README links to both new docs | Two new lines in "Explore The Project" section |
| Dev-only components clearly flagged | `security-model.md` §5 (four sub-sections, explicit warnings) |
| Guardrails as defense-in-depth, not hard boundary | `security-model.md` §2 opening + §4 last bullet |
| Links to MCP-compliance.md, not restating OAuth | `security-model.md` opener + §1.2 |

---

## Pitfalls

- **Roadmap template says 11 tools — actual code has 14.** Use 14 (confirmed from `K8sTools.cs`).
- **`get_allowed_namespaces` makes zero K8s API calls** — table must say `none`.
- **Hash-bound approvals appear in §1 and §2** — §1.4 = approval as hard boundary; §2.5 = SHA-256 integrity as defense-in-depth. Keep them separate.
- **`apply_approved_plan` K8s verbs are plan-type-dependent** — use the sub-table rather than listing verbs in the main row.
- **Do not imply production readiness** — both new docs need the experimental status note near the top.
- **Approval is out-of-band, not MCP elicitation** — the plan's original §1.4 and tool matrix referenced MCP elicitation, but the codebase now uses browser-based out-of-band approval via Gateway-hosted single-use challenge URLs. The MCP client receives a URL as plain text and cannot submit approval content through MCP. See `GatewayApprovalService.cs` for the challenge architecture.
- **`scripts/approve-plan.sh` only works with direct stdio server** — it writes an approval file but does not create an `ApprovalChallenge` record, so the Gateway will not accept it. The script is for direct `InfraGate.McpServer` use only.
