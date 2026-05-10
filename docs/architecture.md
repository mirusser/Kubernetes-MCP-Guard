# Architecture

This document is the consolidated system map for Kubernetes MCP Guard. It focuses on components and request flows; detailed protocol, security, tool-permission, and configuration references live in:

- [docs/MCP-compliance.md](MCP-compliance.md) for MCP transport, OAuth 2.1, PKCE, protected-resource metadata, and RFC 8707 details.
- [docs/security-model.md](security-model.md) for hard boundaries, threat model, non-goals, and production warnings.
- [docs/tool-permissions.md](tool-permissions.md) for per-tool RBAC verbs, OAuth scope, and approval requirements.
- [docs/configuration.md](configuration.md) for environment variables, defaults, examples, and production guidance.

## Component Map

```mermaid
---
title: Kubernetes MCP Guard Components
---
flowchart TB
    Client["MCP client<br/>Codex / Open WebUI / LibreChat"]
    Browser["Human browser<br/>approval UI"]

    subgraph GatewayRuntime["Gateway runtime"]
        Gateway["InfraGate.McpGateway<br/>HTTP MCP /mcp"]
        Auth["InfraGate.McpGateway.Auth<br/>JWT validation + browser OAuth cookie"]
        Guardrails["GuardedToolRunner<br/>prompt-injection scan + response sanitization"]
        Downstream["DownstreamMcpClient<br/>stdio client transport"]
        Gateway --> Auth
        Gateway --> Guardrails
        Guardrails --> Downstream
    end

    subgraph DevAuth["Development identity provider"]
        DevIssuer["InfraGate.DevIssuer<br/>localhost OAuth/OIDC issuer"]
    end

    subgraph ServerRuntime["Kubernetes MCP server"]
        Server["InfraGate.McpServer<br/>private stdio subprocess"]
        Tools["K8sTools<br/>typed MCP tool surface"]
        Manager["K8sManager<br/>namespace validation, observability, plans, apply"]
        Parser["K8sManifestParser<br/>Deployment / Service / ConfigMap allow-list"]
        Server --> Tools --> Manager
        Manager --> Parser
    end

    subgraph Storage["Local durable storage"]
        ApprovalStore["ApprovalStore<br/>K8S_MCP_APPROVAL_ROOT"]
        Pending["pending/*.json"]
        Approved["approved/*.sha256"]
        Applied["applied/*.json"]
        Challenges["challenges/*.json"]
        ApprovalAudit["audit.jsonl<br/>approval events"]
        GuardAudit[".mcp-guardrails/audit.jsonl<br/>guardrail events"]
        ApprovalStore --> Pending
        ApprovalStore --> Approved
        ApprovalStore --> Applied
        ApprovalStore --> Challenges
        ApprovalStore --> ApprovalAudit
    end

    subgraph Kubernetes["Kubernetes boundary"]
        RBAC["Namespace-scoped RBAC"]
        Api["Kubernetes API"]
        RBAC --> Api
    end

    Client -->|"HTTP MCP + JWT"| Gateway
    Browser -->|"/approvals/* + OAuth cookie"| Gateway
    Client -. "OAuth discovery/login" .-> DevIssuer
    Browser -. "approval OAuth login" .-> DevIssuer
    Auth -. "JWKS / issuer metadata" .-> DevIssuer
    Downstream -->|"stdio, no bearer token"| Server
    Guardrails --> GuardAudit
    Manager --> ApprovalStore
    Manager -->|"KubernetesClient"| RBAC
```

In source mode, the gateway launches the server project as a private stdio subprocess. In container mode, the `mcp-gateway` image contains the published server assembly and starts it through `dotnet /app/server/InfraGate.McpServer.dll`. DevIssuer is a development-only identity provider; production deployments use an external OIDC issuer.

## OAuth Login And MCP Authorization

```mermaid
---
title: OAuth Login And Authorization
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001<br/>Resource Server
    participant Issuer as DevIssuer :3011<br/>Auth Server

    Note over User,Issuer: OAuth login and MCP authorization (once per session)

    User->>Client: start MCP session

    Note over Client,GW: Gateway advertises auth requirements
    Client->>GW: POST /mcp (initial request, no token)
    GW-->>Client: 401 Unauthorized<br/>+ WWW-Authenticate Bearer
    alt token present but insufficient scope
        Client->>GW: POST /mcp (JWT Bearer)
        GW-->>Client: 403 Forbidden<br/>+ WWW-Authenticate error="insufficient_scope"<br/>+ resource_metadata
    end
    Note over GW: GatewayAuthentication<br/>AddGatewayAuthentication<br/>JwtBearerEvents.OnForbidden

    Note over Client,Issuer: MCP resource discovery
    Client->>GW: GET /.well-known/oauth-protected-resource
    GW-->>Client: authorization server URI<br/>+ available scopes (mcp:tools)
    Note over GW: .AddMcp() hosts<br/>protected-resource metadata<br/>RFC 9728
    Client->>Issuer: GET /.well-known/openid-configuration
    Issuer-->>Client: OAuth/OIDC metadata<br/>(authorize / token / register / JWKS)

    opt Dynamic client registration (first session only)
        Note over Client,Issuer: MCP DCR, loopback redirect URI only
        Client->>Issuer: POST /register (loopback redirect URI)
        Issuer-->>Client: client_id
        Note over Issuer: DevIssuerStore<br/>ClientAllowsRedirectUri<br/>IsLoopbackHttpUri
    end

    Note over Client,Issuer: Authorization Code + PKCE S256
    Client->>Issuer: GET /authorize<br/>(PKCE S256, resource=..., scope=mcp:tools)
    Issuer-->>Client: redirect to loopback callback<br/>code + state
    Note over Issuer: DevIssuerApplication.Authorize<br/>resource parameter binding (RFC 8707)<br/>code is resource-bound
    Client->>Issuer: POST /token<br/>(grant_type=authorization_code<br/>code + redirect_uri + client_id + code_verifier)
    Issuer-->>Client: JWT access token<br/>(aud = resource, scope = mcp:tools)
    Note over Issuer: DevIssuerApplication.TokenAsync<br/>PkceMatches, S256 enforced<br/>audience/resource bounded
```

## Read-Only Tool Call

```mermaid
---
title: Read-Only Tool Call
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001
    participant Svr as MCP Server (stdio subprocess)
    participant K8s as Kubernetes API

    Note over User,K8s: Read-only tool call, for example get_k8s_status

    User->>Client: request get_k8s_status
    Client->>GW: POST /mcp -> get_k8s_status(namespace)<br/>JSON-RPC + JWT Bearer

    Note over GW: JWT validation
    GW->>GW: validate issuer / audience / lifetime<br/>signature / scope (mcp:tools)
    Note over GW: GatewayAuthentication<br/>scope enforcement

    Note over GW: Prompt-injection guardrails
    GW->>GW: GuardedToolRunner scans request arguments
    Note over GW: K8sGatewayTools delegates to GuardedToolRunner<br/>ignore-instructions / reveal-prompts<br/>tool-use / secret-exfiltration<br/>authority-override

    Note over GW,Svr: Token passthrough prevention
    GW->>Svr: forward tool call<br/>(StdioClientTransport, no token)
    Note over GW,Svr: DownstreamMcpClient.GetClientAsync<br/>OAuth JWT terminated at gateway

    Note over Svr: K8sTools tool handlers
    Svr->>Svr: validate namespace in allowed list
    Svr->>K8s: KubernetesClient GET/LIST<br/>(namespace-scoped, bounded)
    K8s-->>Svr: Kubernetes API response<br/>(Deployments, Services, ConfigMaps, Pods, ReplicaSets)
    Note over Svr: K8sManager observability<br/>no Secret values, ConfigMap data, raw manifests

    Note over GW: Response sanitization + audit
    Svr-->>GW: tool result text
    GW->>GW: sanitize response<br/>(redact manifest blocks<br/>redact prompt-injection-risk lines)
    GW->>GW: GuardrailAuditStore.Append<br/>(if suspicious content detected)

    GW-->>Client: MCP response (Streamable HTTP)
    Note over GW: Program.cs .WithHttpTransport()
```

## Mutation Plan Request

```mermaid
---
title: Mutation Plan Request
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001
    participant Svr as MCP Server (stdio subprocess)
    participant Store as ApprovalStore
    participant K8s as Kubernetes API

    Note over User,K8s: Step 1: request_* creates a pending plan

    User->>Client: request a change, for example scale a Deployment
    Client->>GW: POST /mcp -> request_scale_deployment<br/>(namespace, name, replicas)<br/>JSON-RPC + JWT Bearer

    GW->>GW: validate JWT + scope
    GW->>GW: GuardedToolRunner scans args
    GW->>Svr: forward tool call<br/>(StdioClientTransport, no token)

    Note over Svr: Plan creation
    Svr->>Svr: validate namespace, name, replicas, or manifest kind
    Svr->>Svr: K8sManifestParser allows Deployment / Service / ConfigMap
    Svr->>K8s: dry-run against K8s API<br/>(dryRun=All, strict field validation)
    Svr->>Store: write pending plan with dry-run result<br/>+ compute SHA-256 hash
    Store-->>Svr: PlanId + pending path + plan hash
    Note over Svr,Store: K8sManager.Request*<br/>ApprovalStore (.mcp-approvals/pending/)
    Svr-->>GW: PlanId + plan summary<br/>(dry-run result, affected resources)

    GW->>GW: sanitize response, audit if needed
    GW-->>Client: pending plan details + next step
```

## Browser Approval Challenge

```mermaid
---
title: Browser Approval Challenge
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant Browser as Browser
    participant GW as Gateway :3001
    participant Store as ApprovalStore

    Note over User,Store: Step 2: first apply_approved_plan call creates an out-of-band challenge

    User->>Client: apply approved plan
    Client->>GW: POST /mcp -> apply_approved_plan(planId)<br/>JSON-RPC + JWT Bearer
    GW->>GW: validate JWT + scope
    GW->>Store: read pending plan + current hash
    GW->>GW: create single-use challenge<br/>bound to planId + hash + requester subject + expiry
    GW->>Store: write challenge file<br/>+ approval_challenge_created audit event
    GW-->>Client: approval required<br/>PlanId + plan hash + approval URL

    User->>Browser: open approval URL
    Browser->>GW: GET /approvals/{challengeId}
    GW->>GW: require approval OAuth cookie<br/>or redirect to /approvals/login
    GW->>GW: validate same authenticated subject<br/>+ challenge status + expiry
    GW->>Store: read actual pending plan from disk
    GW-->>Browser: render Gateway-owned approval page<br/>PlanId + hash + objects + dry-run status + expiry

    User->>Browser: approve or deny
    Browser->>GW: POST /approvals/{challengeId}/approve<br/>or /deny with anti-forgery token
    GW->>Store: recompute pending plan SHA-256
    alt hash still matches
        GW->>Store: write approved hash<br/>+ approval_challenge_approved audit event
        GW-->>Browser: approval recorded
    else hash changed
        GW->>Store: write approval_hash_mismatch / rejected audit
        GW-->>Browser: approval failed
    end
```

The MCP client receives only the approval URL and status text. It does not submit approval content through MCP, and the browser approval session must authenticate as the same subject that requested the plan.

## Approved Apply

```mermaid
---
title: Approved Apply
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001
    participant Svr as MCP Server (stdio subprocess)
    participant Store as ApprovalStore
    participant K8s as Kubernetes API

    Note over User,K8s: Step 3: retry apply_approved_plan after browser approval

    User->>Client: retry apply approved plan
    Client->>GW: POST /mcp -> apply_approved_plan(planId)<br/>JSON-RPC + JWT Bearer
    GW->>GW: validate JWT + scope
    GW->>Store: find approved challenge for planId + subject
    GW->>Store: validate approved hash exists and matches
    GW->>Svr: forward apply_approved_plan(planId)<br/>(no token)

    Svr->>Store: read pending plan + approved hash
    Svr->>Store: recompute pending plan SHA-256
    alt hash still matches
        Svr->>K8s: repeat dry-run<br/>(dryRun=All)
        Svr->>K8s: apply mutation<br/>(server-side apply / patch / delete)
        K8s-->>Svr: Kubernetes API response
        Svr->>Store: write applied plan<br/>+ plan_applied audit event
        Svr-->>GW: apply result + current status
    else hash changed
        Svr->>Store: write approval_hash_mismatch audit event
        Svr-->>GW: refused
    end

    GW->>GW: sanitize response, GuardrailAuditStore.Append if needed
    GW-->>Client: success or refusal
```

## Audit Flow

```mermaid
---
title: Audit Flow
---
flowchart LR
    Request["MCP request arguments"] --> Guard["PromptInjectionGuard"]
    Response["Downstream tool response"] --> Sanitizer["Response sanitization"]
    Guard -->|"suspicious input"| GuardAudit[".mcp-guardrails/audit.jsonl"]
    Sanitizer -->|"suspicious or redacted output"| GuardAudit

    Plan["request_* plan"] --> PlanAudit["K8S_MCP_APPROVAL_ROOT/audit.jsonl<br/>plan_requested"]
    Challenge["approval challenge"] --> ChallengeAudit["K8S_MCP_APPROVAL_ROOT/audit.jsonl<br/>approval_challenge_*"]
    Approve["approve / deny / expire / reject"] --> ChallengeAudit
    Apply["apply_approved_plan"] --> ApplyAudit["K8S_MCP_APPROVAL_ROOT/audit.jsonl<br/>plan_applied / apply_denied / apply_failed"]
```

Guardrail audit and approval audit are separate streams. Guardrail audit records model-visible prompt-injection findings and response redaction actions. Approval audit records plan, challenge, approval, denial, expiry, hash mismatch, and apply events under `K8S_MCP_APPROVAL_ROOT/audit.jsonl`.

## Image And Registry Layout

| Runtime image | GHCR | Docker Hub | Contains |
| --- | --- | --- | --- |
| Gateway | `ghcr.io/mirusser/kubernetes-mcp-guard-gateway:<tag>` | `mirusser/kubernetes-mcp-guard-gateway:<tag>` | `InfraGate.McpGateway`, `InfraGate.McpGateway.Auth`, `InfraGate.Approvals`, and published downstream server assembly at `/app/server/InfraGate.McpServer.dll` |
| DevIssuer | `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:<tag>` | `mirusser/kubernetes-mcp-guard-devissuer:<tag>` | `InfraGate.DevIssuer` development OAuth/OIDC issuer |

The local/demo source Compose file builds both images locally, and the local/demo release Compose file pulls GHCR images by default while documenting Docker Hub equivalents. The deployment Compose files under `deploy/compose/` deploy the gateway image only; development uses the local Keycloak setup script, while production uses a real OIDC provider. Tags and CI/CD settings are owned by [docs/configuration.md](configuration.md) and the release process docs.
