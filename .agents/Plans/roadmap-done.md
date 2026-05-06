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
- OAuth 2.1 JWT auth at the gateway (no static bearer token mode).
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
| `docs/security-model.md` (new) | What is actually safe and what is not | RBAC as hard boundary, JWT validation, scopes, namespace enforcement, approval flow, audit, guardrails as defense-in-depth, threat model (assumptions, what is reduced, what is out of scope), non-goals, production warnings | Setup commands, env-var tables, provider-specific OIDC |
| `docs/tool-permissions.md` (new) | Per-tool RBAC and scope matrix | Each MCP tool's type (read/plan/mutation), Kubernetes verbs and resources, OAuth scope, approval requirement, namespace boundary | Implementation details, OIDC setup, env-var tables |
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
- Add a release checklist at `docs/releasing.md` (see template below) covering test signal, image publishing, package visibility, secrets hygiene, pre-release flagging, and quickstart tag verification.
- Add explicit Docker Hub and GHCR image references in the README quickstart section.
- Add a short note in the README explaining the public name "Kubernetes MCP Guard" vs. the internal "InfraGate" naming visible in `.slnx`, project folders, and env-var prefixes.

#### Release checklist (template)

Bake this into `docs/releasing.md` so every release follows the same path:

```markdown
1. Confirm unit tests pass on `main`.
2. Confirm integration tests pass (self-hosted runner, both `INFRA_GATE_RUN_INTEGRATION` and `INFRA_GATE_RUN_GATEWAY_INTEGRATION`).
3. Confirm `package-docker.yml` succeeds for the release tag.
4. Confirm Docker Hub images are pushed (`mirusser/kubernetes-mcp-guard-gateway`, `mirusser/kubernetes-mcp-guard-devissuer`).
5. Confirm GHCR images are pushed (`ghcr.io/mirusser/kubernetes-mcp-guard-gateway`, `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer`).
6. Confirm GHCR packages are set to **public** if public pulls are intended; confirm Docker Hub repositories are public if intended.
7. Confirm the GitHub Packages page links back to the GitHub repository and has a description.
8. Confirm release notes include exact image names and tags.
9. Mark the GitHub release as **pre-release** while the project is experimental.
10. Verify quickstart commands (compose, README) reference the released tag.
11. Verify no secrets, tokens, or live credentials are present in docs, logs, sample manifests, or example env files.
12. Run the published-image smoke test from Epic 2 against the release tag before announcing.
```

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
- README has architecture summary, safety model bullets, a Mermaid diagram.
- Docker Hub and GHCR image *publishing* is configured in `package-docker.yml`.
- `LICENSE` (Apache-2.0) exists at the repo root.
- `SECURITY.md` exists with supported versions, reporting channel, and disclosure policy.
- Release checklist is documented in `docs/releasing.md`.
- Docker Hub and GHCR image references are visible in the README quickstart section.
- README includes the InfraGate vs. Kubernetes MCP Guard naming note.
- README includes a compatibility/support matrix.

Remaining:

- Nothing. Epic 1 is complete.

#### Compatibility / support matrix

Add a small section to the README (or to `docs/configuration.md` if it grows) so users do not assume production-grade compatibility across every Kubernetes distribution.

```markdown
| Area | Supported / tested |
| --- | --- |
| .NET | .NET 10 |
| Kubernetes | minikube / local cluster initially |
| MCP transport | HTTP MCP endpoint at `/mcp` |
| OIDC | DevIssuer (dev), Keycloak planned, Entra ID later |
| Container registries | GHCR, Docker Hub |
| Platforms | linux/amd64 initially |
```

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

#### Release smoke test

CI builds and pushes images today, but nothing exercises the documented release path end to end. Add a small smoke test (workflow or scripted check) that runs after a release and validates:

- `compose.release.yaml` boots cleanly from a clean checkout using the latest release tag.
- The gateway responds on `http://127.0.0.1:3001/mcp` (initialize handshake returns 200 + `Mcp-Session-Id`).
- DevIssuer responds on `http://127.0.0.1:3011/.well-known/openid-configuration`.
- One read-only Kubernetes tool call (e.g. `get_k8s_status` against `mcp-nginx-demo`) succeeds.

#### Acceptance criteria

- A user can run the project from published images with documented prerequisites and a single Docker Compose command.
- Local-build mode and released-image mode are both documented and clearly distinguished.
- Tag references in docs and compose files match the latest GitHub release tag.
- Published images are smoke-tested after release, not merely built and pushed.
- GHCR packages and Docker Hub repositories are confirmed public if public pulls are intended, and their descriptions link back to the GitHub repo.

#### Status

Already in place:

- `package-docker.yml` builds and publishes both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` to Docker Hub and `ghcr.io/<owner>/...` on `v*` tags and manual dispatch.
- `deploy/mode-c/compose.yaml` exists and works for local builds.
- `deploy/mode-c/compose.release.yaml` exists using published image tags.
- `docs/setup-guide.md` has a "Run from published images" section.
- `README.md` links to both the local-build and released-image quickstart.
- Tag references in docs and compose files reference the current release tag.

Remaining:

- Nothing. Epic 2 is complete.

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
4. Show the approval step (out-of-band browser approval via Gateway-hosted challenge URL).
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
- The demo documents the browser-based out-of-band approval flow; `scripts/approve-plan.sh` is documented as a direct-stdio-server-only fallback (not usable through the Gateway).

#### Status

Already in place:

- 14 MCP tools are implemented and documented across [src/InfraGate.McpServer/README.md](../../src/InfraGate.McpServer/README.md) and [docs/setup-guide.md](../../docs/setup-guide.md).
- `scripts/approve-plan.sh` exists for direct stdio server approval (not usable through the Gateway).
- `deploy/minikube/rbac.yaml` and `scripts/create-demo-kubeconfig.sh` already provision the `mcp-nginx-demo` namespace and ServiceAccount.
- `examples/failing-deployment/` contains `deployment.yaml` (broken image tag) and `fix.yaml`.
- `docs/demo-failing-deployment.md` provides the narrated walkthrough exercising read-only diagnosis, mutation proposal, out-of-band browser approval, apply, verification, and audit inspection.
- `README.md` links to the demo.

Remaining:

- Nothing. Epic 3 is complete.

---

### Epic 4 — Security model documentation

#### Problem

Users need a single doc that names hard boundaries, defense-in-depth layers, and explicit non-goals. Today this content is spread across the README, `MCP-compliance.md`, and per-project READMEs. There is also no per-tool RBAC/scope matrix, which security reviewers and Kubernetes operators always ask for.

#### Recommended changes

Add `docs/security-model.md` with five sections:

1. Hard boundaries: Kubernetes RBAC, JWT validation, required scopes, namespace enforcement, approval-gated mutation flow.
2. Defense-in-depth: prompt/input guardrails, output redaction, audit logging, MCP tool annotations, hash-bound approvals.
3. Threat model: assumptions, what risk is reduced, and what is explicitly out of scope.
4. Non-goals: not a replacement for Kubernetes RBAC, not a production IdP, not a full policy engine, no guarantee that AI-generated actions are correct, not production-certified.
5. Development-only components: DevIssuer, `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`, local kubeconfig scripts.

Add `docs/tool-permissions.md` (or a section inside `security-model.md`) with a per-tool matrix.

Link to `docs/MCP-compliance.md` for OAuth detail rather than duplicating it.

#### Threat model section (template)

```markdown
## Threat model

This project assumes:

- The Kubernetes API server enforces RBAC correctly.
- The configured identity provider issues valid tokens.
- The gateway is deployed behind TLS in production.
- MCP clients may be untrusted or partially trusted.
- AI-generated suggestions may be incorrect or unsafe.

This project aims to reduce risk from:

- Overbroad AI access to Kubernetes.
- Unauthorized mutation attempts.
- Plan tampering after approval.
- Prompt injection influencing responses.
- Accidental unsafe changes.

This project does not defend against:

- A compromised Kubernetes cluster.
- A compromised identity provider.
- A malicious administrator with cluster-admin.
- A compromised host running the gateway.
```

#### Tool permissions matrix (template)

Populate one row per shipped MCP tool. Keep this aligned with the actual `K8sTools.cs` surface and the `K8sManifestParser.cs` allow-list.

```markdown
| MCP tool | Type | Requires approval | Kubernetes verbs | Kubernetes resources | Scope required | Notes |
| --- | --- | :---: | --- | --- | --- | --- |
| `get_k8s_status` | Read | No | `get`, `list` | Deployments, Services, ConfigMaps, Pods, ReplicaSets | `mcp:tools` | Namespace-scoped |
| `get_k8s_events` | Read | No | `list` | Events | `mcp:tools` | Bounded; default 50, max 100 |
| `get_pod_logs` | Read | No | `get` (logs subresource) | Pods | `mcp:tools` | Bounded; tail + byte cap |
| `get_k8s_resource` | Read | No | `get` | Deployment, Service, ConfigMap | `mcp:tools` | No Secret values, no raw manifests |
| `get_*_diagnostics` | Read | No | `get`, `list` | Deployment / Pod / Service | `mcp:tools` | Aggregated, bounded |
| `request_apply_manifest` | Plan mutation | No apply yet | none/apply preview | Deployment, Service, ConfigMap | `mcp:tools` | Produces hash-bound plan |
| `request_delete_manifest` | Plan mutation | No apply yet | none/delete preview | Deployment, Service, ConfigMap | `mcp:tools` | Produces hash-bound plan |
| `request_scale_deployment` | Plan mutation | No apply yet | none/scale preview | Deployment | `mcp:tools` | Replicas bounded `0..5` |
| `request_restart_deployment` | Plan mutation | No apply yet | none/patch preview | Deployment | `mcp:tools` | |
| `request_set_deployment_image` | Plan mutation | No apply yet | none/patch preview | Deployment | `mcp:tools` | |
| `apply_approved_plan` | Mutation | Yes | depends on plan | depends on plan | `mcp:tools` | Applies only the exact approved plan |
```

The actual scope value reflects the gateway default `INFRA_GATE_OAUTH_SCOPE=mcp:tools`. If finer-grained scopes (`mcp:read` / `mcp:write`) are introduced later, update the matrix and the gateway auth options together.

#### Files to add or modify

- `docs/security-model.md` (new)
- `docs/tool-permissions.md` (new, or a section inside `security-model.md`)
- [README.md](../../README.md) (link to both)

#### Acceptance criteria

- Security model has its own document.
- Threat model section names assumptions, risks reduced, and out-of-scope risks.
- A per-tool RBAC/scope matrix exists and matches the shipped tool surface.
- README links to the security model and the tool permissions matrix.
- Dev-only components are clearly flagged.
- Guardrails are described as defense-in-depth, not as a hard boundary.
- The doc links to `MCP-compliance.md` rather than restating OAuth content.

#### Status

Already in place:

- [docs/security-model.md](../../docs/security-model.md) is implemented with five sections: hard boundaries, defense-in-depth, threat model, non-goals, and development-only components.
- [docs/tool-permissions.md](../../docs/tool-permissions.md) is implemented with a 14-tool matrix across three tables: read-only (8), plan mutation (5), and mutation execution (1), plus a plan-operation sub-table.
- Both docs are linked from `README.md` and cross-linked to each other and to `docs/MCP-compliance.md`.
- [docs/MCP-compliance.md](../../docs/MCP-compliance.md) covers OAuth 2.1, PKCE S256, RFC 8707, token-passthrough prevention, loopback redirect URI handling.
- `SECURITY.md` section 34 references the security model instead of the old forward reference placeholder.

Remaining:

- Nothing. Epic 4 is complete.

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
- `docs/production-oidc.md` exists with OIDC assumptions, required claims, config variables, and a Keycloak walkthrough.
- DevIssuer is explicitly marked as development-only in the production doc.

Remaining:

- Entra ID (or other enterprise provider) example planned for a future revision.

Epic 5 core (Keycloak walkthrough) is complete. The Entra ID example remains as a future addition.

---

### Epic 6 — Image and dependency scanning, supply-chain checks

#### Problem

A safety-focused project that publishes container images should scan them and should also catch vulnerable .NET / NuGet dependencies before they ship. There is no scanning step of either kind in any workflow today.

#### Recommended changes

- Add a Trivy (preferred) or Grype container scan step after image build in `package-docker.yml` (or in a dedicated `image-scan.yml`).
- Upload SARIF results to the GitHub Security tab.
- Add **dependency scanning** for .NET / NuGet packages — at minimum:
  - Enable GitHub Dependabot for NuGet (and GitHub Actions) via `.github/dependabot.yml`.
  - Run `dotnet list package --vulnerable --include-transitive` (or equivalent) in CI on the solution and fail on known vulnerable packages above the agreed severity.
  - Optionally add `dotnet list package --deprecated` and `--outdated` as advisory signal.
- Define an explicit ignore policy that applies to **both** image and dependency findings: any ignored CVE must include a documented reason and an expiry.
- Update the workflow `description:` field — it currently says only "Push images to Docker Hub" but the workflow already publishes to both Docker Hub and GHCR. Reword to reflect both registries (and scanning, once added).
- Consider pinning third-party Actions by commit SHA for the security-sensitive workflow once scanning is in place.

#### Severity policy (initial)

- Fail on `CRITICAL` with a fix available.
- Fail on `HIGH` with a fix available, or require an explicitly recorded ignore.
- Warn on `MEDIUM` and `LOW`.
- Document base-image noise expectations so the policy stays practical.

#### Files to add or modify

- [.github/workflows/package-docker.yml](../../.github/workflows/package-docker.yml) (description text + image scan step + SARIF upload)
- `.github/workflows/dependency-scan.yml` (new — `dotnet list package --vulnerable --include-transitive` on the solution; SARIF upload optional)
- `.github/dependabot.yml` (new — NuGet + GitHub Actions ecosystems)
- `.trivyignore` and/or a documented dependency-ignore allowlist (new, only if needed; ignored entries must include reason and expiry)
- [README.md](../../README.md) (badges for image scan and dependency scan once stable)

#### Future supply-chain hardening

Beyond image scanning, plan a follow-up wave that fits the project's safety identity. None of these are required for v0.0.x but should be tracked here so they are not forgotten:

- Generate SBOMs for published images (e.g. Syft / `docker buildx` SBOM attestations) and attach them to GitHub Releases.
- Publish build provenance / SLSA attestations.
- Sign images with cosign (keyless via GitHub OIDC is acceptable).
- Document how users can verify image signatures, SBOMs, and provenance from the published images.

#### Acceptance criteria

- CI scans both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` images.
- CI runs .NET dependency vulnerability scanning on the solution and fails on agreed severity thresholds.
- Dependabot is enabled for NuGet and GitHub Actions ecosystems.
- Scan failures (image and dependency) are understandable and actionable.
- Workflow description names both Docker Hub and GHCR.
- README displays scan badges once scanning is stable.
- Any ignored CVE (image or dependency) has a documented reason, owner, and expiry.
- A follow-up issue or roadmap entry tracks SBOM, provenance, and signing as future hardening.

#### Status

Already in place:

- `package-docker.yml` already builds and pushes to **both** Docker Hub and `ghcr.io/<owner>/...` (matrix over both images, login steps for both registries, metadata action emits tags for both).
- `unit-tests.yml`, `integration-tests.yml`, `sonar.yml`, and `dotnet-build.yml` cover .NET CI quality.
- Trivy image scanning runs in `package-docker.yml` with SARIF upload to the GitHub Security tab.
- `.github/dependabot.yml` is configured for NuGet and GitHub Actions ecosystems.
- Workflow `description:` text names both Docker Hub and GHCR.
- `.trivyignore` and dependency ignore policy are documented.

Remaining:

- Actions are pinned by major version, not by SHA (low-risk hardening item).
- SBOM, provenance, and image signing tracked as future supply-chain hardening (Epic 6 future items).

Epic 6 core (scanning + Dependabot + description fix) is complete. SHA-pinning and SBOM/signing remain as tracked future work.

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

- [docs/configuration.md](../../docs/configuration.md) is implemented with five canonical tables: McpServer (3 vars), McpGateway (7 vars), McpGateway.Auth (9 vars), DevIssuer (8 vars), and CI/Release/Scripts (14 vars). Each table uses columns Variable, Component, Required, Default, Example, Description, and Production guidance.
- Dev-only and production-dangerous settings are clearly flagged with per-row production guidance.
- All four runtime project READMEs (`src/InfraGate.*/README.md`), `docs/setup-guide.md`, and `docs/devs-readme.md` link to `docs/configuration.md` instead of duplicating env-var references. Old `## Configuration` sections were removed.
- `README.md` links to `docs/configuration.md` from the project map.
- Runnable shell snippets and compose examples are preserved; only prose/table description duplicates were removed.

Remaining:

- Nothing. Epic 7 is complete.

---

### Epic 8 — Architecture document

#### Problem

The README has a Mermaid diagram, but request flows (read, mutation, approval, auth, audit) are not laid out in one place. A single architecture doc helps technical reviewers form a mental model without reading every C# file.

#### Recommended changes

Add `docs/architecture.md` containing:

1. Component diagram (Mermaid).
2. Read-only request flow.
3. Mutation request flow (`request_*` plan creation).
4. Approval flow (hash-bound, out-of-band browser approval via Gateway-hosted challenge URL).
5. Auth flow (OAuth 2.1 JWT validation, scope check, 403 insufficient-scope challenge, and browser approval OAuth PKCE session).
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
- [docs/architecture.md](../../docs/architecture.md) is implemented (316 lines) with seven sections: component map (Mermaid), OAuth login and MCP authorization flow, read-only tool call flow, mutation plan request flow, browser approval challenge flow, approved apply flow, audit flow, and image/registry layout.
- The doc cross-links to `docs/MCP-compliance.md`, `docs/security-model.md`, `docs/tool-permissions.md`, and `docs/configuration.md` rather than restating their content.
- Mutation approval flow is documented end-to-end through three sequence diagrams (plan request, browser challenge, approved apply).
- Registry/image layout table documents both GHCR and Docker Hub images.

Remaining:

- Nothing. Epic 8 is complete.

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
- `AGENTS.md` captures the agent collaboration norms.
- [CONTRIBUTING.md](../../CONTRIBUTING.md) covers local setup, verification, MCP tool change rules, and documentation ownership.
- [.github/PULL_REQUEST_TEMPLATE.md](../../.github/PULL_REQUEST_TEMPLATE.md) includes a safety checklist surfacing auth, approval, RBAC, guardrail, and tool-surface concerns.
- [.github/ISSUE_TEMPLATE/bug_report.md](../../.github/ISSUE_TEMPLATE/bug_report.md) and [.github/ISSUE_TEMPLATE/feature_request.md](../../.github/ISSUE_TEMPLATE/feature_request.md) exist.
- [CHANGELOG.md](../../CHANGELOG.md) follows Keep-a-Changelog format with pre-release tags.
- [docs/roadmap.md](../../docs/roadmap.md) provides the outward-facing public roadmap and is linked from the README.
- `CODE_OF_CONDUCT.md` was marked as optional and is not present; the existing ground rules in `CONTRIBUTING.md` and issue templates cover community expectations.

Remaining:

- Nothing. Epic 9 is complete.

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

For any work that touches the gateway demo or Kubernetes invocations, use `.codex/skills/infragate-mcp-gateway` as the canonical contract: HTTP MCP at `http://127.0.0.1:3001/mcp`, demo namespace `mcp-nginx-demo`, plan-first mutation flow, out-of-band browser approval via Gateway-hosted challenge URL inside `apply_approved_plan`.

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
- README includes a compatibility/support matrix.
- `LICENSE` and `SECURITY.md` exist at the repo root.
- Users can run the project from published Docker Hub and GHCR images via a documented compose file.
- Published images are smoke-tested after release (compose boots, gateway initializes, DevIssuer reachable, one read-only tool call succeeds).
- GHCR and Docker Hub package visibility is verified to match intent and links back to the GitHub repo.
- Experimental and pre-production limitations are visible.
- A standalone security model doc exists and is linked from the README.
- The security model doc includes a threat model section.
- A per-tool RBAC/scope matrix exists (in `docs/tool-permissions.md` or inside `security-model.md`) and matches the shipped tool surface.
- A production OIDC guide exists with at least one real provider walkthrough.
- A configuration reference exists and other docs link to it instead of duplicating env-var detail.
- A consolidated architecture doc exists.
- An end-to-end demo (manifests + walkthrough) exercises read-only and approval-gated flows.
- CI runs container image scanning with a documented severity and ignore policy.
- CI runs .NET / NuGet dependency vulnerability scanning, and Dependabot is configured for NuGet and GitHub Actions.
- A documented release checklist covers tests, image publishing, GHCR/Docker Hub package visibility, secrets hygiene, pre-release flagging, and quickstart tag verification.
- A follow-up issue or roadmap entry tracks SBOM, provenance, and image signing as future supply-chain hardening.
- The `package-docker.yml` workflow description names both Docker Hub and GHCR.
- `CONTRIBUTING.md`, PR template, issue templates, and `CHANGELOG.md` exist.
- The repository is ready for external experimental users to evaluate safely.

✅ All 21 criteria are met. The roadmap is fully implemented.
