# Public Roadmap

Kubernetes MCP Guard is an experimental project for AI-safe Kubernetes operations through the Model Context Protocol. The near-term goal is not production certification; it is to make the project understandable, runnable, and safe enough for early technical evaluation in disposable or tightly controlled environments.

This roadmap is intentionally high level. It avoids dates and detailed internal planning history.

## Current Focus

- Keep the gateway-first safety model intact: OAuth JWTs at the HTTP MCP gateway, namespace-scoped Kubernetes access, bounded tools, prompt-injection guardrails, audit logs, and approval-gated mutations.
- Improve the first-run experience with published images, Docker Compose examples, and a concrete failing-deployment demo.
- Keep public documentation accurate and split by ownership: setup, configuration, architecture, security model, protocol compliance, tool permissions, OIDC guidance, and release process.
- Make contribution and review paths clearer for external users.

## Available Today

- HTTP MCP gateway at `/mcp` backed by a private stdio Kubernetes MCP server.
- Development OAuth/OIDC issuer for local testing.
- Read-only Kubernetes observability tools for status, events, logs, focused resource summaries, and diagnostics.
- Plan-first mutation tools for supported Deployments, Services, and ConfigMaps, with browser approval before apply.
- Guardrail audit and approval audit JSONL streams.
- Docker images published to GHCR and Docker Hub for gateway and DevIssuer.
- Unit, integration, dependency-scan, image-scan, Docker, and SonarCloud workflows.

## Near-Term Work

- Keep contributor docs, issue templates, PR review prompts, changelog, and release notes current.
- Keep the public demo aligned with the shipped tool surface and approval workflow.
- Continue tightening release validation and published-image smoke testing.
- Improve operational guidance for running with a real OIDC provider and least-privilege Kubernetes credentials.

## Future Hardening

- Add release-smoke CI once a Kubernetes-in-CI path is available.
- Track SBOM generation, provenance, and image signing.
- Expand production OIDC guidance beyond the first documented provider.
- Revisit finer-grained OAuth scopes if the tool surface grows.
- Keep security documentation explicit about what is enforced, what is defense-in-depth, and what remains out of scope.

## Non-Goals For Now

- Production certification.
- Cluster-admin automation.
- Raw shell, `kubectl` passthrough, exec, attach, or port-forward through MCP.
- A general Kubernetes policy engine.
