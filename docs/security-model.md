# Security Model

This document consolidates the Kubernetes MCP Guard security model; OAuth 2.1, PKCE, protected-resource metadata, and RFC 8707 protocol details are owned by [docs/MCP-compliance.md](MCP-compliance.md).

Kubernetes MCP Guard is experimental. See the [security policy](../SECURITY.md) for supported-version and vulnerability-reporting expectations.

## 1. Hard Boundaries

The following controls are hard enforcement boundaries. Violating any of them causes the request to be rejected regardless of other configuration.

### 1.1 Kubernetes RBAC

The demo deployment uses a namespace-scoped ServiceAccount, Role, and RoleBinding in [`deploy/minikube/rbac.yaml`](../deploy/minikube/rbac.yaml). The Kubernetes API server enforces those verbs independently of the gateway, so even a gateway bug does not grant cluster-scoped permissions beyond the credentials it runs with.

See [docs/tool-permissions.md](tool-permissions.md) for the per-tool Kubernetes verb matrix.

### 1.2 JWT Validation and Scope Enforcement

The HTTP gateway validates JWT issuer, resource-bound audience, lifetime, signature, and required scope before MCP tool calls reach the downstream server. Audience comparison normalizes a trailing slash so the configured resource and JWT audience can match consistently.

The default required scope is `mcp:tools`, configurable through `InfraGate__Auth__OAuthScope`. A valid token without that scope receives HTTP 403 with a `WWW-Authenticate: Bearer error="insufficient_scope"` challenge.

See [`src/InfraGate.McpGateway.Auth/README.md`](../src/InfraGate.McpGateway.Auth/README.md) for gateway auth contracts and step-up challenge behavior. See [docs/MCP-compliance.md](MCP-compliance.md) for the OAuth 2.1, PKCE, protected-resource metadata, and RFC 8707 details.

### 1.3 Namespace Allow-list

`InfraGate__Kubernetes__AllowedNamespaces__0` is a single indexed namespace entry, not a comma-separated list. Provide additional namespaces as separate indexed keys (`InfraGate__Kubernetes__AllowedNamespaces__1`, `__2`, and so on). The default single value is `mcp-nginx-demo`.

`InfraGate.McpServer` checks the allow-list before Kubernetes API calls, providing a second containment layer beyond Kubernetes RBAC.

### 1.4 Approval-Gated Mutation Flow

The `request_*` tools create pending plans only after Kubernetes `dryRun=All` succeeds; they do not persist Kubernetes mutations at request time. The dry-run result is stored inside the pending plan, and its Evidence Artifact digest summary is covered by the Review Digest; the executable mutation is covered by the Intent Digest. The MCP client then calls `execute_approved_plan`, and the gateway returns a browser approval URL instead of forwarding the apply call immediately.

Approval happens out of band in the gateway-hosted browser UI. The challenge has a cryptographically random ID, a TTL, the requester subject, the current pending-plan hash, expected Intent Digest, expected Review Digest, and Single-Execution status. The browser approval flow requires a separate OAuth session bound to the same subject.

Approval issues an Approval Grant persisted in PostgreSQL `approvals.approval_grants`, bound to the plan id, requester, approver, challenge id, Intent Digest, Review Digest, approval policy, reuse policy, and expiry. The gateway still recomputes the pending-plan hash at challenge creation and approval time to detect approval-URL drift. The gateway validates the grant and digests before apply, then asks the Kubernetes adapter to repeat Kubernetes dry-run immediately before the real write. A mismatch is refused; dry-run failures are audited as `execution.blocked` with a dry-run failure payload.

The gateway approval endpoints require anti-forgery tokens, same-subject binding, challenge TTL checks, and pending-plan hash plus digest time-of-check/time-of-use re-verification at approval time.

See [`src/InfraGate.Approvals/README.md`](../src/InfraGate.Approvals/README.md) for the approval persistence contracts and [`src/InfraGate.McpGateway/README.md`](../src/InfraGate.McpGateway/README.md) for `GatewayApprovalService` and browser approval endpoints.

## 2. Defense-in-Depth

The following mechanisms reduce risk and increase observability, but they are not enforcement hard boundaries.

### 2.1 Prompt-Injection Guardrails

The gateway scans model-visible inputs and outputs for these guardrail categories:

1. `ignore-instructions`
2. `reveal-prompts`
3. `tool-use`
4. `secret-exfiltration`
5. `authority-override`
6. `manifest-echo`

On detection, the gateway writes a guardrail audit finding and still forwards the request. Suspicious values in the response are redacted before the response returns to the MCP client.

### 2.2 Response Sanitization

Echoed manifest blocks are redacted to `[redacted: inspect the pending plan file before approval]`. Suspicious response values or lines are redacted to `[redacted: prompt-injection-risk]`. Clean responses pass through unchanged.

### 2.3 Guardrail Audit Logging

Guardrail audit entries are written to `.mcp-guardrails/audit.jsonl` by default, configurable through `InfraGate__Gateway__GuardAuditRoot`. Entries include `toolName`, `direction`, `action` (`warn`, `warn_redact`, `redact_manifest`, or `redact_sensitive_data`), `categories`, `planId`, `subject`, and `authenticationType`.

Sensitive-data redaction adds the `sensitive-data` category and, when redaction fires, writes `redactionPatterns` (the distinct pattern names that matched) and `redactionCount` (pattern name to match count). The matched secret values themselves are never stored in the audit entry.

Approval audit is separate and is persisted in PostgreSQL `approvals.audit_outbox`.

### 2.4 MCP Tool Annotations

Tool definitions use MCP annotations such as `ReadOnly = true` and `Destructive = true`. Compliant clients may use these annotations for UI policy, but the gateway does not rely on them for enforcement.

### 2.5 Hash-Bound Approvals

Hash binding protects the integrity of the approval signal after a user approves a plan. The hard boundary is the approval requirement itself, described in section 1.4; the SHA-256 binding is defense-in-depth on top of that requirement.

### 2.6 Manifest Allow-list

`KubernetesManifestParser` accepts only `apps/v1 Deployment`, `v1 Service`, and `v1 ConfigMap` manifests for apply and delete planning. Other kinds are rejected before any Kubernetes write can occur.

### 2.7 Bounded Observability

Events default to 50 and are capped at 100. Pod logs default to 200 tail lines, are capped at 500 tail lines, and have a 65536-byte hard cap. Diagnostics cap related Pods and ReplicaSets at 50 items. Scale requests cap replicas to 0 through 5.

The tool surface does not expose exec, attach, or port-forward.

### 2.8 No Privileged Operations

The server does not provide kubectl exec passthrough, shell execution, namespace creation, RBAC manipulation, Secret reads, raw manifests for reads, or cluster-scoped writes.

## 3. Threat Model

### 3.1 Assumptions

- The Kubernetes API server enforces RBAC correctly.
- The configured identity provider issues valid tokens.
- The gateway is deployed behind TLS in production.
- MCP clients may be untrusted or partially trusted.
- AI-generated suggestions may be incorrect or unsafe.

### 3.2 Risks Reduced

- Overbroad AI access to Kubernetes.
- Unauthorized mutation attempts.
- Plan tampering after approval.
- Prompt injection influencing responses.
- Accidental unsafe changes.

### 3.3 Out of Scope

- Compromised Kubernetes cluster.
- Compromised identity provider.
- Malicious admin with `cluster-admin`.
- Compromised gateway host.

## 4. Non-Goals

- Not a replacement for Kubernetes RBAC.
- Local Keycloak is not a production identity provider. See [docs/production-oidc.md](production-oidc.md) for production OIDC guidance.
- Not a full policy engine.
- No guarantee that AI-generated actions are correct or safe.
- Not production-certified; the project is experimental.
- Prompt-injection guardrails are defense-in-depth, not a guaranteed hard boundary.

## 5. Development-Only Components

Must not be used in any production or shared environment.

### 5.1 Local Keycloak

The local Keycloak realm runs via `start-dev` over HTTP and enables anonymous DCR only for loopback demo use. It is not suitable for production.

### 5.2 `InfraGate__Auth__OAuthRequireHttpsMetadata=false`

`InfraGate__Auth__OAuthRequireHttpsMetadata=false` disables the HTTPS requirement for OIDC discovery metadata. It is acceptable only when the gateway points at a local HTTP issuer during development, such as the local Keycloak demo.

See [`src/InfraGate.McpGateway.Auth/README.md`](../src/InfraGate.McpGateway.Auth/README.md).

### 5.3 Static Bearer Tokens

Static bearer token authentication is not a supported gateway mode. Opaque bearer values such as `change-me` are rejected by JWT validation before signature or scope checks. Use the local Keycloak demo for local OAuth testing or a production OIDC provider for shared environments.

See [`src/InfraGate.McpGateway.Auth/README.md`](../src/InfraGate.McpGateway.Auth/README.md) and [docs/mcp-clients-quirks.md](mcp-clients-quirks.md).

### 5.4 `scripts/create-demo-kubeconfig.sh`

[`scripts/create-demo-kubeconfig.sh`](../scripts/create-demo-kubeconfig.sh) generates a 24-hour ServiceAccount token for the minikube demo namespace and writes gitignored kubeconfig files. It is for local minikube demos only.

See [docs/setup-guide.md](setup-guide.md).
