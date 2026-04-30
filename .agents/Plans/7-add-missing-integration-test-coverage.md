# Add Missing Integration Test Coverage

## Summary

Add targeted integration coverage around the currently under-tested runtime seams: real HTTP MCP gateway transport, gateway-to-stdio downstream startup, opt-in HTTP gateway-to-Kubernetes flow, and elicitation forwarding. Keep normal tests cluster-free; only live Kubernetes coverage remains opt-in.

## Key Changes

- Add gateway HTTP MCP integration tests in `InfraGate.McpGateway.Tests`.
  - Build an in-memory ASP.NET `TestServer` that mirrors gateway startup: auth, guardrails, audit store, fake `IDownstreamMcpClient`, `AddMcpServer().WithHttpTransport()`, and `MapMcp("/mcp")`.
  - Exercise real MCP HTTP tool discovery and tool invocation through the client-facing endpoint.
  - Cover static bearer auth success/failure, `get_k8s_status` forwarding, suspicious input warning, response redaction, and guardrail audit capture.
  - Use fake downstream only; no Kubernetes and no spawned child process.

- Add gateway-to-stdio downstream smoke coverage.
  - Add a cluster-free integration test for `DownstreamMcpClient` that starts the real `InfraGate.McpServer` stdio child process.
  - Call `request_apply_manifest` with a supported ConfigMap manifest because it only creates a pending plan and does not contact Kubernetes.
  - Set temp `K8S_MCP_APPROVAL_ROOT`, `K8S_MCP_ALLOWED_NAMESPACES`, and a minimal kubeconfig path if needed by the Kubernetes client startup.
  - Assert the returned text contains `PlanId`, `Operation: apply`, and the expected object ref.

- Add opt-in live HTTP gateway integration.
  - Gate with `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1`.
  - Start the gateway against the real downstream server and existing demo kubeconfig/namespace.
  - Use static bearer auth to keep this focused on the gateway/Kubernetes path, not OAuth.
  - Through HTTP MCP, run the same essential workflow as the stdio live test: apply demo manifest, read status/resource/diagnostics, request/apply set-image, scale, restart, and delete.
  - Approve plans by writing the exact pending-plan hash, matching existing integration behavior.

- Add elicitation bridge integration.
  - Add a gateway-level test where downstream requests approval through `apply_approved_plan` and the upstream MCP client supplies an accepted elicitation response.
  - Assert that the plan becomes approved by server-side approval flow, not by pre-writing the approval hash.
  - Also cover a declined or unavailable elicitation response returning a clear refusal.
  - Keep this cluster-free by using a scale plan plus a fake/stub Kubernetes API or by stopping before Kubernetes mutation if approval refusal is the behavior under test.

- Update README files after tests are added.
  - Update `tests/InfraGate.McpGateway.Tests/README.md` to mention HTTP MCP transport integration, downstream stdio smoke coverage, and any new opt-in gateway integration command.
  - Update `tests/InfraGate.McpServer.Tests/README.md` only if the existing stdio integration wording changes.
  - Update `docs/devs-readme.md` and `docs/setup-guide.md` to document `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1` if that opt-in test is added.

## Test Plan

- New default tests:
  - HTTP MCP endpoint rejects missing/wrong auth.
  - HTTP MCP endpoint lists gateway tools.
  - HTTP MCP `get_k8s_status` forwards exact tool name and arguments to fake downstream.
  - HTTP MCP suspicious manifest input returns guardrail warning and writes request audit.
  - HTTP MCP suspicious downstream output is redacted and writes response audit.
  - `DownstreamMcpClient` can start the real stdio server and call `request_apply_manifest`.

- New opt-in tests:
  - `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - Live gateway path applies, reads, updates image, scales, restarts, and deletes demo resources via HTTP MCP.

- Regression checks:
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.DevIssuer.Tests/InfraGate.DevIssuer.Tests.csproj`
  - `dotnet test InfraGate.slnx --no-build`

## Assumptions

- Normal CI/default local tests must not require a live Kubernetes cluster.
- Live gateway-to-Kubernetes coverage should be opt-in and separate from the existing stdio live integration.
- Static bearer auth is sufficient for the live gateway integration; OAuth remains covered by existing in-memory DevIssuer/gateway auth tests.
- README updates happen in the same change as the tests so documented coverage and commands stay current.
