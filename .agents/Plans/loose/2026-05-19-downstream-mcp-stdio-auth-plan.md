# Implementation Plan: Downstream MCP Stdio Auth

## Overview

Add authentication between `InfraGate.McpGateway` and the private stdio `InfraGate.McpServer` without exposing the downstream server over HTTP. The gateway will authenticate as a **Gateway Service Identity** using OAuth/OIDC client credentials, pass a short-lived downstream access token per MCP request through private `_meta`, and the server will validate that token before exposing discovery or execution behavior.

This implements ADR 0008. The standards-aligned parts are token issuance and JWT validation. The token presentation over stdio `_meta` is intentionally InfraGate-private because the official MCP authorization model targets HTTP protected resources.

The downstream token is not the primary defense. It is a cheap defense-in-depth layer that improves auditability, catches miswired clients, avoids silent unauthenticated drift, and keeps the design closer to a future standardized MCP auth shape. The primary security boundary is the combination of trusted server launch, immutable runtime packaging, sandboxed child-process permissions, out-of-band approval for destructive actions, and per-action authorization checks against trusted requester identity.

## Architecture Decisions

- Keep the downstream MCP server on stdio; do not expose it as HTTP for this work.
- Reuse the existing Keycloak local/demo realm by adding a confidential service-account client for the gateway service identity.
- The downstream token represents only the gateway service, never a requester or approver.
- Treat the downstream token as defense-in-depth, audit signal, and forward-compatibility, not as the main authorization control.
- Pass the service token per downstream MCP request through a private `_meta` key, not through environment variables.
- Require downstream auth before tool discovery and tool execution.
- Secure the legacy `initialize` handshake when the SDK permits it; otherwise add an explicit pre-MCP stdio launch-auth gate before the server processes MCP frames.
- Production fails closed when downstream auth is disabled or incomplete.
- Direct unauthenticated stdio remains possible only through an explicit development/test opt-out.
- Tokens are bearer tokens: not encrypted by OAuth itself. Secrecy relies on process/container isolation, trusted server launch, short lifetime, strict validation, and redaction.
- The server binary must be launched from a verified production artifact, preferably an immutable image layer, without shell indirection.
- The child process must run under the narrowest practical filesystem, process, network, and Kubernetes permissions.
- Destructive actions remain protected by out-of-band approval and pre-execution authorization checks; downstream service auth does not replace those controls.
- Future hardening options remain out of scope for the first cut: HTTP protected-resource migration, mTLS/DPoP sender-constrained tokens, stdio request signing, and challenge-response.

## Security Priority

The planned controls are ordered by importance:

1. **Trusted launch**: the gateway starts the intended downstream binary from a verified, immutable runtime artifact.
2. **Containment**: the downstream process can touch only the filesystem paths, network endpoints, Kubernetes namespaces, and credentials it needs.
3. **Human approval**: destructive actions require the existing out-of-band approval flow, not an MCP-client-provided approval signal.
4. **Per-action authorization**: request and execution checks use trusted gateway identity context for the requester and approval model.
5. **Downstream service token**: the gateway proves service identity to the downstream server for audit, defense-in-depth, and forward compatibility.

The token is intentionally last in that list. It is still worth implementing, but a stolen bearer token must not be enough to bypass trusted launch, sandboxing, approval, or action-level authorization.

## Public Interfaces

### Configuration

Add a shared `InfraGate:DownstreamAuth` configuration section:

```json
{
  "InfraGate": {
    "DownstreamAuth": {
      "Required": true,
      "Authority": "http://keycloak:8080/realms/infra-gate",
      "MetadataAddress": "http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration",
      "RequireHttpsMetadata": false,
      "Audience": "urn:infra-gate:mcp-server",
      "Scope": "mcp:downstream",
      "GatewayClientId": "infra-gate-gateway-service",
      "GatewayClientSecret": "<dev/demo secret only>"
    }
  }
}
```

Environment mappings should use constants, with names shaped like:

- `INFRA_GATE_DOWNSTREAM_AUTH_REQUIRED`
- `INFRA_GATE_DOWNSTREAM_AUTH_AUTHORITY`
- `INFRA_GATE_DOWNSTREAM_AUTH_METADATA_ADDRESS`
- `INFRA_GATE_DOWNSTREAM_AUTH_REQUIRE_HTTPS_METADATA`
- `INFRA_GATE_DOWNSTREAM_AUTH_AUDIENCE`
- `INFRA_GATE_DOWNSTREAM_AUTH_SCOPE`
- `INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_ID`
- `INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET`

`GatewayClientSecret` is gateway-only. The server subprocess must not receive it through inherited environment variables.

### Timing Constants

Use fixed internal timing constants for the first implementation:

- Gateway token refresh skew: **60 seconds before `exp`**.
- Server JWT validation clock skew: **30 seconds**.

These values are intentionally small because gateway and server run in the same deployment shape and the downstream token is short-lived. If production telemetry later shows real clock drift, make the values configurable with conservative production validation.

### MCP Metadata

Use one private `_meta` key for the bearer token. The exact key should be defined once in code, for example:

```text
io.infragate.downstream.authorization
```

The value should be an authorization credential string, for example `Bearer <access_token>`, so the format remains familiar even though the transport is stdio.

### Keycloak

Add a new confidential client:

- client id: `infra-gate-gateway-service`
- service accounts enabled: `true`
- standard/direct/public browser flows disabled
- client secret configured for local/demo only
- assigned downstream scope `mcp:downstream`
- token audience mapped to `urn:infra-gate:mcp-server`

## Dependency Graph

```text
ADR/glossary decision
    |
    v
Primary boundary controls
    |
    v
Downstream auth contract and config
    |
    +--> Keycloak realm/client credentials setup
    |
    +--> Server token validation filters
    |
    +--> Gateway token acquisition/cache
             |
             v
        Gateway _meta forwarding and retry
             |
             v
        Integration tests, run profiles, docs
```

## Task List

### Phase 1: Contract and Configuration

## Task 0: Resolve Initialize Auth Strategy

**Description:** Determine how to secure the startup handshake before implementing the rest of the auth boundary. Preferred path: attach the same private auth `_meta` to `initialize` and validate it before returning `InitializeResult`. Fallback path: add an InfraGate-private pre-MCP stdio launch-auth gate where the gateway writes a short-lived service credential before the server starts processing MCP JSON-RPC frames. Do not use `ClientInfo`, command-line arguments, or environment variables as credentials.

**Acceptance criteria:**
- [ ] Verify whether ModelContextProtocol 1.3.0 can attach `_meta` to `initialize` and expose server-side validation before `InitializeResult`.
- [ ] If SDK support exists, the selected design authenticates `initialize`, `listTools`, and `callTool` through the same private `_meta` convention.
- [ ] If SDK support does not exist, the selected design authenticates process startup through a pre-MCP stdio launch-auth gate and still authenticates `listTools`/`callTool` per request.
- [ ] The chosen path is documented in this plan before implementation begins.

**Verification:**
- [ ] A spike test or minimal harness proves the selected path rejects unauthenticated startup before tool metadata is exposed.
- [ ] `git diff --check`

**Dependencies:** None

**Files likely touched:**
- `.agents/Plans/loose/2026-05-19-downstream-mcp-stdio-auth-plan.md`
- minimal spike test files if the SDK behavior is not obvious from public APIs

**Estimated scope:** Small

## Task 1: Add Downstream Auth Contract

**Description:** Create a small shared downstream-auth contract used by both gateway and server. It should define configuration settings, environment/configuration key constants, the private `_meta` key, default scope/audience names, and validation helpers for required configuration. Keep this independent from Kubernetes and ASP.NET.

**Acceptance criteria:**
- [ ] One shared place defines downstream auth configuration names and the private `_meta` key.
- [ ] Options distinguish common validation settings from gateway-only client secret usage.
- [ ] Production validation refuses missing authority/metadata, audience, scope, and gateway client identity when auth is required.

**Verification:**
- [ ] Unit tests cover required, disabled, and incomplete configuration states.
- [ ] `dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj` or the chosen shared-test project passes.

**Dependencies:** Task 0

**Files likely touched:**
- `src/InfraGate.DownstreamAuth/InfraGate.DownstreamAuth.csproj`
- `src/InfraGate.DownstreamAuth/DownstreamAuthConventions.cs`
- `src/InfraGate.DownstreamAuth/DownstreamAuthOptions.cs`
- `InfraGate.slnx`
- matching test project or existing runtime-safety tests

**Estimated scope:** Medium

## Task 2: Wire Downstream Auth Config Into Gateway and Server Startup

**Description:** Bind `InfraGate:DownstreamAuth` in both runtime processes and enforce the agreed fail-closed behavior. Gateway and server should both reject production startup if downstream auth is disabled or incomplete. Development/test can disable downstream auth only through an explicit opt-out.

**Acceptance criteria:**
- [ ] `InfraGate.McpGateway` and `InfraGate.McpServer` both bind downstream auth from generated appsettings and env overrides.
- [ ] Production refuses `Required=false`.
- [ ] Development/test direct stdio requires explicit `Required=false`; missing config is not silently treated as disabled.
- [ ] Existing gateway/server configuration behavior remains compatible.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter McpGatewayOptions`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter K8SMcpOptions`
- [ ] `dotnet build InfraGate.slnx`

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpGateway/McpGatewayOptions.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `src/InfraGate.McpServer/K8SMcpOptions.cs`
- `src/InfraGate.McpServer/Program.cs`
- gateway/server option tests

**Estimated scope:** Medium

## Task 3: Harden Downstream Process Environment Passing

**Description:** Stop treating inherited environment as a safe transport for all values. The gateway currently copies every environment variable into the server subprocess. Replace that with an explicit pass-through policy or at minimum a denylist for gateway-only auth secrets, so service tokens and client secrets never reach the server through environment variables. Also make trusted launch explicit: production should start a configured downstream artifact directly, without shell indirection or `dotnet run --project`.

**Acceptance criteria:**
- [ ] `INFRA_GATE_DOWNSTREAM_AUTH_GATEWAY_CLIENT_SECRET` is not passed to the server subprocess.
- [ ] No access token is ever added to `StdioClientTransportOptions.EnvironmentVariables`.
- [ ] Required server runtime values still pass through, including `INFRA_GATE_CONFIG_PATH`, runtime mode, Kubernetes config, and approval root.
- [ ] Production uses a configured downstream assembly path from the built runtime artifact rather than `dotnet run --project`.
- [ ] The downstream process is launched directly, not through a shell wrapper.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter DownstreamMcpClient`
- [ ] A unit test proves secret env vars are excluded from `CreateTransportOptions()`.

**Dependencies:** Task 1

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamMcpClient.cs`
- `src/InfraGate.McpGateway/McpGatewayConventions.cs`
- `src/InfraGate.McpGateway/McpGatewayOptions.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamMcpClientTests.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/McpGatewayOptionsTests.cs`

**Estimated scope:** Medium

## Task 3A: Verify Primary Boundary Controls Are Enforced Or Documented

**Description:** Make the non-token controls explicit before treating downstream auth as complete. Verify that the production run shape launches the expected server artifact from an immutable image, restricts what the child process can touch, preserves out-of-band approval for destructive actions, and keeps per-action authorization checks in the gateway/pre-execution path.

**Acceptance criteria:**
- [ ] Production image/run configuration starts the downstream server from a known built artifact included in the image.
- [ ] The downstream artifact path resolves inside an immutable container image layer or an equivalently read-only, verified deployment artifact; a mutable mounted path is not accepted for production.
- [ ] Server process does not receive gateway-only secrets or broader filesystem mounts than it needs.
- [ ] Kubernetes permissions and allowed namespaces remain scoped independently from downstream service-token validation.
- [ ] Destructive downstream tools remain reachable only through the gateway's approval-bound execution path.
- [ ] Per-action authorization and pre-execution gates are documented as primary controls; service-token validation is documented as defense-in-depth.

**Verification:**
- [ ] `docker build` or existing image-build verification shows the downstream artifact is included in the gateway image.
- [ ] Compose/rendered run config review confirms mounts, env vars, and Kubernetes credentials are minimal for the selected profile.
- [ ] Gateway tests for approval-bound execution and authorization checks still pass.
- [ ] `git diff --check`

**Dependencies:** Tasks 1 and 3

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamMcpClient.cs`
- `deploy/local-oauth/*`
- `deploy/run-profiles.yaml`
- `README.md` or `docs/devs-readme.md`
- existing approval/authorization tests if assertions need tightening

**Estimated scope:** Medium

### Checkpoint: Contract

- [ ] Both processes understand downstream auth configuration.
- [ ] Production cannot accidentally start unauthenticated.
- [ ] Gateway-only secrets are not inherited by the server subprocess.
- [ ] The plan still treats trusted launch, sandboxing, approval, and per-action authorization as primary controls.
- [ ] Existing non-Keycloak unit tests still pass for changed projects.

### Phase 2: Keycloak and Token Issuance

## Task 4: Add Keycloak Gateway Service Client

**Description:** Extend the local/demo Keycloak realm with a confidential service-account client for gateway-to-server auth and a downstream audience/scope mapping. Keep the existing requester/approver clients unchanged.

**Acceptance criteria:**
- [ ] Realm import contains `infra-gate-gateway-service` as a confidential service-account client.
- [ ] The client receives `mcp:downstream` scope and downstream audience claim.
- [ ] Existing `mcp:tools` gateway audience mapping remains unchanged.
- [ ] TestData realm copy stays in sync with deploy realm.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter KeycloakRealmFileTests`
- [ ] Opt-in Keycloak integration test can acquire a client-credentials token with the downstream audience and scope.

**Dependencies:** Task 1

**Files likely touched:**
- `deploy/keycloak/infra-gate-realm.json`
- `tests/TestData/keycloak/infra-gate-realm.json`
- `tests/InfraGate.McpGateway.KeycloakTests/UnitTests/KeycloakRealmFileTests.cs`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

**Estimated scope:** Medium

## Task 5: Implement Gateway Service Token Provider

**Description:** Add a gateway-side client-credentials token provider. It should use OIDC metadata/token endpoint discovery from the configured authority or metadata address, request the downstream scope, cache the token in memory, refresh 60 seconds before expiry, and support one forced refresh after a downstream auth rejection. Cache misses and refreshes must be single-flight so a burst of concurrent downstream requests results in one token request while other callers wait for the same result.

**Acceptance criteria:**
- [ ] Token request uses `grant_type=client_credentials`, configured gateway client id/secret, and `mcp:downstream` scope.
- [ ] Token is cached in memory and refreshed 60 seconds before expiry.
- [ ] Empty-cache acquisition is single-flight: concurrent callers share one in-progress token request.
- [ ] Refresh-before-expiry is single-flight: concurrent callers share one in-progress refresh request.
- [ ] Forced refresh bypasses the cache for the one-retry path.
- [ ] Token value is never logged.

**Verification:**
- [ ] Unit tests cover cache hit, refresh-before-expiry at 60 seconds, forced refresh, token endpoint failure, missing `access_token`, and concurrent empty-cache calls.
- [ ] Unit tests prove concurrent cache misses call the token endpoint exactly once and return the same acquired token to waiting callers.
- [ ] Keycloak integration test proves the provider obtains a real service token.

**Dependencies:** Tasks 1 and 4

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamAuth/IDownstreamServiceTokenProvider.cs`
- `src/InfraGate.McpGateway/DownstreamAuth/ClientCredentialsDownstreamServiceTokenProvider.cs`
- `src/InfraGate.McpGateway/Program.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamAuth/*`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`

**Estimated scope:** Medium

### Checkpoint: Token Issuance

- [ ] Keycloak issues a gateway service token through client credentials.
- [ ] Gateway can cache and refresh that token without leaking it.
- [ ] Existing requester/approver OAuth tests still pass.

### Phase 3: Gateway Forwarding

## Task 6: Attach Service Token To Downstream MCP Requests

**Description:** Update `DownstreamMcpClient` to attach the service token to downstream MCP requests through `RequestOptions.Meta`, request params `_meta`, or the initialize-auth strategy selected in Task 0. This must use SDK-supported `_meta` when available and must not use environment variables, command-line arguments, or `ClientInfo` as credentials.

**Acceptance criteria:**
- [ ] Startup is authenticated by the Task 0 strategy before tool metadata is exposed.
- [ ] `ListToolsAsync` sends the private `_meta` auth key.
- [ ] `CallToolAsync` sends the private `_meta` auth key.
- [ ] The token key/value are not included in logged argument keys or downstream error logs.
- [ ] Disabled dev/test auth mode does not add auth `_meta`.

**Verification:**
- [ ] Unit or integration tests prove `listTools` and `callTool` receive `_meta` with the auth key.
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter DownstreamMcpClient`

**Dependencies:** Task 5

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamMcpClient.cs`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamMcpClientTests.cs`
- possible small test-only MCP server fixture

**Estimated scope:** Medium

## Task 7: Add One-Retry Behavior For Downstream Auth Rejection

**Description:** Teach the gateway to recognize the server's downstream-auth rejection, force-refresh the service token, and retry the same downstream operation once. Persistent failure should return a clear upstream auth error without hiding misconfiguration behind repeated retries.

**Acceptance criteria:**
- [ ] Expired/invalid downstream auth causes one forced refresh and one retry.
- [ ] Non-auth downstream failures are not retried as auth failures.
- [ ] A second auth failure returns a clear message without token content.
- [ ] Retry behavior applies to both discovery and tool execution paths.

**Verification:**
- [ ] Unit tests cover success after one retry, failure after second auth rejection, and no retry for non-auth errors.
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter DownstreamMcpClient`

**Dependencies:** Tasks 6 and 8

**Files likely touched:**
- `src/InfraGate.McpGateway/DownstreamMcpClient.cs`
- `src/InfraGate.McpGateway/DownstreamAuth/*`
- `tests/InfraGate.McpGateway.Tests/UnitTests/DownstreamMcpClientTests.cs`

**Estimated scope:** Medium

### Phase 4: Server Validation

## Task 8: Validate Downstream Tokens On Server Requests

**Description:** Add server validation that requires and validates the private downstream credential before startup returns tool metadata, tool discovery runs, or tool execution runs. Use request filters for `listTools` and `callTool`; use the Task 0 strategy for `initialize` if the SDK lacks an initialize filter. Validation should require issuer/signature, lifetime with 30 seconds of clock skew, audience, `mcp:downstream` scope, and configured gateway client identity. User/requester/approver claims must not be used for downstream authorization.

**Acceptance criteria:**
- [ ] Startup without a valid service credential is refused before exposing tool metadata.
- [ ] `listTools` without a valid service token is refused.
- [ ] `callTool` without a valid service token is refused.
- [ ] Invalid issuer, signature, lifetime, audience, scope, or gateway client identity are refused.
- [ ] Lifetime validation uses 30 seconds of clock skew; tokens outside that skew are refused.
- [ ] Successful validation records only safe auth outcome details.
- [ ] Disabled dev/test mode bypasses validation only when explicitly configured.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter DownstreamAuth`
- [ ] Tests use locally signed JWTs for positive and negative validation cases, including accepted/rejected boundaries around the 30-second clock skew.

**Dependencies:** Tasks 1 and 2

**Files likely touched:**
- `src/InfraGate.McpServer/Program.cs`
- `src/InfraGate.McpServer/DownstreamAuth/*`
- `tests/InfraGate.McpServer.Tests/UnitTests/DownstreamAuth/*`

**Estimated scope:** Medium

## Task 9: Define Stable Downstream Auth Error Semantics

**Description:** Define how the server reports downstream auth failures so the gateway can safely detect retryable auth rejection. Prefer a small internal convention over parsing arbitrary exception text. The response must never include token material.

**Acceptance criteria:**
- [ ] Server uses one stable downstream-auth failure marker or error code.
- [ ] Gateway retry detection depends only on that marker/code.
- [ ] Error messages are clear to operators but do not include the presented token.
- [ ] Tool-level safety exceptions remain distinct from auth failures.

**Verification:**
- [ ] Server unit tests cover failure marker/code.
- [ ] Gateway unit tests prove retry detection ignores unrelated `McpException` messages.

**Dependencies:** Task 8

**Files likely touched:**
- `src/InfraGate.DownstreamAuth/DownstreamAuthConventions.cs`
- `src/InfraGate.McpServer/DownstreamAuth/*`
- `src/InfraGate.McpGateway/DownstreamAuth/*`
- matching gateway/server tests

**Estimated scope:** Small

### Checkpoint: Auth Boundary

- [ ] Gateway can mint a downstream token and send it per request.
- [ ] Server blocks discovery and execution without a valid token.
- [ ] Gateway refreshes and retries once on auth rejection.
- [ ] No user/requester/approver token is forwarded downstream.
- [ ] A valid downstream service token alone cannot bypass approval-bound execution or per-action authorization.

### Phase 5: Redaction, Integration, and Docs

## Task 10: Add Secret Redaction Tests

**Description:** Add explicit tests proving the downstream token and gateway client secret do not leak through logs, audit payloads, thrown exception messages, or server/gateway result text.

**Acceptance criteria:**
- [ ] Gateway logs do not contain the service token or client secret.
- [ ] Server logs do not contain the service token.
- [ ] Audit payloads record safe outcome data only, such as authenticated client id, audience, scope, and failure reason.
- [ ] Exceptions and MCP error results do not echo token values.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter DownstreamAuth`
- [ ] `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj --filter DownstreamAuth`

**Dependencies:** Tasks 6, 8, and 9

**Files likely touched:**
- gateway downstream auth tests
- server downstream auth tests
- existing logging/audit helpers if redaction gaps are found

**Estimated scope:** Medium

## Task 11: Add End-To-End Coverage For Authenticated Stdio

**Description:** Add focused integration coverage that starts the gateway with a downstream server and proves authenticated stdio works through real MCP calls. Keep the Keycloak part opt-in if it needs containers; use locally signed JWTs for fast non-container tests where possible.

**Acceptance criteria:**
- [ ] Gateway startup succeeds with valid downstream auth config.
- [ ] Gateway tool discovery succeeds when it can acquire and send a valid service token.
- [ ] Gateway tool discovery fails clearly when server validation rejects the token.
- [ ] Direct server stdio without token is refused unless explicit dev/test opt-out is configured.

**Verification:**
- [ ] `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --filter GatewayHttpMcpIntegrationTests`
- [ ] `INFRA_GATE_RUN_KEYCLOAK_TESTS=1 dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj`

**Dependencies:** Tasks 4 through 10

**Files likely touched:**
- `tests/InfraGate.McpGateway.Tests/IntegrationTests/GatewayHttpMcpIntegrationTests.cs`
- `tests/InfraGate.McpGateway.KeycloakTests/IntegrationTests/KeycloakIntegrationTests.cs`
- test fixtures for downstream MCP server process

**Estimated scope:** Medium

## Task 12: Update Run Profiles, Compose, and Docs

**Description:** Add downstream auth settings to the generated local/demo configuration, Compose environment, and operator documentation. Keep production guidance clear that client secrets should be supplied by deployment secret management rather than committed generated JSON.

**Acceptance criteria:**
- [ ] Run profile generation emits downstream auth settings for local/demo.
- [ ] Compose starts Keycloak, gateway, and downstream server with matching downstream auth config.
- [ ] README/dev docs explain stdio service-token auth, explicit dev opt-out, and token redaction expectations.
- [ ] ADR 0008 is linked from docs that describe gateway/server auth.

**Verification:**
- [ ] `dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj`
- [ ] `docker compose --env-file deploy/generated/local-compose.env -f deploy/local-oauth/compose.yaml config`
- [ ] `git diff --check`

**Dependencies:** Tasks 1 through 11

**Files likely touched:**
- `deploy/run-profiles.yaml`
- `src/InfraGate.RunProfiles/*`
- `tests/InfraGate.RunProfiles.Tests/*`
- `deploy/local-oauth/*`
- `README.md` or `docs/devs-readme.md`

**Estimated scope:** Medium

### Checkpoint: Complete

- [ ] Fast unit tests pass for gateway, server, runtime safety, and run profiles.
- [ ] Opt-in Keycloak tests pass when Docker is available.
- [ ] Compose config renders without missing downstream auth settings.
- [ ] Token and client secret values are absent from logs/audit/error outputs in tests.
- [ ] Production launch, sandboxing, approval, and per-action authorization are documented as primary controls.
- [ ] Docs explain the standards tradeoff and local dev opt-out.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `McpClient.CreateAsync` sends `initialize` before normal call sites can attach `_meta` | Medium | Task 0 must select either authenticated initialize via SDK/custom transport support or a pre-MCP stdio launch-auth gate. Do not proceed with an unauthenticated initialize that exposes tool metadata. |
| The service token is mistaken for the primary security boundary | High | Keep trusted launch, sandboxing, out-of-band approval, and per-action authorization explicit in docs and tests. Treat the token as audit, defense-in-depth, and forward-compatibility. |
| Server child process inherits gateway-only secrets | High | Add explicit environment pass-through policy and tests excluding client secret/token values. |
| Verified launch or sandboxing is left implicit while token work proceeds | High | Complete Task 3A before end-to-end sign-off. Production must launch a known artifact directly and restrict downstream filesystem, network, and Kubernetes access. |
| Burst traffic stampedes Keycloak on empty cache or refresh | Medium | Token provider uses single-flight acquisition and refresh; tests prove concurrent callers share one token endpoint request. |
| Refresh/validation skew values drift into PR-only decisions | Medium | Plan fixes gateway refresh skew at 60 seconds and server JWT validation clock skew at 30 seconds for the first implementation. |
| Configured downstream path points at a mutable file that can be swapped after validation | High | Production accepts only paths inside immutable image layers or equivalently read-only verified artifacts. Mutable mounted downstream binaries are rejected or documented as dev-only. |
| Keycloak audience mapping differs from ideal RFC 8707 resource-indicator flow | Medium | Use Keycloak client scope audience mapper for local/demo and validate `aud` server-side. Keep issuer-neutral code so production IdPs can use their own resource/audience model. |
| Token leaks through logs or generic MCP exception wrapping | High | Add redaction tests around gateway logs, server logs, audit payloads, and thrown exception text. Keep auth failures stable and sanitized. |
| Retry detection becomes string-parsing brittle | Medium | Define a small internal downstream auth failure convention and test it. |
| New shared auth project increases solution surface | Low | Keep it limited to constants, options, validation, and safe helpers shared by gateway/server. No Kubernetes or ASP.NET dependencies. |
| Direct dev server workflows break unexpectedly | Medium | Require explicit dev/test opt-out, document it, and add tests for disabled mode behavior. |

## Open Questions

- Can the current ModelContextProtocol 1.3.0 SDK attach `_meta` to the `initialize` request without a custom transport? If not, implement a pre-MCP stdio launch-auth gate or custom transport shim before accepting any MCP frames.
- Should the private `_meta` value be exactly `Bearer <token>` or a small object like `{ "scheme": "Bearer", "token": "..." }`? Recommendation: use `Bearer <token>` for the first cut.

## Implementation Order

1. Task 0
2. Task 1
3. Task 2
4. Task 3
5. Task 3A
6. Task 4
7. Task 5
8. Task 8
9. Task 9
10. Task 6
11. Task 7
12. Task 10
13. Task 11
14. Task 12

The server validation work can start after the shared contract exists and can proceed in parallel with gateway token acquisition. Gateway forwarding should wait until both token provider and server error semantics are defined.

## Verification Commands

```bash
dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj
dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj
dotnet test tests/InfraGate.RuntimeSafety.Tests/InfraGate.RuntimeSafety.Tests.csproj
dotnet test tests/InfraGate.RunProfiles.Tests/InfraGate.RunProfiles.Tests.csproj
INFRA_GATE_RUN_KEYCLOAK_TESTS=1 dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj
dotnet build InfraGate.slnx
```

Run opt-in Keycloak tests only when Docker/Testcontainers are available.
