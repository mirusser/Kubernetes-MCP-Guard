# Epic 2 — Published-Image Quickstart

## Source

This plan implements **Epic 2** of [.agents/Plans/roadmap-final.md](roadmap-final.md). Read that section before editing — its Acceptance criteria and Status block are the source of truth. This document is the agent-facing implementation guide.

## Goal

Give a new user a way to run Kubernetes MCP Guard from the **already-published** GHCR / Docker Hub images using a single Docker Compose command — no clone-and-build required. Document the released-image path in [setup-guide.md](../../docs/setup-guide.md) next to the existing local-build path, link it from [README.md](../../README.md), and add a smoke test that catches release-path breakage end-to-end.

Touch only what Epic 2 requires; do not pre-empt Epics 3–9.

## Phase

Phase 1 — Public credibility and first-run experience. High priority, low risk.

## Skills to apply

- `.codex/skills/verify-readme-docs` — drives every doc change. Code, compose files, and workflows are the source of truth. Patch only real drift; do not rewrite voice or formatting.
- `.codex/skills/infragate-mcp-gateway` — canonical contract for the runtime: HTTP MCP at `http://127.0.0.1:3001/mcp`, demo namespace `mcp-nginx-demo`, plan-first mutation flow. The smoke test must respect this contract.
- `.codex/skills/code-standards` — applies only if any C# is incidentally touched (none expected for this epic).

## Pre-flight checks (read-only)

Run these before the first edit so the plan is grounded in current state, not assumptions:

```bash
ls /home/mirusser/MyRepos/GitRepos/k8s-toolkit/deploy/mode-c/
ls /home/mirusser/MyRepos/GitRepos/k8s-toolkit/scripts/
rg -n 'kubernetes-mcp-guard-(gateway|devissuer)' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/.github/workflows/package-docker.yml
rg -n 'image:' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/deploy/mode-c/compose.yaml
rg -n 'mirusser/kubernetes-mcp-guard|ghcr.io/mirusser' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/README.md /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs
gh release list --limit 5 2>/dev/null || true
```

Confirm:

- `deploy/mode-c/compose.yaml` exists and uses `build:` blocks (local-build only).
- `deploy/mode-c/compose.release.yaml` does **not** exist.
- `package-docker.yml` matrix publishes both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` to Docker Hub *and* `ghcr.io/<owner>/...`.
- `scripts/create-demo-kubeconfig.sh --compose` is the kubeconfig path used by `compose.yaml` (volume mount `../../.kube/mcp-nginx-demo.compose.config`).
- README already references both registries (Epic 1 added that section).
- A latest published release tag exists; record it for use in docs (`vX.Y.Z`). If no tag exists yet, default the doc text to `latest` and add a note that the user should pin to a specific release tag.

If any of these contradict the Epic 2 Status block in the roadmap, stop and update the roadmap before continuing.

## Deliverables

| # | File | Action | Owner per doc-ownership table |
| --- | --- | --- | --- |
| 1 | `deploy/mode-c/compose.release.yaml` | New | Released-image runtime entry point |
| 2 | [docs/setup-guide.md](../../docs/setup-guide.md) | Edit (additive) | Local development and demo setup |
| 3 | [README.md](../../README.md) | Edit (small additive) | Project entry point — link only |
| 4 | `scripts/smoke-test-release.sh` | New | Released-image smoke test (driver) |
| 5 | `.github/workflows/release-smoke-test.yml` | New (optional, see decision rule) | CI smoke test on release publish |

Do not create or modify in this epic:

- `examples/failing-deployment/` (Epic 3).
- `docs/security-model.md`, `docs/tool-permissions.md` (Epic 4).
- `docs/production-oidc.md` (Epic 5).
- Image-scan / dependency-scan workflows, Dependabot, `.trivyignore`, the `package-docker.yml` `description:` field (Epic 6).
- `docs/configuration.md` (Epic 7) — the released-image env vars must be documented inline in `setup-guide.md`, not extracted to a new reference doc.
- `docs/architecture.md` (Epic 8).
- `CONTRIBUTING.md`, PR/issue templates, `CHANGELOG.md` (Epic 9).

If a change you are about to make falls in this list, stop and revisit the roadmap.

---

## Step 1 — Add `deploy/mode-c/compose.release.yaml`

**File:** `deploy/mode-c/compose.release.yaml` (new)

**Discipline:** mirror `deploy/mode-c/compose.yaml` exactly, with these differences only:

1. **No `build:` blocks.** Replace each service's `build:` with a single `image:` line that pins a published tag.
2. **Use a `${TAG}` env var with a default.** Keep the file pinnable per release without editing it. Recommended default is `latest` while the project is pre-release; document overriding it via `TAG=vX.Y.Z` in [setup-guide.md](../../docs/setup-guide.md).
3. **Default to GHCR.** GHCR is the primary registry per the roadmap. Docker Hub is documented as the alternate in `setup-guide.md`, not in this compose file.
4. **Keep all environment variables, ports, volumes, `depends_on`, and `extra_hosts` identical to `compose.yaml`.** The runtime contract (kubeconfig at `/run/kube/mcp-nginx-demo.compose.config`, OAuth via DevIssuer, allowed namespace `mcp-nginx-demo`, ports `127.0.0.1:3001` / `127.0.0.1:3011`) must not drift between the two files. The smoke test depends on this.

**Suggested content:**

```yaml
# deploy/mode-c/compose.release.yaml
# Run published Kubernetes MCP Guard images from GHCR.
# Override the release tag with: TAG=vX.Y.Z docker compose -f deploy/mode-c/compose.release.yaml up
# Docker Hub equivalents: mirusser/kubernetes-mcp-guard-{gateway,devissuer}:${TAG}

services:
  devissuer:
    image: ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:${TAG:-latest}
    environment:
      ASPNETCORE_URLS: http://0.0.0.0:3011
      INFRA_GATE_DEV_ISSUER_ISSUER: http://127.0.0.1:3011
      INFRA_GATE_DEV_ISSUER_INTERNAL_ENDPOINT_BASE: http://devissuer:3011
      INFRA_GATE_DEV_ISSUER_RESOURCE: http://127.0.0.1:3001/mcp
      INFRA_GATE_DEV_ISSUER_SCOPE: mcp:tools
    ports:
      - "127.0.0.1:3011:3011"

  mcp-gateway:
    image: ghcr.io/mirusser/kubernetes-mcp-guard-gateway:${TAG:-latest}
    depends_on:
      - devissuer
    environment:
      ASPNETCORE_URLS: http://0.0.0.0:3001
      INFRA_GATE_OAUTH_AUTHORITY: http://127.0.0.1:3011
      INFRA_GATE_OAUTH_METADATA_ADDRESS: http://devissuer:3011/.well-known/openid-configuration
      INFRA_GATE_OAUTH_RESOURCE: http://127.0.0.1:3001/mcp
      INFRA_GATE_OAUTH_SCOPE: mcp:tools
      INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA: "false"
      INFRA_GATE_DOWNSTREAM_ASSEMBLY: /app/server/InfraGate.McpServer.dll
      INFRA_GATE_GUARD_AUDIT_ROOT: /data/guardrails
      KUBECONFIG: /run/kube/mcp-nginx-demo.compose.config
      K8S_MCP_APPROVAL_ROOT: /data/approvals
      K8S_MCP_ALLOWED_NAMESPACES: mcp-nginx-demo
    volumes:
      - ../../.kube/mcp-nginx-demo.compose.config:/run/kube/mcp-nginx-demo.compose.config:ro
      - ../../.mcp-approvals:/data/approvals
      - ../../.mcp-guardrails:/data/guardrails
    extra_hosts:
      - "host.docker.internal:host-gateway"
    ports:
      - "127.0.0.1:3001:3001"
```

**Verification:**

```bash
TAG=latest docker compose -f deploy/mode-c/compose.release.yaml config
diff <(docker compose -f deploy/mode-c/compose.yaml config | rg -v '^\s*(build|context|dockerfile):') \
     <(TAG=latest docker compose -f deploy/mode-c/compose.release.yaml config | rg -v '^\s*image:')
```

Only image identity should differ between `compose.yaml` and `compose.release.yaml`.

---

## Step 2 — Update [docs/setup-guide.md](../../docs/setup-guide.md)

**File:** [docs/setup-guide.md](../../docs/setup-guide.md) (additive edits only)

**Discipline:** add a new subsection inside **Mode C**, *after* the existing Compose-from-source path and *before* "Source Mode C". Do not rewrite or reorder existing content. Do not move env-var tables.

**New subsection — recommended heading:** `#### Mode C — Run from published images`

**Required content (in order):**

1. **One-line framing** — "This is the fastest path to evaluate the gateway. It pulls released images from GHCR (Docker Hub equivalents are listed below). Use this when you do not need to modify source."

2. **Prerequisites bullet list** — link to existing prerequisite steps; do not duplicate them:
   - Minikube running (Step 2 of this guide).
   - `./scripts/create-demo-kubeconfig.sh --compose` already executed in this checkout (the compose file mounts `.kube/mcp-nginx-demo.compose.config`).
   - Docker Compose v2.

3. **Run** — fenced block:

   ```bash
   ./scripts/create-demo-kubeconfig.sh --compose
   TAG=vX.Y.Z docker compose -f deploy/mode-c/compose.release.yaml up
   ```

   Add one explicit line: "Replace `vX.Y.Z` with the release tag from <https://github.com/mirusser/Kubernetes-MCP-Guard/releases>. Omitting `TAG=` falls back to `latest`, which moves over time and is fine for a quick try but is not stable for repeatable runs."

4. **Endpoints** — same bullets as the existing Mode C section (`http://127.0.0.1:3001/mcp`, `http://127.0.0.1:3011`). Do not invent new ports.

5. **Codex CLI config** — point to the existing Mode C TOML block above with a "Same Codex CLI config as Mode C from source." sentence. Do not duplicate.

6. **Docker Hub equivalents** — short note + fenced block:

   ```yaml
   # Docker Hub alternates (substitute into compose.release.yaml if preferred)
   ghcr.io/mirusser/kubernetes-mcp-guard-devissuer:${TAG} → mirusser/kubernetes-mcp-guard-devissuer:${TAG}
   ghcr.io/mirusser/kubernetes-mcp-guard-gateway:${TAG}   → mirusser/kubernetes-mcp-guard-gateway:${TAG}
   ```

7. **Smoke test pointer** — one sentence: "After release, the published-image path is verified by `scripts/smoke-test-release.sh` (see [Verification](#verification))." Do not describe the script body here — Step 4 owns that file.

8. **Tradeoff callout** — reuse the existing Mode C tradeoff callout by reference; do not restate it.

**Do not change:** the Mermaid diagram, the prerequisites section, the existing Mode C source block, the env-var reference tables, the troubleshooting table.

**Verification:**

```bash
rg -n 'compose.release.yaml' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs/setup-guide.md
rg -n 'mirusser/kubernetes-mcp-guard-(gateway|devissuer)|ghcr.io/mirusser' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs/setup-guide.md
```

Both must return matches in the new subsection.

---

## Step 3 — Update [README.md](../../README.md) (link only)

**File:** [README.md](../../README.md)

**Discipline:** README owns the project pitch and quickstart entry, not the full released-image runbook. Add only what is needed to make the released-image path discoverable.

**Required additions:**

### 3.1 — Replace the forward-link sentence under "Published Images"

The current README ends the "Published Images" section with:

> "An end-to-end published-image quickstart will land with [Epic 2](.agents/Plans/roadmap-final.md)."

Replace that single sentence with:

> "Run the project from published images with [Mode C — Run from published images](docs/setup-guide.md#mode-c--run-from-published-images) (Compose: `TAG=vX.Y.Z docker compose -f deploy/mode-c/compose.release.yaml up`)."

Adjust the anchor slug (`#mode-c--run-from-published-images`) to match the heading you used in Step 2 — confirm it after the edit by searching the rendered file.

### 3.2 — Tag note

If not already present in the "Published Images" block, ensure exactly one sentence states "Released image tags follow the GitHub release tag (`vX.Y.Z`); the floating `latest` tag is also published." Do not duplicate if Epic 1 already added equivalent wording.

**Do not change:** badges, the Mermaid diagram, the Mode A/B/C quickstart wording, the safety-model bullets, the compatibility matrix.

**Verification:**

```bash
rg -n 'compose.release.yaml' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/README.md
rg -n 'Run from published images' /home/mirusser/MyRepos/GitRepos/k8s-toolkit/README.md /home/mirusser/MyRepos/GitRepos/k8s-toolkit/docs/setup-guide.md
```

Both files must reference the same heading slug.

---

## Step 4 — Add `scripts/smoke-test-release.sh`

**File:** `scripts/smoke-test-release.sh` (new, executable)

**Purpose:** verify that the released images, the released-image compose file, and the documented quickstart actually work end-to-end. The script is what `release-smoke-test.yml` (Step 5, optional) calls; it must also be runnable by hand from a clean checkout.

**Contract — what it must do, in order:**

1. **Inputs** — accept `TAG` (default `latest`) and `KUBECONFIG_PATH` (default `.kube/mcp-nginx-demo.compose.config`). Use `set -euo pipefail`. Match the style of `scripts/create-demo-kubeconfig.sh`.

2. **Pre-checks** — fail fast with a clear message if any of these is missing:
   - `docker compose version` succeeds.
   - The kubeconfig path exists. If not, suggest `./scripts/create-demo-kubeconfig.sh --compose`.
   - The release compose file exists at `deploy/mode-c/compose.release.yaml`.

3. **Boot** — `TAG="${TAG}" docker compose -f deploy/mode-c/compose.release.yaml up -d --pull always`. Use `--pull always` so the script reflects the actual published image, not a cached one.

4. **Wait for readiness** — poll with a bounded retry loop (e.g. up to 60 seconds, 2-second sleeps; do not poll forever):
   - DevIssuer: `curl -fsS http://127.0.0.1:3011/.well-known/openid-configuration` returns 200.
   - Gateway: an MCP `initialize` request to `http://127.0.0.1:3001/mcp` returns 200 and the response includes a session id.

   Use the static-bearer header path **only if** `INFRA_GATE_GATEWAY_BEARER_TOKEN` is set; otherwise rely on the OAuth DevIssuer flow already wired into the compose file. (The compose file uses OAuth, so the script's default path should obtain a token from DevIssuer or skip the authenticated tool call — see step 5.)

5. **One read-only Kubernetes tool call** — exercise `get_k8s_status` against `mcp-nginx-demo`. Two acceptable shapes; pick **one** and stick with it:

   - **Shape A (preferred when feasible):** mint a DevIssuer-issued JWT via the OAuth client-credentials-style helper if one exists, then send an authenticated `tools/call` MCP request. Reject the temptation to re-implement OAuth in shell — if no helper exists today, fall back to Shape B.
   - **Shape B (fallback):** assert the gateway returns `401 Unauthorized` with a properly formed `WWW-Authenticate: Bearer ...` challenge that includes `resource_metadata`. This proves the auth surface is wired without requiring a real token. Document this fallback in the script header so reviewers know it is intentional.

   Whichever shape is used, the script must exit non-zero on any unexpected response and print the failing payload before tearing down.

6. **Teardown** — always run `docker compose -f deploy/mode-c/compose.release.yaml down -v` in a `trap`, including on success. Do not leave volumes behind.

7. **Output** — last line on success is `OK: smoke test passed for tag <tag>`. On failure, dump the last 50 lines of `docker compose logs` for both services before exiting non-zero.

**Discipline:**

- No production secrets. The compose file uses DevIssuer; if the script needs a token, it mints one against `http://127.0.0.1:3011`.
- Do not assume `kubectl` is available; the script's job is to exercise the gateway/server, not the cluster directly. Cluster reachability is the kubeconfig's responsibility (already enforced by `create-demo-kubeconfig.sh`).
- Keep it under ~150 lines. If it grows beyond that, split helpers but keep them in `scripts/`.

**Verification:**

```bash
shellcheck scripts/smoke-test-release.sh
chmod +x scripts/smoke-test-release.sh
TAG=latest ./scripts/smoke-test-release.sh   # locally, with minikube + kubeconfig in place
```

---

## Step 5 — Add `.github/workflows/release-smoke-test.yml` (decision rule)

**File:** `.github/workflows/release-smoke-test.yml` (new, optional)

**Decision rule — only ship this workflow if all are true:**

1. A self-hosted runner with minikube (or a comparable Kubernetes-in-CI option such as `kind` via `helm/kind-action`) is available to this repo.
2. The script in Step 4 runs cleanly locally first.
3. The team is willing to gate releases on this signal.

If any of those is false, **stop after Step 4**. Document in `docs/releasing.md` (owned by Epic 1, not this epic) that smoke testing is currently a manual step that runs `scripts/smoke-test-release.sh`. Do not silently add a green badge for a workflow that does not actually exercise Kubernetes — that would be worse than no signal at all.

**If shipped, the workflow must:**

- Trigger on `release: published` and on `workflow_dispatch` with a `tag` input. Do not trigger on `push` or `pull_request` — pulling release images on every PR is wasteful and noisy.
- Use `kind` (recommended over self-hosted minikube) with [`helm/kind-action`](https://github.com/helm/kind-action) so the workflow stays on `ubuntu-latest`. Apply `deploy/minikube/rbac.yaml` against the kind cluster — the manifest is namespace-scoped and works on any cluster.
- Run `./scripts/create-demo-kubeconfig.sh --compose` against the kind cluster.
- Run `TAG=${{ github.event.release.tag_name || inputs.tag }} ./scripts/smoke-test-release.sh`.
- Pin third-party Actions by major version for now. SHA pinning is Epic 6's territory.

**Do not** add this workflow's badge to the README in this epic. README badges land once the workflow has been green for at least one release; that is a follow-up concern, not Epic 2 scope.

---

## Documentation ownership reminder

| Topic | Owner doc | What this epic adds |
| --- | --- | --- |
| Released-image runtime steps | `docs/setup-guide.md` | New "Run from published images" subsection inside Mode C |
| Project entry point + link | `README.md` | One forward-link sentence, anchor only |
| Released-image compose file | `deploy/mode-c/compose.release.yaml` | New file, mirrors local-build compose |
| Smoke test driver | `scripts/smoke-test-release.sh` | New file |
| Release process / checklist | `docs/releasing.md` (Epic 1, already exists) | **No change** — only step 11 ("smoke test from Epic 2") becomes meaningful once Step 4 lands; do not rewrite the checklist here |
| Per-tool RBAC, threat model | `docs/security-model.md` (Epic 4) | Out of scope |
| Single env-var reference | `docs/configuration.md` (Epic 7) | Out of scope |

When tempted to add content to a doc that does not own that topic, link to the right doc instead.

---

## Acceptance criteria (from the roadmap)

Reproduced verbatim — every box must be checked before the epic is closed:

- [ ] A user can run the project from published images with documented prerequisites and a single Docker Compose command.
- [ ] Local-build mode and released-image mode are both documented and clearly distinguished.
- [ ] Tag references in docs and compose files match the latest GitHub release tag.
- [ ] Published images are smoke-tested after release, not merely built and pushed.
- [ ] GHCR packages and Docker Hub repositories are confirmed public if public pulls are intended, and their descriptions link back to the GitHub repo.

Plus implicit additions captured in the Status block:

- [ ] `deploy/mode-c/compose.release.yaml` exists, references published images, and shares runtime env vars / ports / volumes with `compose.yaml`.
- [ ] `setup-guide.md` documents both registries; `README.md` links to the new subsection.
- [ ] `scripts/smoke-test-release.sh` exists, is executable, and runs end-to-end from a clean checkout against released images.

The "GHCR packages public" and "Docker Hub repos public" boxes are repository-admin actions, not file edits. Surface them in the open-questions list below; do not silently mark them done.

---

## Verification

End-to-end check on a clean checkout:

1. `deploy/mode-c/compose.release.yaml` exists; `docker compose -f deploy/mode-c/compose.release.yaml config` exits 0.
2. The diff between `compose.yaml` and `compose.release.yaml` (after stripping `build:` from one and `image:` from the other) is empty for env vars, volumes, ports, `depends_on`, and `extra_hosts`.
3. `rg -n 'compose.release.yaml' README.md docs/setup-guide.md` returns matches in both files.
4. `rg -n 'ghcr.io/mirusser/kubernetes-mcp-guard' deploy/mode-c/compose.release.yaml` returns two matches (one per service).
5. `bash -n scripts/smoke-test-release.sh && shellcheck scripts/smoke-test-release.sh` passes.
6. With minikube running and `./scripts/create-demo-kubeconfig.sh --compose` already executed, `TAG=latest ./scripts/smoke-test-release.sh` exits 0 and prints `OK: smoke test passed for tag latest` (or the tag you pinned).
7. Tearing the smoke test down leaves no `kubernetes-mcp-guard-*` containers and no orphan volumes (`docker ps -a`, `docker volume ls`).
8. README's anchor link to the new subsection resolves (preview the README locally or use `gh markdown-preview`).
9. `git diff --check` reports no whitespace issues.
10. No file outside the deliverables list above was modified.

---

## Suggested commit shape

One commit per deliverable keeps review small:

1. `chore(deploy): add deploy/mode-c/compose.release.yaml for published images`
2. `docs(setup): add Mode C run-from-published-images subsection`
3. `docs(readme): link the published-image quickstart`
4. `chore(scripts): add smoke-test-release.sh for end-to-end release verification`
5. (optional) `ci: add release-smoke-test workflow gated on release publish` — only if Step 5's decision rule passes.

---

## Open questions for the user (resolve before merging)

- **Compose file location.** Roadmap accepts either `deploy/mode-c/compose.release.yaml` or `compose.release.yaml` at the repo root. This plan picks `deploy/mode-c/compose.release.yaml` to match the existing `compose.yaml` neighbor and keep the same relative volume paths. Confirm before authoring, or supply the alternate location.
- **Default tag.** This plan defaults `TAG` to `latest` so the compose file is runnable without editing. Confirm, or pick a fixed pinned tag (`vX.Y.Z`) and update the docs and smoke-test script together.
- **CI workflow vs. manual smoke test.** Step 5 ships only if a Kubernetes-in-CI path (kind / self-hosted minikube) is acceptable. Confirm whether to ship the workflow now or defer it. If deferred, the smoke-test script in Step 4 still lands and `docs/releasing.md` step 11 references it as a manual step.
- **Smoke-test auth shape.** Step 4 offers Shape A (mint a real DevIssuer JWT and call `tools/call`) and Shape B (assert the 401 challenge shape). Confirm which one to implement first. Shape B is faster to build and resilient to MCP protocol drift; Shape A is the stronger signal.
- **Registry visibility.** GHCR packages and Docker Hub repositories must be set to **public** if public pulls are intended, and their package/repo descriptions should link to the GitHub repo. Confirm both are already public and described, or schedule the admin work as part of closing the epic. This is the only acceptance-criteria item the agent cannot complete unaided.
- **Apache-2.0 vs source-only distribution.** If the released images include third-party components with attribution requirements, surface them as a follow-up — not in this epic. Out of scope here, but worth flagging now.
