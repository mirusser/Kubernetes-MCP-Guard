# Architecture

This document is the consolidated system map for InfraGate. It focuses on components and request flows; detailed protocol, security, tool-permission, and configuration references live in:

- [docs/MCP-compliance.md](MCP-compliance.md) for MCP transport, OAuth 2.1, PKCE, protected-resource metadata, and RFC 8707 details.
- [docs/security-model.md](security-model.md) for hard boundaries, threat model, non-goals, and production warnings.
- [docs/tool-permissions.md](tool-permissions.md) for per-tool RBAC verbs, OAuth scope, and approval requirements.
- [docs/configuration.md](configuration.md) for environment variables, defaults, examples, and production guidance.

## Component Map

```mermaid
---
title: InfraGate Components
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

    subgraph DevAuth["Local identity provider"]
        Keycloak["Keycloak<br/>local/test OIDC issuer"]
    end

    subgraph GenericCore["Generic Approval Core"]
        ApprovalStore["InfraGate.Approvals<br/>plan envelopes, challenges, grants, audit spine"]
        Pending["pending/*.json<br/>Plan Envelope"]
        Grants["grants/*.json<br/>Approval Grant"]
        Applied["applied/*.json"]
        Challenges["challenges/*.json<br/>Approval Challenge / Challenge Outcome"]
        ApprovalAudit["audit.jsonl<br/>approval events"]
        ApprovalStore --> Pending
        ApprovalStore --> Grants
        ApprovalStore --> Applied
        ApprovalStore --> Challenges
        ApprovalStore --> ApprovalAudit
    end

    subgraph K8sAdapter["Kubernetes Adapter"]
        Adapter["InfraGate.KubernetesAdapter<br/>mutation intent, evidence, intent canonicalization"]
        Payload["KubernetesPlanPayload<br/>namespace, objects, diff, dry-run, policy findings"]
        Adapter --> Payload
    end

    subgraph ServerRuntime["Kubernetes MCP server"]
        Server["InfraGate.McpServer<br/>private stdio subprocess"]
        Tools["K8sTools<br/>typed MCP tool surface"]
        Manager["K8sManager<br/>namespace validation, observability, plans, apply"]
        Parser["K8sManifestParser<br/>Deployment / Service / ConfigMap allow-list"]
        Server --> Tools --> Manager
        Manager --> Parser
    end

    subgraph GuardrailStore["Guardrail audit"]
        GuardAudit[".mcp-guardrails/audit.jsonl<br/>guardrail events"]
    end

    subgraph Kubernetes["Kubernetes boundary"]
        RBAC["Namespace-scoped RBAC"]
        Api["Kubernetes API"]
        RBAC --> Api
    end

    Client -->|"HTTP MCP + JWT"| Gateway
    Browser -->|"/approvals/* + OAuth cookie"| Gateway
    Client -. "OAuth discovery/login + DCR" .-> Keycloak
    Browser -. "approval OAuth login" .-> Keycloak
    Auth -. "JWKS / issuer metadata" .-> Keycloak
    Downstream -->|"stdio, no bearer token"| Server
    Guardrails --> GuardAudit
    Manager -->|"creates typed envelopes"| Adapter
    Adapter -->|"persists + loads"| ApprovalStore
    Manager -->|"KubernetesClient"| RBAC
```

In source mode, the gateway launches the server project as a private stdio subprocess. In container mode, the `mcp-gateway` image contains the published server assembly and starts it through `dotnet /app/server/InfraGate.McpServer.dll`. Keycloak is the local/test identity provider. Production deployments use an external OIDC issuer.

The Generic Approval Core (`InfraGate.Approvals`) owns plan envelopes, approval challenges, challenge outcomes, approval grants, and the audit spine independent of any target system. The Kubernetes Adapter (`InfraGate.KubernetesAdapter`) owns Kubernetes mutation intents, evidence artifacts (diffs, dry-run results, policy findings), and intent-digest canonicalization. The adatper sits between the server and the approval store: the server builds typed intent/evidence through the adapter, and the adapter persists and loads generic envelopes from the approval store.

## OAuth Login And MCP Authorization

```mermaid
---
title: OAuth Login And Authorization
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001<br/>Resource Server
    participant Issuer as Keycloak :3010<br/>Auth Server

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
        Note over Client,Issuer: OIDC DCR, loopback redirect URI only
        Client->>Issuer: POST /clients-registrations/openid-connect
        Issuer-->>Client: client_id
        Note over Issuer: Keycloak registration policies<br/>trusted-hosts + allowed scopes + max clients
    end

    Note over Client,Issuer: Authorization Code + PKCE S256
    Client->>Issuer: GET /protocol/openid-connect/auth<br/>(PKCE S256, scope=mcp:tools)
    Issuer-->>Client: redirect to loopback callback<br/>code + state
    Client->>Issuer: POST /token<br/>(grant_type=authorization_code<br/>code + redirect_uri + client_id + code_verifier)
    Issuer-->>Client: JWT access token<br/>(aud = resource, scope = mcp:tools)
    Note over Issuer: aud emitted by mcp:tools audience mapper<br/>gateway enforces issuer/signature/lifetime/audience/scope
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
    Note over GW: GatewayToolDispatcher delegates read-only calls to GuardedToolRunner<br/>ignore-instructions / reveal-prompts<br/>tool-use / secret-exfiltration<br/>authority-override

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
    Svr->>Store: write pending plan with dry-run result<br/>+ Intent/Review Digest binding
    Store-->>Svr: PlanId + pending path + Plan Envelope
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
    GW->>GW: create approval challenge<br/>bound to planId + Intent Digest + Review Digest + requester subject + expiry
    GW->>Store: write challenge file<br/>+ approval_challenge_created audit event
    GW-->>Client: approval required<br/>PlanId + Intent/Review Digests + approval URL

    User->>Browser: open approval URL
    Browser->>GW: GET /approvals/{challengeId}
    GW->>GW: require approval OAuth cookie<br/>or redirect to /approvals/login
    GW->>GW: validate same authenticated subject<br/>+ challenge status + expiry
    GW->>Store: read actual pending plan from disk
    GW-->>Browser: render Gateway-owned approval page<br/>PlanId + Intent/Review Digests + objects + dry-run status + expiry

    User->>Browser: approve or deny
    Browser->>GW: POST /approvals/{challengeId}/approve<br/>or /deny with anti-forgery token
    GW->>Store: recompute Intent Digest and Review Digest
    alt bindings still match
        GW->>Store: record Challenge Outcome<br/>+ issue Approval Grant
        GW-->>Browser: approval recorded
    else pending plan changed
        GW->>Store: write rejected audit
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
    GW->>Store: validate Approval Grant for planId + subject
    alt gateway grant and subject checks pass
        GW->>Svr: forward apply_approved_plan(planId)<br/>(no token)
        Svr->>Store: read pending plan + Approval Grant
        Svr->>Store: validate grant expiry + Intent/Review Digests
        Svr->>K8s: repeat dry-run<br/>(dryRun=All)
        Svr->>K8s: apply mutation<br/>(server-side apply / patch / delete)
        K8s-->>Svr: Kubernetes API response
        Svr->>Store: write applied plan<br/>+ plan_applied audit event
        Svr-->>GW: apply result + current status
    else grant, subject, or digest validation fails
        GW->>Store: write apply_denied audit event
        GW->>GW: format refusal
    end

    GW->>GW: sanitize downstream response if present, GuardrailAuditStore.Append if needed
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

The local/demo Compose path builds or pulls the gateway image and starts Keycloak from `quay.io/keycloak/keycloak:26.6.1`. The deployment Compose files under `deploy/compose/` deploy the gateway image only; development uses the local Keycloak setup script, while production uses a real OIDC provider. Tags and CI/CD settings are owned by [docs/configuration.md](configuration.md) and the release process docs.
