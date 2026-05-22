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
        Approvals["InfraGate.Approvals (PostgreSQL)<br/>approvals.plan_envelopes, approval_challenges,<br/>approval_grants, audit_events"]
        Persistence["InfraGate.Approvals.Postgres<br/>Npgsql + Dapper"]
        Approvals --> Persistence
    end

    subgraph K8sAdapter["Kubernetes Adapter"]
        Adapter["InfraGate.KubernetesAdapter<br/>mutation intent, evidence, intent canonicalization"]
        Payload["KubernetesPlanPayload<br/>namespace, objects, diff, dry-run, policy findings"]
        Adapter --> Payload
    end

    subgraph ServerRuntime["Kubernetes MCP server"]
        Server["InfraGate.McpServer<br/>private stdio subprocess"]
        Tools["KubernetesTools<br/>typed MCP tool surface"]
        Manager["KubernetesManager<br/>namespace validation, observability, evidence, raw apply"]
        Parser["KubernetesManifestParser<br/>Deployment / Service / ConfigMap allow-list"]
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
    Downstream -->|"stdio, user JWT terminated"| Server
    Guardrails --> GuardAudit
    Gateway -->|"plan builder + executor seams"| Adapter
    Adapter -->|"calls evidence/raw mutation tools"| Downstream
    Gateway -->|"approval workflow interfaces"| Approvals
    Manager -->|"KubernetesClient"| RBAC
```

In source mode, the gateway launches the server project as a private stdio subprocess. In container mode, the `mcp-gateway` image contains the published server assembly and starts it through `dotnet /app/server/InfraGate.McpServer.dll`. Keycloak is the local/test identity provider. Production deployments use an external OIDC issuer.

The Generic Approval Core (`InfraGate.Approvals`) owns plan envelopes, approval challenges, challenge outcomes, approval grants, the audit spine, and generic pre-execution gate orchestration independent of any target system. The Kubernetes Adapter (`InfraGate.KubernetesAdapter`) owns Kubernetes mutation intents, evidence artifacts (diffs, dry-run results, policy findings), intent-digest canonicalization, and adapter-owned freshness/domain policy checks. The gateway composes the adapter through generic seams, persists generic envelopes in the approval store, and calls the private stdio server only for Kubernetes evidence and raw mutation tools.

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
    GW->>Svr: forward tool call<br/>(StdioClientTransport)
    Note over GW,Svr: DownstreamMcpClient.GetClientAsync<br/>OAuth JWT terminated at gateway<br/>downstream service token via separate client-credentials when configured

    Note over Svr: KubernetesTools tool handlers
    Svr->>Svr: validate namespace in allowed list
    Svr->>K8s: KubernetesClient GET/LIST<br/>(namespace-scoped, bounded)
    K8s-->>Svr: Kubernetes API response<br/>(Deployments, Services, ConfigMaps, Pods, ReplicaSets)
    Note over Svr: KubernetesManager observability<br/>no Secret values, ConfigMap data, raw manifests

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
    participant Adapter as Kubernetes Adapter
    participant Svr as MCP Server (stdio subprocess)
    participant Store as ApprovalStore
    participant K8s as Kubernetes API

    Note over User,K8s: Step 1: request_* creates a pending plan

    User->>Client: request a change, for example scale a Deployment
    Client->>GW: POST /mcp -> request_scale_deployment<br/>(namespace, name, replicas)<br/>JSON-RPC + JWT Bearer

    GW->>GW: validate JWT + scope
    GW->>GW: GuardedToolRunner scans args
    GW->>Adapter: build Kubernetes Plan Envelope
    Adapter->>Svr: call evidence tools<br/>(StdioClientTransport, no token)

    Note over Svr: Evidence collection
    Svr->>Svr: validate namespace, name, replicas, or manifest kind
    Svr->>Svr: KubernetesManifestParser allows Deployment / Service / ConfigMap
    Svr->>K8s: dry-run against K8s API<br/>(dryRun=All, strict field validation)
    Svr-->>Adapter: dry-run, diff, and policy evidence
    Adapter-->>GW: Plan Envelope + target namespace
    GW->>Store: write pending plan with evidence summaries<br/>+ Intent/Review Digest binding
    Store-->>GW: PlanId + pending path + Plan Envelope

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

    Note over User,Store: Step 2: first execute_approved_plan call creates an out-of-band challenge

    User->>Client: apply approved plan
    Client->>GW: POST /mcp -> execute_approved_plan(planId)<br/>JSON-RPC + JWT Bearer
    GW->>GW: validate JWT + scope
    GW->>Store: read pending plan + current hash
    GW->>GW: create approval challenge<br/>bound to planId + Intent Digest + Review Digest + requester subject + expiry
    GW->>Store: write challenge file<br/>+ challenge.created audit event
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

    Note over User,K8s: Step 3: retry execute_approved_plan after browser approval

    User->>Client: retry apply approved plan
    Client->>GW: POST /mcp -> execute_approved_plan(planId)<br/>JSON-RPC + JWT Bearer
    GW->>GW: validate JWT + scope
    GW->>Store: validate Approval Grant for planId + subject
    alt gateway grant and subject checks pass
        GW->>Store: read pending plan + Approval Grant
        GW->>Store: validate grant expiry + Intent/Review Digests
        GW->>Svr: repeat dry-run through adapter<br/>(dryRun=All)
        GW->>Svr: call raw mutation tool<br/>(server-side apply / patch / delete)
        Svr->>K8s: apply mutation
        K8s-->>Svr: Kubernetes API response
        GW->>Store: write applied plan<br/>+ execution.succeeded audit event
        Svr-->>GW: apply result + current status
    else grant, subject, or digest validation fails
        GW->>Store: write execution.blocked audit event
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

    Plan["request_* plan"] --> PlanAudit["approvals.audit_events<br/>plan.created"]
    Challenge["approval challenge"] --> ChallengeAudit["approvals.audit_events<br/>challenge.*"]
    Approve["approve / deny / expire / reject"] --> ChallengeAudit
    Apply["execute_approved_plan"] --> ApplyAudit["approvals.audit_events<br/>execution.succeeded / execution.blocked / execution.failed"]
```

Guardrail audit and approval audit are separate. Guardrail audit records model-visible prompt-injection findings and response redaction actions to `.mcp-guardrails/audit.jsonl`. Approval audit records plan, challenge, approval, denial, expiry, hash mismatch, and apply events in the `approvals.audit_events` PostgreSQL table.

## Image And Registry Layout

| Runtime image | GHCR | Docker Hub | Contains |
| --- | --- | --- | --- |
| Gateway | `ghcr.io/mirusser/kubernetes-mcp-guard-gateway:<tag>` | `mirusser/kubernetes-mcp-guard-gateway:<tag>` | `InfraGate.McpGateway`, `InfraGate.McpGateway.Auth`, `InfraGate.Approvals`, and published downstream server assembly at `/app/server/InfraGate.McpServer.dll` |

The local/demo Compose path builds or pulls the gateway image and starts Keycloak from `quay.io/keycloak/keycloak:26.6.1`. The deployment Compose files under `deploy/compose/` deploy the gateway image only; development uses the local Keycloak setup script, while production uses a real OIDC provider. Tags and CI/CD settings are owned by [docs/configuration.md](configuration.md) and the release process docs.
