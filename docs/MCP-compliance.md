# MCP Protocol Compliance & Architecture Flow

This document details the compliance of the **InfraGate** project against the official Model Context Protocol (MCP) specifications (as of `2025-11-25`), specifically focusing on the HTTP Gateway + OAuth DevIssuer path.

## Architecture & Request Flow

The following diagram illustrates the OAuth login path and representative tool calls, emphasizing how authentication, guardrails, approval, and transport layers interact.

```mermaid
sequenceDiagram
    participant Client as MCP Client
    participant Issuer as DevIssuer (Auth Server)
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
        Client->>Issuer: POST /register (loopback redirect URI)
        Issuer-->>Client: client_id
    end

    Client->>Issuer: GET /authorize (PKCE + resource + scope)
    Issuer-->>Client: Redirect to loopback callback with code + state
    Client->>Issuer: POST /token (code + redirect_uri + client_id + code_verifier + optional resource)
    Issuer-->>Client: JWT access token (audience/resource bounded)

    %% Transport and Execution Phase
    Client->>Gateway: POST /mcp (JSON-RPC + JWT Bearer)
    Note over Gateway: JWT issuer/audience/lifetime/signature/scope validation
    Gateway->>Gateway: GuardedToolRunner scans request arguments
    Gateway->>Downstream: Start or reuse stdio McpServer (no token passthrough)
    Note over Downstream: K8sTools tool handlers

    alt Read/status or approved apply tool
        Downstream->>K8s: KubernetesClient API request
        K8s-->>Downstream: Kubernetes API response
    else request_* planning tool
        Downstream->>Downstream: Write pending approval plan + audit entry
    end

    opt apply_approved_plan requires approval
        Gateway->>Gateway: Read pending plan + hash
        Gateway-->>Client: Approval URL
        Client-->>Gateway: Browser opens /approvals/{challengeId}
        Gateway->>Gateway: OAuth cookie auth + same-subject check
        Gateway->>Gateway: Recompute pending-plan hash
        Gateway->>Gateway: Write approved hash if unchanged
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

### B. Resource Parameter Binding (RFC 8707)
* **Methods:** `DevIssuerApplication.Authorize`, `DevIssuerApplication.TokenAsync`
* **Implementation:** The DevIssuer strictly validates the `resource` parameter during authorization. The authorization code is bound to that resource, so token exchange may omit `resource` for client compatibility, but an explicitly wrong token-exchange resource is rejected. Tokens are issued with a specific `Audience` (`aud`) claim bounded to that exact resource URI, mitigating the Confused Deputy problem.

### C. Authorization Code Protection (PKCE)
* **Methods:** `DevIssuerApplication.TokenAsync` (specifically `PkceMatches`)
* **Implementation:** `DevIssuer` rejects fallback methods and strictly enforces `code_challenge_method=S256`. The `code_verifier` is validated against the stored challenge before token issuance.

### D. Token Passthrough Prevention
* **Methods:** `DownstreamMcpClient.GetClientAsync`
* **Implementation:** The spec warns against forwarding access tokens to downstream services. The `McpGateway` acts as a true structural firewall: it fully terminates the OAuth JWT and initiates a `StdioClientTransport` to the `InfraGate.McpServer`. No tokens or network contexts are passed to the downstream worker, structurally eliminating Token Passthrough vulnerabilities.

### E. Open Redirection & Localhost Risks
* **Methods:** `DevIssuerApplication.RegisterClientAsync`, `DevIssuerStore.ClientAllowsRedirectUri`
* **Implementation:** Validates that dynamic client registrations use loopback `http` redirect URIs (`IsLoopbackHttpUri`) to prevent attackers from phishing authorization codes to external domains.

---

## Security Consideration: Development vs. Production
As per the MCP spec: *"All authorization server endpoints MUST be served over HTTPS."* 
Because the `DevIssuer` is purely a localhost test harness, it runs on HTTP and disables the `RequireHttpsMetadata` flag on the Gateway. This is a deliberate development exception managed by the `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA` environment variable, which MUST be true in production scenarios.
