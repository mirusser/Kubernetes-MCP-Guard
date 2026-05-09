# Production Mode Fail-Closed Checks

## Summary
Implement strict Production mode with no bypass flags, enforced by `InfraGate.McpGateway`, `InfraGate.McpServer`, and `InfraGate.DevIssuer`. Production startup will fail before serving traffic or creating the downstream MCP client when unsafe dev defaults are present. Development and demo Compose runs stay easy by explicitly setting Development mode.

## Public Config And Behavior
- Add `INFRA_GATE_ENVIRONMENT=Development|Production`; it overrides `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`.
- Treat standard env values other than `Development` as production-like; if `INFRA_GATE_ENVIRONMENT` is set to anything except `Development` or `Production`, fail with a clear startup error.
- Add `K8S_MCP_USE_IN_CLUSTER=true` for explicit in-cluster Kubernetes auth.
- No MCP tool names, arguments, response JSON, or approval file shapes change.
- Docker runtime images default to `INFRA_GATE_ENVIRONMENT=Production`; dev/demo Compose files explicitly set `INFRA_GATE_ENVIRONMENT=Development`.

## Implementation Tasks
1. Add shared runtime-safety primitives
- Add a small `InfraGate.RuntimeSafety` project for environment parsing, URI checks, persistent store path checks, and clear `InvalidOperationException` messages.
- Validate production store paths as explicit, absolute, not temp/default dev paths, and not group/other writable on Unix.
- Update solution/project references and Docker restore/publish copy steps.

2. Gateway production validator
- Extend `McpGatewayOptions.FromEnvironment()` to carry runtime mode and whether approval/audit roots were explicit.
- In Production, fail if OAuth authority, metadata address, approval auth/token endpoints, OAuth resource, or approval base URL are HTTP, loopback, or missing where required.
- Fail if `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`.
- Require explicit persistent `K8S_MCP_APPROVAL_ROOT` and `INFRA_GATE_GUARD_AUDIT_ROOT`.
- Call validation in `Program.cs` immediately after options load.

3. MCP server Kubernetes config provider
- Replace direct `BuildDefaultConfig()` fallback in `Program.cs` with `KubernetesConfigProvider`.
- Auth selection order: explicit `KUBECONFIG`, else `K8S_MCP_USE_IN_CLUSTER=true`, else Development-only `BuildDefaultConfig()`, else throw.
- In Production, require explicit `K8S_MCP_ALLOWED_NAMESPACES` and explicit persistent `K8S_MCP_APPROVAL_ROOT`.
- Preserve current Development defaults: default namespace, default approval root, and Kubernetes client default discovery.

4. DevIssuer production refusal
- Extend `DevIssuerOptions` with runtime mode.
- In Production, always fail startup with an actionable message that DevIssuer is development-only and a real OIDC provider is required.
- Keep Development behavior unchanged for local OAuth testing and Compose demos.

5. Docs and deploy config
- Update `docs/configuration.md`, `docs/production-oidc.md`, `docs/devs-readme.md`, and setup examples with the new environment mode and `K8S_MCP_USE_IN_CLUSTER`.
- Mark production requirements: HTTPS external issuer/resource/approval base URL, explicit namespace allow-list, explicit Kubernetes auth mode, durable approval/audit paths.
- Update both Compose files to declare Development mode for demo services.

## Test Plan
- Add unit tests matching roadmap names:
  `ProductionMode_WithDevIssuer_RefusesStartup`,
  `ProductionMode_WithHttpMetadata_RefusesStartup`,
  `ProductionMode_WithDefaultKubeConfigFallback_RefusesStartup`,
  `ProductionMode_WithoutExplicitNamespaces_RefusesStartup`,
  `DevelopmentMode_AllowsLocalDefaults`.
- Add gateway tests for localhost/HTTP resource URL, missing or HTTP approval base URL, temp/default approval or guard audit roots, and valid external HTTPS production config.
- Add server tests for kubeconfig mode, in-cluster mode, both-auth-modes ambiguity, missing auth in Production, and Development default fallback.
- Add DevIssuer tests for Production refusal and Development defaults.
- Verify with:
  `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
  `dotnet test InfraGate.slnx`

## Assumptions
- “No bypasses” means no production override variables for unsafe settings.
- Binding `ASPNETCORE_URLS` to internal HTTP remains allowed because TLS may terminate at a reverse proxy; production safety is enforced on public OAuth/resource/approval URLs instead.
- Persistence checks use path and Unix permission heuristics, not a full storage backend durability proof.
