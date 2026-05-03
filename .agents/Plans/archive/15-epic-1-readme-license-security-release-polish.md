# Epic 1 — README, LICENSE, SECURITY.md, Release Polish

## Source

This plan implements **Epic 1** of [.agents/Plans/roadmap-final.md](roadmap-final.md). Read that section before editing — its Acceptance criteria and Status block are the source of truth. This document is the agent-facing implementation guide.

## Goal

Make the repository legitimate as a public, experimental open-source release by adding the missing OSS license, vulnerability reporting policy, release checklist, README quickstart for published images, naming explanation, and a compatibility matrix. Touch only what Epic 1 requires; do not pre-empt Epics 2–9.

## Phase

Phase 1 — Public credibility and first-run experience. High priority, low risk.

## Skills to apply

- `.codex/skills/verify-readme-docs` — drives every README change. Code and tests are the source of truth. Patch only real drift; do not rewrite voice or formatting.
- `.codex/skills/code-standards` — applies only if any code is incidentally touched (none expected for this epic).

## Pre-flight checks (read-only)

Run these before the first edit so the plan is grounded in current state, not assumptions:

```bash
ls -la /home/mirusser/MyRepos/GitRepos/k8s-toolkit/LICENSE /home/mirusser/MyRepos/GitRepos/k8s-toolkit/SECURITY.md 2>/dev/null
ls /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs/ /home/mirusser/MyRepos/GitRepos/k8s-toolkit/.github/
rg -n 'mirusser/kubernetes-mcp-guard|ghcr.io/mirusser' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/README.md /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs /home/mirusser/MyRepos/GitRepos/k8s-toolkit/deploy
rg -n 'description:' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/.github/workflows/package-docker.yml
```

Confirm:

- `LICENSE`, `SECURITY.md`, `docs/releasing.md` do not exist.
- README badges are present (do not duplicate them).
- `package-docker.yml` already publishes to Docker Hub and GHCR.
- Image names referenced in workflows and compose: `kubernetes-mcp-guard-gateway`, `kubernetes-mcp-guard-devissuer`.

If any of these contradict the Epic 1 Status block in the roadmap, stop and update the roadmap before continuing.

## Deliverables

| # | File | Action | Owner per doc-ownership table |
| --- | --- | --- | --- |
| 1 | `LICENSE` | New | Repo root — OSS license |
| 2 | `SECURITY.md` | New | Repo root — vulnerability policy |
| 3 | `docs/releasing.md` | New | Release checklist + notes template |
| 4 | `.github/release.yml` | New (optional) | GitHub auto-generated release-notes config |
| 5 | [README.md](../../README.md) | Edit (additive only) | Project entry point |

Do not create `docs/security-model.md`, `docs/configuration.md`, `docs/architecture.md`, `docs/tool-permissions.md`, `compose.release.yaml`, or any new workflow in this epic — they belong to later epics.

---

## Step 1 — Add `LICENSE`

**File:** `LICENSE` (new, repo root)

**License:** Apache-2.0 (recommended for an infra/security-adjacent project; includes explicit patent grant).

**Content:** Verbatim Apache-2.0 text from <https://www.apache.org/licenses/LICENSE-2.0.txt>. Set the copyright line to:

```text
Copyright 2026 mirusser
```

(Year resolved from today's date; do not invent a different attribution.)

**Verification:** GitHub renders the License chip on the repo page; `rg -n 'Apache License' LICENSE | head -1` returns a match.

---

## Step 2 — Add `SECURITY.md`

**File:** `SECURITY.md` (new, repo root)

**Required sections:**

1. **Supported Versions** — explicitly state the project is experimental; security fixes target the latest release only.
2. **Reporting a Vulnerability** — direct reporters to GitHub Security Advisories (private vulnerability reporting). Provide a fallback email only if one is intentional; do not invent one.
3. **What to include** — affected version/commit, description, reproduction steps, potential impact, suggested mitigation if known.
4. **Disclosure** — request no public issues for security vulnerabilities until coordination.
5. **Scope reminder** — link to `docs/MCP-compliance.md` for the OAuth/auth surface and to the future `docs/security-model.md` (placeholder line; the file does not exist yet, so phrase as "see `docs/security-model.md` once published" or omit until Epic 4 lands — do not create that doc here).

**Tone:** experimental, factual. Match the repo's existing voice in `docs/setup-guide.md`.

**Verification:** GitHub displays the "Security policy" tab on the repo. `rg -n 'GitHub Security Advisories' SECURITY.md` returns a match.

---

## Step 3 — Add `docs/releasing.md`

**File:** `docs/releasing.md` (new)

**Sections:**

1. **Purpose** — one paragraph: this file owns the release process for Kubernetes MCP Guard. Releases are pre-release while the project is experimental.
2. **Release checklist** — copy the 12-step list from Epic 1 of [roadmap-final.md](roadmap-final.md) verbatim. Do not abbreviate; the roadmap is the source of truth for the steps.
3. **Release notes template** — a fenced markdown block ready to paste into GitHub Releases:

   ```markdown
   ## Kubernetes MCP Guard VERSION — Experimental Preview

   This is an experimental release of Kubernetes MCP Guard.

   ### Images

   Docker Hub:

   - `mirusser/kubernetes-mcp-guard-gateway:VERSION`
   - `mirusser/kubernetes-mcp-guard-devissuer:VERSION`

   GitHub Container Registry:

   - `ghcr.io/mirusser/kubernetes-mcp-guard-gateway:VERSION`
   - `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:VERSION`

   ### Status

   Experimental. Not recommended for production workloads.

   ### Changes

   - ...

   ### Known limitations

   - ...

   ### Upgrade notes

   - ...
   ```

4. **Smoke test reference** — one line saying the published-image smoke test is owned by Epic 2 / `compose.release.yaml`; link forward without describing the test here.

**Verification:** `rg -n 'Confirm GHCR packages are set to' docs/releasing.md` returns a match. Step count in checklist is exactly 12 (matches roadmap).

---

## Step 4 — Add `.github/release.yml` (optional)

**File:** `.github/release.yml` (new, optional)

This is GitHub's auto-generated release-notes config. Include only if it makes the release-notes template easier to apply; otherwise skip and rely on `docs/releasing.md` alone.

**If included**, configure label-based categories that match the project:

```yaml
changelog:
  exclude:
    labels:
      - ignore-for-release
  categories:
    - title: Breaking changes
      labels:
        - breaking
    - title: Features
      labels:
        - feature
        - enhancement
    - title: Fixes
      labels:
        - bug
        - fix
    - title: Security
      labels:
        - security
    - title: Documentation
      labels:
        - docs
    - title: Other
      labels:
        - "*"
```

**Decision rule:** if no PR labels are in active use today, skip this file and let Epic 9 introduce it alongside the PR template.

---

## Step 5 — Update `README.md`

**File:** [README.md](../../README.md)

**Discipline:** additive edits only. Do not rewrite paragraphs that already exist. Do not move badges. Do not change the Mermaid diagram. Do not duplicate env-var tables. Use `verify-readme-docs` rules.

**Required additions (and only these):**

### 5.1 — Naming note

A short paragraph (3–5 lines) near the top, after the project pitch but before the badges, explaining that:

- The public name is **Kubernetes MCP Guard**.
- The internal codename **InfraGate** appears in `.slnx`, project folders, env-var prefixes (`INFRA_GATE_*`), and Docker labels.
- They refer to the same project; the rename is gradual and does not change runtime behavior.

### 5.2 — Published images section

A new H2 (e.g. `## Published images`) placed before the existing local-build quickstart. It must list both registries verbatim:

```markdown
### GitHub Container Registry (recommended)

- `ghcr.io/mirusser/kubernetes-mcp-guard-gateway:<tag>`
- `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:<tag>`

### Docker Hub

- `mirusser/kubernetes-mcp-guard-gateway:<tag>`
- `mirusser/kubernetes-mcp-guard-devissuer:<tag>`
```

Add one sentence: "Released image tags follow the GitHub release tag (`vX.Y.Z`)." Do **not** add a `compose.release.yaml` example here — that file does not exist yet and belongs to Epic 2. A forward-link sentence is fine: "An end-to-end published-image quickstart will land with [Epic 2](.agents/Plans/roadmap-final.md)."

### 5.3 — Compatibility / support matrix

Add a `## Compatibility` section using the table from Epic 1's "Compatibility / support matrix" subsection in [roadmap-final.md](roadmap-final.md). Verbatim copy of that table.

### 5.4 — Footer links

Add a small `## Project policies` section near the bottom (or fold into an existing footer) with:

- License: link to `LICENSE`.
- Security policy: link to `SECURITY.md`.
- Release process: link to `docs/releasing.md`.

If the README already has a links/footer section, append; do not create a duplicate.

### 5.5 — Experimental status sentence

If not already present in the first paragraph, add one explicit sentence: "This project is experimental — APIs, image tags, configuration, and runtime behavior may change." Place it adjacent to the existing pitch.

**Do not change:** badges, the Mermaid diagram, the existing safety-model bullets, the existing Mode A/B/C content, or the list of MCP tools.

**Verification:** the README still renders end-to-end with no broken Markdown. `rg -n 'Apache License|LICENSE\)|SECURITY.md\)' README.md` returns matches for the new policy links. Diff shows only additive sections.

---

## What is explicitly out of scope for this epic

- `compose.release.yaml` (Epic 2).
- Demo manifests in `examples/` (Epic 3).
- `docs/security-model.md`, threat model section, `docs/tool-permissions.md` (Epic 4).
- `docs/production-oidc.md` (Epic 5).
- Image scanning, Dependabot, dependency scan workflow, workflow `description:` text fix (Epic 6).
- `docs/configuration.md` (Epic 7).
- `docs/architecture.md` (Epic 8).
- `CONTRIBUTING.md`, `CHANGELOG.md`, PR/issue templates, `CODE_OF_CONDUCT.md`, outward-facing `docs/roadmap.md` (Epic 9).

If a change you are about to make falls in this list, stop and revisit the roadmap.

---

## Acceptance criteria (from the roadmap)

Reproduced verbatim — every box must be checked before the epic is closed:

- [ ] Repository has a clear OSS license at the root.
- [ ] Repository has a vulnerability reporting policy linked from the README.
- [ ] Future releases can reuse the documented release-notes template.
- [ ] README links to both `LICENSE` and `SECURITY.md`.
- [ ] Docker Hub and GHCR image names are visible in the README without scrolling through Mode A/B/C.

Plus the implicit additions captured in the Status block:

- [ ] README explains the InfraGate vs. Kubernetes MCP Guard naming.
- [ ] README includes a compatibility/support matrix.
- [ ] `docs/releasing.md` contains the full 12-step release checklist and the release-notes template.

## Verification

End-to-end check on a clean checkout:

1. `LICENSE`, `SECURITY.md`, and `docs/releasing.md` exist.
2. `rg -n 'mirusser/kubernetes-mcp-guard-gateway|ghcr.io/mirusser/kubernetes-mcp-guard-gateway' README.md` returns matches.
3. `rg -n 'InfraGate' README.md` shows the naming-note paragraph.
4. `rg -nE '^\| \.NET' README.md` shows the compatibility matrix row.
5. `rg -n 'releasing.md|LICENSE|SECURITY.md' README.md` shows the policy footer links.
6. README renders cleanly (open locally or `gh markdown-preview` if available).
7. `git diff --check` reports no whitespace issues.
8. No file outside the deliverables list above was modified.

## Suggested commit shape

One commit per deliverable keeps review small:

1. `chore: add Apache-2.0 LICENSE`
2. `docs: add SECURITY.md vulnerability reporting policy`
3. `docs: add docs/releasing.md with 12-step release checklist and notes template`
4. `docs(readme): add naming note, published-image references, compatibility matrix, policy links`

(Optional) `chore: add .github/release.yml auto-changelog config` — only if Step 4 was included.

## Open questions for the user (resolve before merging)

- Apache-2.0 vs MIT — confirm Apache-2.0 (recommendation) before generating the LICENSE text.
- Copyright holder line — confirm `mirusser` is the correct attribution, or supply a different one.
- Whether to ship `.github/release.yml` now (Step 4) or defer to Epic 9 alongside the PR template.
