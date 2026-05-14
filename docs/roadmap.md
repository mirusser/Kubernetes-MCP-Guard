# Public Roadmap

Kubernetes MCP Guard is an experimental reference implementation for a possible MCP mutation-approval profile, with Kubernetes as the first concrete adapter. The near-term goal is not production certification or standardization; it is to make digest-bound, human-approved MCP mutations understandable, runnable, and safe enough for early technical evaluation in disposable or tightly controlled environments.

This roadmap is intentionally high level. It avoids dates and detailed internal planning history.

## Current Focus

- Keep the existing gateway-first Kubernetes safety model intact: OAuth JWTs at the HTTP MCP gateway, namespace-scoped Kubernetes access, bounded tools, prompt-injection guardrails, audit logs, and approval-gated mutations.
- Shape the generic approval lifecycle described in [mutation-approval-profile.md](mutation-approval-profile.md): plan envelope, opaque plan identifier, intent and review digests, approval challenges, challenge outcomes, approval grants, audit spine, plan validity, challenge TTL, freshness policy, and pre-execution gates.
- Separate generic approval-core responsibilities from Kubernetes adapter responsibilities, following [ADR 0001](adr/0001-separate-generic-approval-core-from-domain-adapters.md).
- Keep plan identity and integrity separate, following [ADR 0002](adr/0002-use-opaque-plan-identifiers-and-separate-digests.md): `planId` is a workflow handle, while intent and review digests prove what is executed and what was reviewed.
- Keep public documentation accurate and split by ownership: setup, configuration, architecture, security model, protocol compliance, tool permissions, OIDC guidance, release process, mutation-approval profile design, and mutation-approval flow diagrams.

## Available Today

- HTTP MCP gateway at `/mcp` backed by a private stdio Kubernetes MCP server.
- Keycloak-backed local OAuth path, with DevIssuer retained as a deprecated fallback for compatibility testing.
- Read-only Kubernetes observability tools for status, events, logs, focused resource summaries, and diagnostics.
- Plan-first mutation tools for supported Deployments, Services, and ConfigMaps, with browser approval before apply.
- Separate pending plans and approval challenges, with same-subject browser approval, challenge TTL, hash-bound apply, dry-run checks, drift checks, and single successful execution.
- Guardrail audit and approval audit JSONL streams.
- Docker images published to GHCR and Docker Hub for gateway and DevIssuer.
- Unit, integration, dependency-scan, image-scan, Docker, and SonarCloud workflows.
- A glossary in [CONTEXT.md](../CONTEXT.md), a profile sketch in [mutation-approval-profile.md](mutation-approval-profile.md), flow diagrams in [mutation-approval-flow.md](mutation-approval-flow.md), and initial ADRs for the new generic-core/domain-adapter boundary.

## Near-Term Work

- Refine the profile sketch into a concrete internal target for implementation without presenting it as a finished standard.
- Introduce generic plan-envelope concepts in the shared approval layer while preserving existing Kubernetes behavior.
- Split Kubernetes-specific mutation intent, evidence, freshness checks, domain policy checks, canonicalization, execution behavior, and adapter audit payloads away from generic approval lifecycle code.
- Add the two-digest model: intent digest for executable mutation binding and review digest for the trusted human review snapshot.
- Add challenge outcomes as recorded challenge results, and approval grants as the durable execution authorization consumed by execution gates.
- Add explicit plan validity windows alongside short-lived approval challenge TTLs.
- Shape the audit trail toward a generic audit spine with Kubernetes adapter payloads.
- Keep the public demo aligned with the shipped tool surface, approval workflow, and profile terminology.
- Continue tightening release validation and published-image smoke testing.
- Improve operational guidance for running with a real OIDC provider and least-privilege Kubernetes credentials.

## Future Hardening

- Explore external approval-authority integration points without requiring every MCP server to own browser approval UI and durable workflow state.
- Add approval-policy modes beyond same-subject approval, such as delegated, service-owner, or multi-party approval.
- Add explicit reusable-plan support only as an opt-in execution reuse policy after the single-execution path is mature.
- Add release-smoke CI once a Kubernetes-in-CI path is available.
- Track SBOM generation, provenance, and image signing.
- Expand production OIDC guidance beyond the first documented provider.
- Revisit finer-grained OAuth scopes if the tool surface grows.
- Keep security documentation explicit about what is enforced, what is defense-in-depth, and what remains out of scope.

## Non-Goals For Now

- Production certification.
- Claiming this is an MCP standard before the profile matures and receives external validation.
- Cluster-admin automation.
- Raw shell, `kubectl` passthrough, exec, attach, or port-forward through MCP.
- A general Kubernetes policy engine.
- A universal schema for every domain's mutation intent, dry-run, diff, drift, or idempotency semantics.
