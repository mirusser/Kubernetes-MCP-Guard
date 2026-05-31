# Public Roadmap

Kubernetes MCP Guard is an experimental reference implementation for a digest-bound, human-approved MCP mutation profile with Kubernetes as the first adapter. This roadmap is intentionally high level — no dates, no internal planning details.

## Current Focus

- Migrate `Observer` and `Planner` from raw `IChatClient` to the Microsoft Agent Framework for structured workflows, prompt templates, MCP-native tool providers, OpenTelemetry, and framework guardrails.
- Separate the generic approval lifecycle from Kubernetes adapter code (ADRs 0001, 0002): opaque plan identifiers, two-digest model (intent + review), generic audit spine.
- Keep the gateway-first safety model intact: OAuth, namespace-scoped access, bounded tools, audit logs, approval-gated mutations.

## Available Today

- HTTP MCP gateway at `/mcp` with OAuth (Keycloak), namespace-scoped K8s access, prompt-injection guardrails, and audit logging.
- Read-only K8s tools (status, events, logs, diagnostics) and plan-first mutations for Deployments, Services, ConfigMaps with browser approval before apply.
- Approval lifecycle: pending plans, challenges, hash-bound apply, dry-run + drift checks, single-execution grants.
- Images on GHCR and Docker Hub; CI (unit, integration, scan, SonarCloud).
- Docs: [CONTEXT.md](../CONTEXT.md), [mutation-approval-profile.md](mutation-approval-profile.md), [mutation-approval-flow.md](mutation-approval-flow.md), ADRs.

## Near-Term Work

- **Agent framework migration.** Refactor `Observer` and `Planner` from raw `IChatClient` background services onto the Microsoft Agent Framework: managed agent workflows, structured prompt templates, native MCP tool providers, OpenTelemetry observability, and framework-level guardrails (interceptors/filters).
- **Approval core separation.** Continue splitting generic approval lifecycle from Kubernetes adapter code (ADRs 0001, 0002): opaque plan identifiers, two-digest model (intent + review), generic audit spine, challenge TTL and grant expiry.
- Polish the mutation-approval profile sketch into an implementable target; keep demo, docs, and release validation aligned.

## Future

- External approval-authority integration, multi-party/delegated approval, reusable plans.
- Release-smoke CI, SBOM, provenance, image signing.
- More OIDC provider guidance and finer-grained OAuth scopes.

## Non-Goals

- Production certification, MCP standardization (not yet), cluster-admin automation, raw `kubectl` passthrough, exec/attach/port-forward, general K8s policy engine, universal mutation schema.
