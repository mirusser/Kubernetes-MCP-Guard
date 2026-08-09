# InfraGate.McpGateway.Tests

`InfraGate.McpGateway.Tests` covers the HTTP MCP gateway, authentication wiring, guardrail scanning, response sanitization, audit logging, and guarded downstream tool calls.

## What It Covers

- `GatewayAuthenticationTests.cs`: OAuth discovery challenges, protected-resource metadata, metadata override configuration, valid JWTs, invalid JWTs, malformed JWTs, and missing-scope step-up `WWW-Authenticate` challenges.
- `PromptInjectionGuardTests.cs`: inbound argument scanning for clean Kubernetes text, injected ConfigMap data, risky metadata values, and allowed ordinary Kubernetes strings.
- `ResponseSanitizationTests.cs`: manifest-block redaction, suspicious JSON value redaction, suspicious text line redaction, and clean text passthrough.
- `GuardedToolRunnerTests.cs`: downstream forwarding, request warnings, response redaction, audit identity capture for OAuth users, requester injection, and unauthenticated refusal.
- `GuardrailAuditStoreTests.cs`: JSONL audit output without credential leakage.
- `GatewayApprovalServiceTests.cs`: approval URL creation, typed gate status, reason-code contracts, same-subject enforcement, dry-run data requirements, hash-drift rejection, denial, cancellation, and Single-Execution challenge behavior.
- `GatewayToolDispatcherTests.cs`: raw destructive tool refusal, domain-target audit storage, plan-status and wait-tool contracts, generic pre-execution gating, and blocked domain execution without applied markers.
- `Notifications/PlanStatusResourceHandlerTests.cs`: plan-status MCP resource template/read behavior, URI validation, and explicit subscribe/unsubscribe routing.
- `GatewayHttpMcpIntegrationTests.cs`: real HTTP MCP transport wiring with OAuth auth, fake-downstream forwarding, guardrail audit capture, response redaction, plan-status resource reads, downstream stdio startup smoke coverage, semantic dry-run/diff rendering, out-of-band approval forwarding, and an opt-in live gateway-to-Kubernetes flow.
- `GatewayKubernetesMcpServerIntegrationTests.cs`: opt-in real-binary coverage that generates the read-only TOML, merges primary and secondary tools through the HTTP Gateway, enforces the curated allowlist, and calls `pods_list_in_namespace` against the demo namespace with the dedicated viewer kubeconfig.
- `McpGatewayOptionsTests.cs`: downstream assembly defaults and subprocess arguments.

## Running Tests

- Gateway suite: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Live gateway integration: run `./scripts/install-kubernetes-mcp-server.sh`, then `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Keycloak integration (requires Docker): `dotnet test tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj --filter "Category=Keycloak"`
- Full solution without Keycloak: `dotnet test InfraGate.slnx --filter "Category!=Keycloak"`

Most tests run against in-memory or fake dependencies. The default suite also starts the real primary downstream stdio server for a cluster-free evidence-tool smoke test. `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1` requires a live Kubernetes demo namespace; the secondary-downstream case also requires the pinned binary installed under `.tools/bin/`. Keycloak tests live in `InfraGate.McpGateway.KeycloakTests` and require Docker for Testcontainers.
