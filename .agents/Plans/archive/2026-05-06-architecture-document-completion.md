# Epic 8 Architecture Document Completion

## Summary

Complete Epic 8 by updating the existing `docs/architecture.md`, not creating a new file. The doc should give reviewers a clear system map and request-flow model while linking out to the existing security, configuration, and protocol docs.

## Key Changes

- No runtime, public API, interface, schema, or type changes.
- Update `docs/architecture.md` into the consolidated architecture doc:
  - Add a component diagram covering MCP client, Gateway, Auth library, DevIssuer, downstream stdio server, ApprovalStore, guardrail audit store, Kubernetes API/RBAC, and storage boundaries.
  - Preserve the existing Mermaid sequence diagrams where useful.
  - Split the current combined mutation section into clearer flows: plan request, browser approval challenge, and approved apply.
  - Keep the existing dry-run planning wording intact because that behavior is planned soon.
  - Add a compact audit flow for `.mcp-guardrails/audit.jsonl` and `K8S_MCP_APPROVAL_ROOT/audit.jsonl`.
  - Add image/registry layout: GHCR and Docker Hub publish `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer`; the gateway image includes the downstream server assembly at `/app/server/InfraGate.McpServer.dll`.
- Make minimal README fixes:
  - Replace the broken `docs/full-architecture-diagram.md` link with `docs/architecture.md`.
  - Leave dry-run wording unchanged.

## Doc Ownership Rules

- Link to `docs/MCP-compliance.md` for OAuth/MCP protocol details.
- Link to `docs/security-model.md` and `docs/tool-permissions.md` for boundaries, threat model, RBAC, and tool permissions.
- Link to `docs/configuration.md` for environment variables and defaults.
- Do not duplicate setup commands, provider-specific OIDC walkthroughs, full env-var tables, or the full threat model inside `docs/architecture.md`.

## Test Plan

- Run `git diff --check`.
- Verify the broken architecture link is gone:
  - `rg -n 'full-architecture-diagram' README.md docs/architecture.md`
- Verify expected cross-links exist:
  - `rg -n 'MCP-compliance.md|security-model.md|configuration.md|tool-permissions.md' docs/architecture.md`
- Verify Mermaid fences are balanced:
  - `rg -n '^```mermaid|^```$' docs/architecture.md`
- No `dotnet test` run is required unless code changes are introduced.

## Assumptions

- Epic 8 remains documentation-only.
- Dry-run planning wording is intentionally retained as near-future architecture direction.
- Mermaid remains the diagram format; no generated image asset is needed.
- One small docs-only commit is enough if committing is part of implementation: `docs: complete architecture overview`.
