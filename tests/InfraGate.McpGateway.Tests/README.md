# InfraGate.McpGateway.Tests

`InfraGate.McpGateway.Tests` covers the HTTP MCP gateway, authentication wiring, guardrail scanning, response sanitization, audit logging, and guarded downstream tool calls.

## What It Covers

- `GatewayAuthenticationTests.cs`: OAuth discovery challenges, protected-resource metadata, static bearer auth, valid JWTs, invalid JWTs, malformed JWTs, and missing-scope step-up `WWW-Authenticate` challenges.
- `PromptInjectionGuardTests.cs`: inbound argument scanning for clean Kubernetes text, injected ConfigMap data, risky metadata values, and allowed ordinary Kubernetes strings.
- `ResponseSanitizationTests.cs`: manifest-block redaction, suspicious JSON value redaction, suspicious text line redaction, and clean text passthrough.
- `GuardedToolRunnerTests.cs`: downstream forwarding, request warnings, response redaction, and audit identity capture for OAuth and static bearer users.
- `GuardrailAuditStoreTests.cs`: JSONL audit output without credential leakage.

## Running Tests

- Gateway suite: `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
- Full solution: `dotnet test InfraGate.slnx`

Most tests run against in-memory or fake dependencies rather than a live downstream MCP server.
