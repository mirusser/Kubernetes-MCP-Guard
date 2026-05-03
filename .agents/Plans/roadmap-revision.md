# Revised Roadmap for Kubernetes MCP Guard

## Core adjustment

The roadmap should prioritize not only credibility files and documentation, but also a concrete demonstration of the project's unique value: approval-gated Kubernetes operations through MCP.

The end-to-end demo should move earlier because it explains the project better than architecture prose alone.

---

# Revised Phases

## Phase 1: Public credibility and first-run experience

Goal: make the repository look legitimate, understandable, and runnable.

### Include

1. README polish
2. LICENSE
3. SECURITY.md
4. Published-image quickstart
5. Release notes template
6. Docker/GHCR image documentation
7. Workflow badge
8. Fix workflow wording from “Docker Hub” to “Docker Hub and GHCR”
9. Add initial demo manifests for a broken deployment scenario

### Notes

The full narrated demo can come in Phase 2, but the example manifests should be added in Phase 1 so the quickstart has something concrete to run against.

### Acceptance criteria

- New users can understand what the project does from the README.
- Users can run the project from published images.
- The repo has a clear license and vulnerability reporting policy.
- The release process is documented at least minimally.
- Demo manifests exist and are referenced as upcoming or experimental.

---

## Phase 2: Demonstrate the safety model

Goal: prove the core product value with an end-to-end scenario.

### Include

1. Full end-to-end demo:
   - deploy broken workload
   - inspect status/events/logs
   - diagnose issue
   - propose mutation plan
   - approve plan
   - apply approved plan
   - verify recovery
   - inspect audit output

2. Security model documentation:
   - hard boundaries
   - defense-in-depth mechanisms
   - non-goals
   - dev-only components
   - production warnings

3. Image scanning:
   - add Trivy or Grype
   - fail on CRITICAL vulnerabilities with fixes available
   - fail or require explicit ignore for HIGH vulnerabilities with fixes available
   - warn on MEDIUM/LOW
   - document ignore policy

4. Production OIDC guide:
   - explain replacing DevIssuer
   - include example configuration for at least one real provider
   - recommended candidates: Keycloak first, then Entra ID later
   - explain issuer, audience, JWKS, scopes, and HTTPS metadata requirements

### Acceptance criteria

- Users can see the approval-gated workflow in action.
- Security boundaries are documented clearly.
- DevIssuer is clearly marked as development-only.
- At least one real OIDC provider path is documented.
- Container image scanning is present in CI.

---

## Phase 3: Operator and contributor maturity

Goal: make the project easier to maintain, extend, and operate.

### Include

1. Configuration reference
2. Architecture document
3. CONTRIBUTING.md
4. PR template
5. CHANGELOG.md
6. Roadmap document
7. Optional CODE_OF_CONDUCT.md
8. Optional screenshots or demo GIF

### Important constraint

Avoid redundant documentation. Each document must have a clear ownership boundary.

---

# Documentation Ownership Model

To avoid drift and duplication, use this structure:

## README.md

Purpose: project overview and entry point.

Should include:

- what the project is
- experimental status
- safety model summary
- quickstart links
- published image names
- badges
- links to deeper docs

Should not include:

- full configuration reference
- full architecture details
- full OIDC setup
- long security explanations

---

## docs/setup-guide.md

Purpose: local development and demo setup.

Should include:

- local build instructions
- Docker Compose development mode
- local kubeconfig/dev setup
- DevIssuer usage
- verification commands

Should not include:

- production OIDC details beyond linking to `production-oidc.md`
- full security model
- complete architecture explanation

---

## docs/security-model.md

Purpose: explain what is actually safe and what is not.

Should include:

- RBAC as hard boundary
- OAuth/JWT validation
- scopes
- namespace enforcement
- approval-gated mutation flow
- audit logging
- guardrails as defense-in-depth
- non-goals
- production warnings

Should not include:

- every environment variable
- full setup commands
- provider-specific OIDC setup

---

## docs/production-oidc.md

Purpose: show how to replace DevIssuer with a real identity provider.

Should include:

- OIDC assumptions
- required claims
- issuer/audience configuration
- scopes
- JWKS metadata
- HTTPS requirement
- example provider configuration

Start with Keycloak because it is easy to reproduce locally and is open-source.

Later add Entra ID or another enterprise provider.

---

## docs/configuration.md

Purpose: single source of truth for environment variables and config.

Should include a table with:

- variable
- component
- required
- default
- example
- description
- production guidance

Should not include long explanations of flows.

---

## docs/architecture.md

Purpose: system map and request flows.

Should include:

- component diagram
- read-only request flow
- mutation request flow
- approval flow
- auth flow
- audit flow
- package/image layout

Should not duplicate:

- setup-guide commands
- security-model details
- configuration tables

This document should link to those docs instead.

---

## docs/demo-failing-deployment.md

Purpose: concrete proof of the core workflow.

Should include:

- broken workload setup
- read-only diagnosis
- mutation proposal
- approval
- apply approved plan
- verification
- cleanup

This should be one of the most important docs in the project.

---

# Revised Epic List

## Epic 1: README, LICENSE, SECURITY.md, release polish

High priority.

## Epic 2: Published-image quickstart

High priority.

## Epic 3: Demo manifests and end-to-end approval demo

Move earlier. High priority.

## Epic 4: Security model documentation

High priority.

## Epic 5: Production OIDC guide

High priority because the OAuth story is incomplete without it.

## Epic 6: Image scanning and supply-chain checks

High priority for a safety-focused project.

## Epic 7: Configuration reference

Medium priority.

## Epic 8: Architecture document

Medium priority, but must be scoped tightly to avoid duplication.

## Epic 9: Contributor and maintenance docs

Medium/lower priority.

---

# Updated Agent Instructions

Before implementing, inspect the existing docs and avoid duplicating content.

For every new document, define its purpose first.

Do not create multiple documents that explain the same thing in different words.

Use links between docs instead of copying sections.

The end-to-end demo should be treated as a core product artifact, not an optional extra.

The production OIDC guide should make clear that DevIssuer is not suitable for production.

The image scanning policy should be strict enough to support the project's safety-first identity but practical enough not to break constantly due to low-risk base-image noise.

Any ignored vulnerability must have a documented reason.

Do not weaken the existing safety model.

Do not add raw shell execution as an MCP tool.

Do not describe guardrails as a hard security boundary. Guardrails are defense-in-depth.