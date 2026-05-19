# MCP Protocol Compliance & Architecture Flow

This document details the compliance of the **InfraGate** project against the official Model Context Protocol (MCP) specifications (as of `2025-11-25`), specifically focusing on the HTTP Gateway + OAuth path. Keycloak is the supported local/test identity provider.

## Architecture & Request Flow

The following diagram illustrates the OAuth login path and representative tool calls, emphasizing how authentication, guardrails, approval, and transport layers interact.

```mermaid
sequenceDiagram
    participant Client as MCP Client
    participant Issuer as Keycloak / OIDC Issuer
    participant Gateway as McpGateway (Resource Server)
    participant Downstream as McpServer (Stdio Subprocess)
    participant K8s as Kubernetes API

    %% Discovery and Authorization Phase
    Client->>Gateway: Initial /mcp request without token or with insufficient scope
    alt Missing or invalid token
        Gateway-->>Client: 401 Unauthorized + OAuth challenge
    else Valid token missing required scope
        Gateway-->>Client: 403 Forbidden + WWW-Authenticate insufficient_scope
    end

    Client->>Gateway: GET /.well-known/oauth-protected-resource
    Gateway-->>Client: Protected resource metadata (authorization server + scopes)
    Client->>Issuer: GET /.well-known/openid-configuration
    Issuer-->>Client: OAuth/OIDC metadata (authorize/token/register/JWKS)

    opt Dynamic client registration
        Client->>Issuer: POST OIDC DCR registration (loopback redirect URI)
        Issuer-->>Client: client_id
    end

    Client->>Issuer: GET /authorize (PKCE + scope; resource where supported)
    Issuer-->>Client: Redirect to loopback callback with code + state
    Client->>Issuer: POST /token (code + redirect_uri + client_id + code_verifier)
    Issuer-->>Client: JWT access token (audience + scope)

    %% Transport and Execution Phase
    Client->>Gateway: POST /mcp (JSON-RPC + JWT Bearer)
    Note over Gateway: JWT issuer/audience/lifetime/signature/scope validation
    Gateway->>Gateway: GuardedToolRunner scans request arguments
    Gateway->>Downstream: Start or reuse stdio McpServer (no token passthrough)
    Note over Downstream: KubernetesTools tool handlers

    alt Read/status or approved apply tool
        Downstream->>K8s: KubernetesClient API request
        K8s-->>Downstream: Kubernetes API response
    else request_* planning tool
        Downstream->>Downstream: Write pending approval plan + audit entry
    end

    opt execute_approved_plan requires approval
        Gateway->>Gateway: Read pending plan + hash
        Gateway-->>Client: Approval URL
        Client-->>Gateway: Browser opens /approvals/{challengeId}
        Gateway->>Gateway: OAuth cookie auth + same-subject check
        Gateway->>Gateway: Recompute pending-plan hash
        Gateway->>Gateway: Record Challenge Outcome and issue Approval Grant if unchanged
    end

    Downstream-->>Gateway: Tool result text
    Gateway->>Gateway: Sanitize response + write guardrail audit when needed
    Gateway-->>Client: MCP response (JSON or SSE via Streamable HTTP)
```

---

## 1. Transports Specification (Streamable HTTP)
**Status: ✅ Fully Compliant**

The Gateway delegates the transport layer to the official `ModelContextProtocol.AspNetCore` SDK. 
* **Key Mechanisms:**
  * Uses `.WithHttpTransport()` in `Program.cs`.
  * Clients send `POST` requests with JSON-RPC messages to `/mcp`.
  * Streamable HTTP response handling is delegated to the official SDK, including JSON-RPC responses and Server-Sent Events (SSE) where appropriate.

---

## 2. Authorization Specification (OAuth 2.1 / OIDC)
**Status: ✅ Fully Compliant**

The MCP Authorization standard is built heavily on Draft OAuth 2.1, emphasizing Resource Indicators (RFC 8707) and Protected Resource Metadata (RFC 9728).

### A. Protected Resource Discovery & Step-Up Authorization
* **Methods:** `GatewayAuthentication.AddGatewayAuthentication`
* **Implementation:** The Gateway uses `.AddMcp(mcpOptions => ...)` to host the `/.well-known/oauth-protected-resource` metadata.
* **Step-Up Auth:** Hooks into `JwtBearerEvents.OnForbidden` to append the mandatory `WWW-Authenticate: Bearer error="insufficient_scope", resource_metadata="..."` header on 403 responses, allowing clients to dynamically negotiate missing scopes.

### B. Resource / Audience Binding
* **Gateway methods:** `GatewayAuthentication.ConfigureJwtBearerOptions`
* **Keycloak local/demo implementation:** The imported Keycloak realm emits the gateway resource URI as `aud` through the `mcp:tools` client-scope audience mapper. The gateway validates issuer, signature, lifetime, audience, and scope on every MCP request.
* **Resource indicator caveat:** Keycloak's MCP guidance currently points to Client ID Metadata Documents and notes limitations around processing RFC 8707 `resource` as MCP clients expect, so this repo keeps mapper-based audience binding for Keycloak. InfraGate should revisit issuer-side RFC 8707 resource-indicator coverage when Keycloak supports the needed MCP flow cleanly.

### C. Authorization Code Protection (PKCE)
* **Methods:** Keycloak realm clients.
* **Implementation:** Keycloak `mcp-client` and `infra-gate-approval-ui` are public authorization-code clients with PKCE S256 configured.

### D. Token Passthrough Prevention
* **Methods:** `DownstreamMcpClient.GetClientAsync`
* **Implementation:** The spec warns against forwarding access tokens to downstream services. The `McpGateway` acts as a true structural firewall: it fully terminates the OAuth JWT and initiates a `StdioClientTransport` to the `InfraGate.McpServer`. No tokens or network contexts are passed to the downstream worker, structurally eliminating Token Passthrough vulnerabilities.

### E. Open Redirection & Localhost Risks
* **Methods:** Keycloak OIDC Dynamic Client Registration policies.
* **Implementation:** The local Keycloak realm enables anonymous DCR only for local/demo use and restricts redirect URIs to trusted loopback hosts with an allowed-scope policy and max-client cap.

---

## Security Consideration: Development vs. Production
As per the MCP spec: *"All authorization server endpoints MUST be served over HTTPS."* 
The local Keycloak demo runs over HTTP and therefore sets `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`. This is a deliberate development exception; production deployments must use HTTPS issuer metadata and keep the gateway in `Production` mode.
