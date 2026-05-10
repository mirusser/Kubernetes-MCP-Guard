## Summary

- <!-- Describe the change. -->

## Verification

- [ ] `dotnet build InfraGate.slnx`
- [ ] `dotnet test InfraGate.slnx --no-build`
- [ ] Integration tests, if relevant:
  - `INFRA_GATE_RUN_INTEGRATION=1 dotnet test InfraGate.slnx --no-build`
  - `INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --no-build`
- [ ] Keycloak OIDC tests, if auth/scope/audience logic changed (requires Docker):
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj --no-build --filter "Category=Keycloak"`
- [ ] Docs-only checks, if relevant: `git diff --check`
- [ ] Not run; reason:

## Documentation

- [ ] README or relevant docs updated
- [ ] `docs/configuration.md` updated for environment variable changes
- [ ] `docs/tool-permissions.md` updated for MCP tool or RBAC changes
- [ ] `docs/security-model.md` updated for boundary or threat-model changes
- [ ] `CHANGELOG.md` updated for release-visible changes

## Safety Checklist

- [ ] This does not add or modify MCP tools.
- [ ] If MCP tools changed, tool names, arguments, annotations, README docs, and tests were updated.
- [ ] mutation behavior remains plan-first and approval-gated.
- [ ] auth and scope enforcement remain at the HTTP gateway.
- [ ] RBAC and namespace allow-list assumptions are preserved.
- [ ] Approval and hash-bound plan integrity are preserved.
- [ ] guardrails, response sanitization, and audit behavior are preserved.
- [ ] No raw shell, `kubectl` passthrough, exec, attach, port-forward, RBAC manipulation, or Secret-value read was added.
- [ ] Tests were added or updated for behavior changes.
- [ ] The failing-deployment demo was checked or is unaffected.

## Notes For Reviewers

- <!-- Add review notes, known tradeoffs, or follow-ups. -->
