# Architecture diagram

Three comprehensive sequence diagrams covering the complete lifecycle, with code-source annotations showing which class or method drives each step. 

See [docs/MCP-compliance.md](MCP-compliance.md) for the consolidated diagram and protocol detail.

## OAuth Login & MCP Authorization

```mermaid
---
title: OAuth Login & Authorization
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001<br/>Resource Server
    participant Issuer as DevIssuer :3011<br/>Auth Server

    Note over User,Issuer: 🔑 OAuth Login & MCP Authorization (once per session)

    rect rgb(255, 245, 245)
        Note over Client,GW: Gateway advertises auth requirements
        Client->>GW: POST /mcp (initial request, no token)
        GW-->>Client: 401 Unauthorized<br/>+ WWW-Authenticate Bearer
        alt token present but insufficient scope
            Client->>GW: POST /mcp (JWT Bearer)
            GW-->>Client: 403 Forbidden<br/>+ WWW-Authenticate error="insufficient_scope"<br/>+ resource_metadata
        end
        Note over GW: GatewayAuthentication<br/>AddGatewayAuthentication<br/>JwtBearerEvents.OnForbidden
    end

    rect rgb(240, 248, 255)
        Note over Client,Issuer: MCP resource discovery
        Client->>GW: GET /.well-known/oauth-protected-resource
        GW-->>Client: authorization server URI<br/>+ available scopes (mcp:tools)
        Note over GW: .AddMcp() hosts<br/>protected-resource metadata<br/>RFC 9728
        Client->>Issuer: GET /.well-known/openid-configuration
        Issuer-->>Client: OAuth/OIDC metadata<br/>(authorize / token / register / JWKS)
    end

    rect rgb(245, 255, 245)
        opt Dynamic client registration (first session only)
            Note over Client,Issuer: MCP DCR — loopback redirect URI only
            Client->>Issuer: POST /register (loopback redirect URI)
            Issuer-->>Client: client_id
            Note over Issuer: DevIssuerStore<br/>ClientAllowsRedirectUri<br/>IsLoopbackHttpUri
        end
    end

    rect rgb(255, 250, 240)
        Note over Client,Issuer: Authorization Code + PKCE S256
        Client->>Issuer: GET /authorize<br/>(PKCE S256, resource=..., scope=mcp:tools)
        Issuer-->>Client: redirect to loopback callback<br/>code + state
        Note over Issuer: DevIssuerApplication.Authorize<br/>resource parameter binding (RFC 8707)<br/>code ← resource-bound
        Client->>Issuer: POST /token<br/>(grant_type=authorization_code<br/>code + redirect_uri + client_id + code_verifier)
        Issuer-->>Client: JWT access token<br/>(aud = resource, scope = mcp:tools)
        Note over Issuer: DevIssuerApplication.TokenAsync<br/>PkceMatches — S256 enforced<br/>audience/resource bounded
    end
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

    Note over User,K8s: 🔎 Read-Only Tool Call (e.g. get_k8s_status)

    Client->>GW: POST /mcp → get_k8s_status (namespace)<br/>JSON-RPC + JWT Bearer

    rect rgb(240, 248, 255)
        Note over GW: JWT validation
        GW->>GW: validate issuer / audience / lifetime<br/>/ signature / scope (mcp:tools)
        Note over GW: GatewayAuthentication<br/>scope enforcement
    end

    rect rgb(255, 245, 245)
        Note over GW: Prompt-injection guardrails (5 categories)
        GW->>GW: GuardedToolRunner scans request arguments
        Note over GW: K8sGatewayTools<br/>delegates to GuardedToolRunner<br/>ignore-instructions / reveal-prompts<br/>tool-use / secret-exfiltration<br/>authority-override
    end

    rect rgb(245, 255, 245)
        Note over GW,Svr: Token passthrough prevention
        GW->>Svr: forward tool call<br/>(StdioClientTransport, no token)
        Note over GW,Svr: DownstreamMcpClient.GetClientAsync<br/>structural firewall — OAuth JWT terminated<br/>no token or network context passed
    end

    rect rgb(255, 250, 240)
        Note over Svr: K8sTools tool handlers
        Svr->>Svr: validate namespace ∈ allowed list
        Svr->>K8s: KubernetesClient GET/LIST<br/>(namespace-scoped, bounded)
        K8s-->>Svr: Kubernetes API response<br/>(Deployments, Services, ConfigMaps, Pods, ReplicaSets)
        Note over Svr: K8sManager.Status<br/>observability bounds<br/>no Secret values, ConfigMap data, raw manifests
    end

    rect rgb(255, 240, 240)
        Note over GW: Response sanitization + audit
        Svr-->>GW: tool result text
        GW->>GW: sanitize response<br/>(redact manifest blocks<br/>redact prompt-injection-risk lines)
        GW->>GW: GuardrailAuditStore.Append<br/>(if suspicious content detected)
        Note over GW: PromptInjectionGuard*<br/>operational-line allow-listing<br/>GuardrailAuditStore (JSONL)
    end

    GW-->>Client: MCP response (JSON via Streamable HTTP)
    Note over GW: Program.cs .WithHttpTransport()
```

## Approval-Gated Mutation (Plan + Apply)

```mermaid
---
title: Approval-Gated Mutation
---
sequenceDiagram
    actor User
    participant Client as MCP Client
    participant GW as Gateway :3001
    participant Svr as MCP Server (stdio subprocess)
    participant K8s as Kubernetes API

    Note over User,K8s: ✅ ① Request Plan (e.g. request_scale_deployment)

    Client->>GW: POST /mcp → request_scale_deployment<br/>(namespace, name, replicas)<br/>JSON-RPC + JWT Bearer

    rect rgb(240, 248, 255)
        Note over GW: Gateway security processing
        GW->>GW: validate JWT + scope (mcp:tools)
        GW->>GW: GuardedToolRunner scans args
        GW->>Svr: forward tool call (StdioClientTransport, no token)
    end

    rect rgb(255, 250, 240)
        Note over Svr: Plan creation (no K8s write yet)
        Svr->>Svr: K8sManifestParser — validate kind<br/>(Deployment / Service / ConfigMap only)
        Svr->>Svr: dry-run against K8s API<br/>(server-side apply, force-conflicts)
        Svr->>Svr: ApprovalStore — write pending plan<br/>+ compute SHA-256 hash
        Note over Svr: K8sManager.RequestPlan<br/>K8sManifestParser<br/>ApprovalStore (.mcp-approvals/pending/)
        Svr-->>GW: PlanId + plan summary<br/>(dry-run result, affected resources)
    end

    rect rgb(255, 240, 240)
        Note over GW: Gateway response processing
        GW->>GW: sanitize response, audit if needed
        GW-->>Client: PlanId + plan summary
    end

    Note over User,K8s: ✅ ② Apply Approved Plan

    Client->>GW: POST /mcp → apply_approved_plan(planId)<br/>JSON-RPC + JWT Bearer
    GW->>GW: validate JWT + scope, GuardedToolRunner scans args
    GW->>Svr: forward tool call (no token)

    rect rgb(245, 255, 245)
        Note over Svr: MCP elicitation approval
        Svr->>GW: elicitation request (plan details + hash)
        GW-->>Client: approval prompt<br/>"Approve this plan?"
        User-->>Client: Yes
        Client->>GW: approval response
        GW-->>Svr: elicitation result: approved
        Note over Svr: K8sManager.ApplyApprovedPlan<br/>MCP elicitation via tools framework
    end

    rect rgb(255, 245, 245)
        Note over Svr: Hash-bound approval enforcement
        Svr->>Svr: recompute SHA-256 of pending plan<br/>→ reject if hash mismatch
        Note over Svr: approval_hash_mismatch audit entry<br/>if pending plan changed after approval
    end

    rect rgb(255, 250, 240)
        Note over Svr,K8s: Apply mutation
        Svr->>K8s: apply mutation<br/>(server-side apply / patch)
        K8s-->>Svr: Kubernetes API response
        Svr->>Svr: ApprovalStore — write applied plan<br/>+ approval audit entry
        Note over Svr: .mcp-approvals/applied/<planId>.json<br/>.mcp-approvals/audit.jsonl
    end

    rect rgb(255, 240, 240)
        Note over GW: Gateway response processing
        Svr-->>GW: result text
        GW->>GW: sanitize response, GuardrailAuditStore.Append
        GW-->>Client: success
    end
```
