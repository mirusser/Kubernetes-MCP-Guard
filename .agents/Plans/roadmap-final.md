# Kubernetes MCP Guard — Consolidated Roadmap

## Context

Kubernetes MCP Guard is an experimental .NET 10 MCP gateway/server for AI-safe Kubernetes operations. The repository already has a working architecture, multi-mode local setup, OAuth 2.1 / OIDC compliance, container builds for Docker Hub and GHCR, unit and integration test workflows, SonarCloud quality gates, and developer documentation.

Each epic carries a **Status** block that names what is already in place and what is left, so readers can see progress and avoid redundant work.

Use this document as the parent reference when generating per-epic implementation plans. Do not start broad architectural rewrites; each epic should turn into small, reviewable changes.

---

## Primary Goal

Prepare Kubernetes MCP Guard for a credible experimental public release by improving:

1. Repository clarity and naming consistency.
2. Published-image usability without a local build.
3. Security posture and supply-chain hygiene.
4. Release and readiness messaging.
5. CI/CD signal (tests, scanning, badges).
6. Documentation depth where it earns its keep.
7. User confidence through a concrete demo.

The project must remain visibly experimental. The bar is "an early adopter can understand, run, and evaluate it safely," not "production certified."

---

## Guiding Principles

### Preserve the safety model

Keep the current direction. Do not weaken or bypass:

- Narrow MCP tool surface (no raw shell, no `kubectl` exec passthrough).
- Kubernetes API access via the typed client.
- Namespace-scoped RBAC as the hard permission boundary.
- OAuth 2.1 / static-bearer auth at the gateway.
- Approval-gated mutations (`request_*` plans + `apply_approved_plan`).
- Hash-bound approvals so a plan cannot be modified after approval.
- Manifest validation limited to `apps/v1 Deployment`, `v1 Service`, `v1 ConfigMap`.
- JSONL audit logging.
- Guardrails as defense-in-depth — never described as the primary boundary.

### Be explicit about what is experimental

State clearly that APIs, configuration, deployment manifests, image tags, and runtime behavior may change. Make sure any reader understands that DevIssuer and `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false` are development-only.

### Prefer practical improvements

Favor small, high-leverage changes over rewrites: README polish, `LICENSE`, `SECURITY.md`, published-image quickstart, image scanning, demo, configuration reference, architecture diagram. Avoid duplicating content across docs — link instead.

---

## Documentation Ownership Model

Every new doc must have a single, named purpose. This is the contract that prevents drift.

| Doc | Purpose | Should include | Should not include |
| --- | --- | --- | --- |
| [README.md](../../README.md) | Project entry point | Project pitch, experimental status, safety-model summary, badges, published image names, quickstart links, links to deeper docs | Full configuration reference, full architecture, full OIDC setup, long security explanations |
| [docs/setup-guide.md](../../docs/setup-guide.md) | Local development and demo setup | Local build, Docker Compose dev mode, kubeconfig setup, DevIssuer usage, verification commands | Production OIDC details (link to `production-oidc.md`), full security model, full architecture |
| [docs/devs-readme.md](../../docs/devs-readme.md) | Developer runbook | Mode A/B/C runbooks, env vars per project, verification commands, file layout, troubleshooting | Long marketing copy, OIDC provider walkthroughs |
| [docs/MCP-compliance.md](../../docs/MCP-compliance.md) | MCP spec alignment | Transport, OAuth 2.1, PKCE, RFC 8707, token-passthrough notes | Setup steps, environment variables |
| `docs/security-model.md` (new) | What is actually safe and what is not | RBAC as hard boundary, JWT validation, scopes, namespace enforcement, approval flow, audit, guardrails as defense-in-depth, non-goals, production warnings | Setup commands, env-var tables, provider-specific OIDC |
| `docs/production-oidc.md` (new) | Replace DevIssuer with a real IdP | OIDC assumptions, required claims, issuer/audience, scopes, JWKS, HTTPS, example provider config (Keycloak first, Entra ID later) | DevIssuer development notes |
| `docs/configuration.md` (new) | Single source of truth for env vars | Variable, component, required, default, example, description, production guidance | Long flow explanations |
| `docs/architecture.md` (new) | System map and request flows | Component diagram, read-only flow, mutation flow, approval flow, auth flow, audit flow, image layout | Setup commands, full security model, env-var tables |
| `docs/demo-failing-deployment.md` (new) | Concrete proof of the core workflow | Broken workload setup, diagnosis, plan proposal, approval, apply, verification, cleanup | OIDC setup, contributor docs |

When tempted to add content to a doc that does not own that topic, link to the right doc instead.

---

## Phases

### Phase 1 — Public credibility and first-run experience

Goal: make the repository legitimate, understandable, and runnable.

Includes Epic 1 (README, LICENSE, SECURITY.md, release polish), Epic 2 (published-image quickstart), and the demo manifests portion of Epic 3.

### Phase 2 — Demonstrate the safety model

Goal: prove the unique value with an end-to-end scenario and harden trust.

Includes the full demo (rest of Epic 3), Epic 4 (security model), Epic 5 (production OIDC guide), and Epic 6 (image scanning + supply-chain checks).

### Phase 3 — Operator and contributor maturity

Goal: make the project easier to maintain, extend, and operate.

Includes Epic 7 (configuration reference), Epic 8 (architecture document), and Epic 9 (contributor and maintenance docs).

---

## Recommended Implementation Epics

Each epic is structured: Problem · Recommended changes · Files to add or modify · Acceptance criteria · Status.

The **Status** block records what already exists in the repo today, so the implementation effort is scoped to the remaining items only.

---

### Epic 1 — README, LICENSE, SECURITY.md, release polish

#### Problem

The README is already strong but the repo lacks an OSS license, a vulnerability reporting policy, and a release-notes template. Without these, external users do not know whether they can reuse the code or how to report a security issue, and releases lack a consistent shape.

#### Recommended changes

- Add `LICENSE` (Apache-2.0 recommended for an infra/security-adjacent project; MIT acceptable).
- Add `SECURITY.md` with supported versions, private reporting channel (GitHub Security Advisories), required information, and disclosure policy.
- Add a release-notes template at `.github/release.yml` or `docs/releasing.md`.
- Add explicit Docker Hub and GHCR image references in the README quickstart section.
- Add a short note in the README explaining the public name "Kubernetes MCP Guard" vs. the internal "InfraGate" naming visible in `.slnx`, project folders, and env-var prefixes.

#### Files to add or modify

- `LICENSE` (new)
- `SECURITY.md` (new)
- `.github/release.yml` or `docs/releasing.md` (new)
- [README.md](../../README.md) (small additions: license link, security link, Docker Hub + GHCR image lines, naming note)

#### Acceptance criteria

- Repository has a clear OSS license at the root.
- Repository has a vulnerability reporting policy linked from the README.
- Future releases can reuse the documented release-notes template.
- README links to both `LICENSE` and `SECURITY.md`.
- Docker Hub and GHCR image names are visible in the README without scrolling through Mode A/B/C.

#### Status

Already in place:

- README has badges (unit tests, integration tests, SonarCloud quality gate, SonarCloud coverage).
- README has architecture summary, safety model bullets, and a Mermaid diagram.
- Docker Hub and GHCR image *publishing* is configured in `package-docker.yml`.

Remaining:

- `LICENSE`, `SECURITY.md`, release-notes template do not exist.
- README does not yet surface the published image references in a quickstart-grade way.
- README does not yet explain the InfraGate vs. Kubernetes MCP Guard naming.

---

### Epic 2 — Published-image quickstart

#### Problem

The current `deploy/mode-c/compose.yaml` is local-build only. New users have to clone, build, and understand the source tree before trying the released images. The release artifacts exist; the on-ramp does not.

#### Recommended changes

- Add `deploy/mode-c/compose.release.yaml` (or `compose.release.yaml` at repo root) using published image tags.
- Decide which registry is primary in docs. Recommendation: document GHCR as the default and Docker Hub as the alternate, since GHCR ties into GitHub Releases naturally.
- Document required environment variables, kubeconfig assumptions, and verification commands for the released-image path.
- Cross-link from the README and `setup-guide.md` so the local-build path and released-image path are clearly distinguished.

#### Suggested image references

```yaml
services:
  devissuer:
    image: ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:VERSION
  gateway:
    image: ghcr.io/mirusser/kubernetes-mcp-guard-gateway:VERSION
```

Document the Docker Hub equivalents (`mirusser/kubernetes-mcp-guard-*:VERSION`) as alternates.

#### Files to add or modify

- `deploy/mode-c/compose.release.yaml` (new)
- [docs/setup-guide.md](../../docs/setup-guide.md) (add a "Run from published images" section)
- [README.md](../../README.md) (link to the published-image quickstart)

#### Acceptance criteria

- A user can run the project with one `docker compose -f deploy/mode-c/compose.release.yaml up` command after providing a kubeconfig.
- Local-build mode and released-image mode are both documented and clearly distinguished.
- Tag references in docs and compose files match the latest GitHub release tag.

#### Status

Already in place:

- `package-docker.yml` builds and publishes both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` to Docker Hub and `ghcr.io/<owner>/...` on `v*` tags and manual dispatch.
- `deploy/mode-c/compose.yaml` exists and works for local builds.

Remaining:

- No compose file or doc section uses published image tags yet.
- `docs/devs-readme.md` mentions the image short names but not full registry paths.

---

### Epic 3 — Demo manifests and end-to-end approval demo

#### Problem

The project's value is hard to grasp from prose alone. A scripted demo of the approval-gated workflow is the strongest evidence the safety model works. Demo manifests should land in Phase 1 so the quickstart has something to run against; the narrated walkthrough belongs in Phase 2.

#### Recommended changes

Create a deliberately broken Deployment plus a fix manifest, and a step-by-step doc that exercises read-only diagnosis, mutation proposal, approval, apply, verification, and audit inspection.

When writing the demo doc, follow the `.codex/skills/infragate-mcp-gateway` invocation contract — gateway endpoint at `http://127.0.0.1:3001/mcp`, default namespace `mcp-nginx-demo`, plan-first mutation flow with `apply_approved_plan` — so the demo does not invent its own conventions.

#### Demo flow

1. Deploy the broken workload into `mcp-nginx-demo`.
2. Inspect status, events, and pod logs with read-only tools (`get_k8s_status`, `get_k8s_events`, `get_pod_logs`, `get_*_diagnostics`).
3. Generate a mutation plan with `request_set_deployment_image` or `request_apply_manifest`.
4. Show the approval step (MCP elicitation or `scripts/approve-plan.sh`).
5. Apply with `apply_approved_plan`.
6. Verify recovery with the read-only tools.
7. Show the JSONL audit output under `.mcp-guardrails/`.

#### Files to add or modify

- `examples/failing-deployment/deployment.yaml` (new — broken image tag or bad probe)
- `examples/failing-deployment/fix.yaml` (new)
- `docs/demo-failing-deployment.md` (new — Phase 2 narrated walkthrough)
- [README.md](../../README.md) (link to the demo)

#### Acceptance criteria

- A reader can follow the demo step by step from a clean checkout.
- The demo exercises read-only and mutation flows.
- The demo reinforces the safety model — approval is visible and required.
- The demo does not require production credentials or a real OIDC provider.
- For clients without elicitation support, the demo documents the `scripts/approve-plan.sh` fallback.

#### Status

Already in place:

- 14 MCP tools are implemented and documented across [src/InfraGate.McpServer/README.md](../../src/InfraGate.McpServer/README.md) and [docs/setup-guide.md](../../docs/setup-guide.md).
- `scripts/approve-plan.sh` exists for non-elicitation approval.
- `deploy/minikube/rbac.yaml` and `scripts/create-demo-kubeconfig.sh` already provision the `mcp-nginx-demo` namespace and ServiceAccount.

Remaining:

- No `examples/` directory.
- No demo doc.

---

### Epic 4 — Security model documentation

#### Problem

Users need a single doc that names hard boundaries, defense-in-depth layers, and explicit non-goals. Today this content is spread across the README, `MCP-compliance.md`, and per-project READMEs.

#### Recommended changes

Add `docs/security-model.md` with four sections:

1. Hard boundaries: Kubernetes RBAC, JWT validation, required scopes, namespace enforcement, approval-gated mutation flow.
2. Defense-in-depth: prompt/input guardrails, output redaction, audit logging, MCP tool annotations, hash-bound approvals.
3. Non-goals: not a replacement for Kubernetes RBAC, not a production IdP, not a full policy engine, no guarantee that AI-generated actions are correct, not production-certified.
4. Development-only components: DevIssuer, `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`, local kubeconfig scripts.

Link to `docs/MCP-compliance.md` for OAuth detail rather than duplicating it.

#### Files to add or modify

- `docs/security-model.md` (new)
- [README.md](../../README.md) (link to the security model)

#### Acceptance criteria

- Security model has its own document.
- README links to it.
- Dev-only components are clearly flagged.
- Guardrails are described as defense-in-depth, not as a hard boundary.
- The doc links to `MCP-compliance.md` rather than restating OAuth content.

#### Status

Already in place:

- [docs/MCP-compliance.md](../../docs/MCP-compliance.md) covers OAuth 2.1, PKCE S256, RFC 8707, token-passthrough prevention, loopback redirect URI handling.
- `src/InfraGate.McpGateway.Auth/README.md` describes scope checks, 403 step-up challenges, and identity normalization.
- `src/InfraGate.McpServer/README.md` describes manifest validation and hash-bound approvals.

Remaining:

- No standalone `docs/security-model.md`.
- Non-goals and production warnings are not consolidated anywhere a reader can find them quickly.

---

### Epic 5 — Production OIDC guide

#### Problem

DevIssuer is correctly framed as development-only, but the path to a production identity provider is not documented. The OAuth story is incomplete without it.

#### Recommended changes

Add `docs/production-oidc.md` covering:

- OIDC assumptions and required claims.
- Issuer, audience (`INFRA_GATE_OAUTH_RESOURCE`), and scope (`INFRA_GATE_OAUTH_SCOPE`) configuration.
- JWKS endpoint expectations.
- HTTPS metadata requirement (`INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=true` in production).
- Worked example for Keycloak first (open-source, easy to reproduce locally).
- Worked example for Entra ID (or another enterprise provider) added later.

Make explicit that DevIssuer must not be used in production.

#### Files to add or modify

- `docs/production-oidc.md` (new)
- [docs/setup-guide.md](../../docs/setup-guide.md) (add a "Production identity providers" link, do not duplicate content)
- [README.md](../../README.md) (link)

#### Acceptance criteria

- At least one real OIDC provider is documented end-to-end.
- DevIssuer is explicitly marked as development-only in the new doc.
- Required claims, scopes, and audience values are documented and consistent with the gateway's env vars.
- HTTPS metadata requirement is highlighted for production.

#### Status

Already in place:

- DevIssuer is implemented and tested with PKCE S256 and resource binding.
- Gateway already supports OIDC via `INFRA_GATE_OAUTH_AUTHORITY`, `INFRA_GATE_OAUTH_METADATA_ADDRESS`, `INFRA_GATE_OAUTH_RESOURCE`, `INFRA_GATE_OAUTH_SCOPE`, `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA`.

Remaining:

- No production OIDC walkthrough exists.
- No worked example for any real provider.

---

### Epic 6 — Image scanning and supply-chain checks

#### Problem

A safety-focused project that publishes container images should scan them. There is no scanning step in any workflow today.

#### Recommended changes

- Add a Trivy (preferred) or Grype scan step after image build in `package-docker.yml` (or in a dedicated `image-scan.yml`).
- Upload SARIF results to the GitHub Security tab.
- Define an explicit ignore policy: any ignored CVE must include a documented reason and an expiry.
- Update the workflow `description:` field — it currently says only "Push images to Docker Hub" but the workflow already publishes to both Docker Hub and GHCR. Reword to reflect both registries (and image scanning, once added).
- Consider pinning third-party Actions by commit SHA for the security-sensitive workflow once scanning is in place.

#### Severity policy (initial)

- Fail on `CRITICAL` with a fix available.
- Fail on `HIGH` with a fix available, or require an explicitly recorded ignore.
- Warn on `MEDIUM` and `LOW`.
- Document base-image noise expectations so the policy stays practical.

#### Files to add or modify

- [.github/workflows/package-docker.yml](../../.github/workflows/package-docker.yml) (description text + scan step + SARIF upload)
- `.trivyignore` or a documented allowlist file (new, only if needed)
- [README.md](../../README.md) (badge for image scan once stable)

#### Acceptance criteria

- CI scans both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` images.
- Scan failures are understandable and actionable.
- Workflow description names both Docker Hub and GHCR.
- README displays a scan badge once scanning is stable.
- Any ignored CVE has a documented reason and an owner.

#### Status

Already in place:

- `package-docker.yml` already builds and pushes to **both** Docker Hub and `ghcr.io/<owner>/...` (matrix over both images, login steps for both registries, metadata action emits tags for both).
- `unit-tests.yml`, `integration-tests.yml`, `sonar.yml`, and `dotnet-build.yml` cover .NET CI quality.

Remaining:

- No image scan in any workflow; no SARIF upload.
- Workflow `description:` text still reads "Push images to Docker Hub" — wording-only drift now that GHCR is wired.
- Actions are pinned by major version, not by SHA.

---

### Epic 7 — Configuration reference

#### Problem

Environment variables are documented in three places — root README, per-project READMEs, `docs/devs-readme.md`, `docs/setup-guide.md`. A single source of truth makes it harder for them to drift.

#### Recommended changes

Add `docs/configuration.md` with one canonical table per component (McpServer, McpGateway, McpGateway.Auth, DevIssuer) plus a row per CI/CD-relevant variable. Columns: Variable, Component, Required, Default, Example, Description, Production guidance.

Other docs reference this file rather than restate variables.

#### Files to add or modify

- `docs/configuration.md` (new)
- [README.md](../../README.md), [docs/setup-guide.md](../../docs/setup-guide.md), [docs/devs-readme.md](../../docs/devs-readme.md), per-project READMEs (replace duplicated env-var detail with a link)

#### Acceptance criteria

- Configuration is documented in one place.
- Dev-only and production-dangerous settings are clearly flagged.
- Other docs link to the reference instead of repeating variables.

#### Status

Already in place:

- `INFRA_GATE_*` variables are documented in [src/InfraGate.McpGateway.Auth/README.md](../../src/InfraGate.McpGateway.Auth/README.md), [src/InfraGate.DevIssuer/README.md](../../src/InfraGate.DevIssuer/README.md), and [docs/setup-guide.md](../../docs/setup-guide.md).
- `K8S_MCP_*` variables are documented in [src/InfraGate.McpServer/README.md](../../src/InfraGate.McpServer/README.md).

Remaining:

- No single-source-of-truth file.
- Risk of drift across the three current locations grows as new variables are added.

---

### Epic 8 — Architecture document

#### Problem

The README has a Mermaid diagram, but request flows (read, mutation, approval, auth, audit) are not laid out in one place. A single architecture doc helps technical reviewers form a mental model without reading every C# file.

#### Recommended changes

Add `docs/architecture.md` containing:

1. Component diagram (Mermaid).
2. Read-only request flow.
3. Mutation request flow (`request_*` plan creation).
4. Approval flow (hash-bound, elicitation or `approve-plan.sh`).
5. Auth flow (OAuth 2.1 + static bearer, scope check, 403 step-up).
6. Audit flow (`.mcp-guardrails/audit.jsonl` and ApprovalStore audit).
7. Image and registry layout (Docker Hub vs. GHCR).

Link out to `MCP-compliance.md`, `security-model.md`, and `configuration.md` rather than restating their content.

#### Files to add or modify

- `docs/architecture.md` (new)
- [README.md](../../README.md) (link)

#### Acceptance criteria

- A reviewer can understand the architecture without reading source.
- Mutation approval flow is documented end-to-end.
- The doc cross-links rather than duplicates security or configuration content.

#### Status

Already in place:

- Root README has a high-level Mermaid diagram.
- `docs/MCP-compliance.md` includes a sequence diagram for the OAuth login flow.

Remaining:

- No consolidated architecture doc with all six flows.
- No diagram of the registry/image layout.

---

### Epic 9 — Contributor and maintenance docs

#### Problem

External contributors do not yet have a documented path. Safety-sensitive changes need explicit PR review prompts.

#### Recommended changes

Add or improve:

- `CONTRIBUTING.md` — build, test, run locally, run Docker Compose, format/lint, how to add MCP tools safely, how to update docs.
- `.github/PULL_REQUEST_TEMPLATE.md` — checklist that asks: does this add or modify MCP tools, does it affect mutation behavior, does it affect auth, does it affect RBAC assumptions, does it update docs, were tests added, was the demo tested.
- `.github/ISSUE_TEMPLATE/` — bug report and feature request templates.
- `CHANGELOG.md` — Keep-a-Changelog style.
- `docs/roadmap.md` — public-facing roadmap (a slim outward version of this consolidated plan).
- Optional `CODE_OF_CONDUCT.md`.

#### Files to add or modify

- `CONTRIBUTING.md` (new)
- `.github/PULL_REQUEST_TEMPLATE.md` (new)
- `.github/ISSUE_TEMPLATE/bug_report.md`, `.github/ISSUE_TEMPLATE/feature_request.md` (new)
- `CHANGELOG.md` (new)
- `docs/roadmap.md` (new — outward-facing)
- `CODE_OF_CONDUCT.md` (optional)

#### Acceptance criteria

- The repo feels approachable to outside contributors.
- Safety-sensitive changes are surfaced during PR review.
- Releases reference `CHANGELOG.md` entries.
- A public roadmap exists and is linked from the README.

#### Status

Already in place:

- `.agents/Plans/` contains internal planning docs (this file, `roadmap.md`, `roadmap-revision.md`, archived plans).
- AGENTS.md captures the agent collaboration norms.

Remaining:

- No `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, PR template, issue templates, or outward-facing `docs/roadmap.md`.

---

## Suggested Prioritization

### Phase 1 — Public credibility and first-run experience

1. Epic 1 — README polish, `LICENSE`, `SECURITY.md`, release-notes template, naming note.
2. Epic 2 — Published-image quickstart (`compose.release.yaml`, doc section).
3. Epic 3 (manifests only) — `examples/failing-deployment/` so the quickstart has something to run.
4. Epic 6 (description-only fix) — correct workflow `description:` to mention Docker Hub and GHCR; image scanning belongs in Phase 2.

These are high-impact and low-risk.

### Phase 2 — Security and trust

1. Epic 3 (full) — narrated demo at `docs/demo-failing-deployment.md`.
2. Epic 4 — `docs/security-model.md`.
3. Epic 5 — `docs/production-oidc.md` with Keycloak first.
4. Epic 6 (rest) — Trivy scan, SARIF upload, ignore policy, scan badge.

### Phase 3 — Operator and contributor maturity

1. Epic 7 — `docs/configuration.md`.
2. Epic 8 — `docs/architecture.md`.
3. Epic 9 — `CONTRIBUTING.md`, PR/issue templates, `CHANGELOG.md`, outward-facing roadmap, optional `CODE_OF_CONDUCT.md`.

---

## Implementation Instructions for the AI Agent

Before editing files:

1. Inspect the current repository state for the epic you are touching. Use the `.codex/skills/verify-readme-docs` discipline: code and tests are the source of truth, README claims must match, do not turn a docs check into a rewrite.
2. Read the doc-ownership table above. If your change adds content already owned by another doc, link instead of copy.
3. Preserve current working commands and contracts unless they are factually incorrect.
4. Keep the project clearly experimental. Do not remove existing safety warnings or weaken authentication, approval, namespace, RBAC, or manifest validation behavior.
5. Do not introduce raw shell execution as an MCP tool. Do not describe guardrails as a hard security boundary.

For any incidental code edits triggered by an epic (renames, helpers, constants), follow `.codex/skills/code-standards`:

- Avoid repeated magic strings; prefer named conventions in the smallest suitable shape.
- Use lower camel case for private fields, no underscore prefix.
- One meaningful top-level type per file.
- `Async` suffix and `CancellationToken` propagation on async I/O.
- Prefer `internal` over `public` unless cross-project use is intentional.

For any work that touches the gateway demo or Kubernetes invocations, use `.codex/skills/infragate-mcp-gateway` as the canonical contract: HTTP MCP at `http://127.0.0.1:3001/mcp`, demo namespace `mcp-nginx-demo`, plan-first mutation flow, MCP elicitation approval inside `apply_approved_plan`.

For each epic:

1. Create a short implementation plan referencing the relevant Acceptance criteria block.
2. List files to add or modify, including the doc-ownership rule that applies.
3. Call out any assumptions that need user confirmation.
4. Make changes in small, reviewable commits.
5. Update README links when a new doc is added.
6. Verify against the Acceptance criteria and the Status block.

---

## Definition of Done

This roadmap is considered implemented when:

- README clearly explains the project, status, images, quickstart, and safety model summary, and links to deeper docs.
- `LICENSE` and `SECURITY.md` exist at the repo root.
- Users can run the project from published Docker Hub and GHCR images via a documented compose file.
- Experimental and pre-production limitations are visible.
- A standalone security model doc exists and is linked from the README.
- A production OIDC guide exists with at least one real provider walkthrough.
- A configuration reference exists and other docs link to it instead of duplicating env-var detail.
- A consolidated architecture doc exists.
- An end-to-end demo (manifests + walkthrough) exercises read-only and approval-gated flows.
- CI runs container image scanning with a documented severity and ignore policy.
- The `package-docker.yml` workflow description names both Docker Hub and GHCR.
- `CONTRIBUTING.md`, PR template, issue templates, and `CHANGELOG.md` exist.
- The repository is ready for external experimental users to evaluate safely.
