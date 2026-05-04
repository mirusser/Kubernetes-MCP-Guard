# Epic 6 — Image and Dependency Scanning, Supply-Chain Checks

## Source

This plan implements **Epic 6** of [.agents/Plans/roadmap-final.md](roadmap-final.md). Read that section before editing — its Acceptance criteria and Status block are the source of truth. This document is the agent-facing implementation guide.

## Context

Kubernetes MCP Guard publishes container images and references external NuGet packages. A safety-focused infrastructure project that ships container images should scan them for OS/package CVEs and catch vulnerable .NET dependencies before they reach a release tag. Neither check exists today. This epic adds both, plus Dependabot automation to keep dependencies current, and fixes a minor wording drift in the existing Docker workflow description.

## Current state (established by exploration)

| Item | Status |
| --- | --- |
| `.github/workflows/package-docker.yml` | Exists; builds + pushes both GHCR and Docker Hub. Third-party Actions already SHA-pinned. No scan step. |
| `package-docker.yml inputs.push_images.description` | Currently reads "Push images to Docker Hub" — does not mention GHCR |
| `.github/dependabot.yml` | Does not exist |
| `.github/workflows/dependency-scan.yml` | Does not exist |
| `.trivyignore` | Does not exist |
| Solution | `InfraGate.slnx`, four `src/` projects; six NuGet packages total |

Actions in `package-docker.yml` are already SHA-pinned — no SHA-pinning work needed for that file.

## Deliverables

| # | File | Action |
| --- | --- | --- |
| 1 | [.github/workflows/package-docker.yml](../../.github/workflows/package-docker.yml) | Edit — fix description + add image scan step + SARIF upload |
| 2 | `.github/workflows/dependency-scan.yml` | New — `dotnet list package --vulnerable --include-transitive` |
| 3 | `.github/dependabot.yml` | New — NuGet + GitHub Actions ecosystems |
| 4 | `.trivyignore` | New (placeholder with policy comment) |

**Out of scope for Epic 6:**
- SBOM generation, SLSA provenance attestations, cosign image signing (deferred — tracked in "Future supply-chain hardening" section)
- `docs/configuration.md` (Epic 7), `docs/architecture.md` (Epic 8), `CONTRIBUTING.md` (Epic 9)
- Changes to compose files, kubeconfig scripts, or runtime behavior

---

## Step 1 — Fix `package-docker.yml` description

**File:** [.github/workflows/package-docker.yml](../../.github/workflows/package-docker.yml)

Update the `inputs.push_images.description` field:

```yaml
# before
description: Push images to Docker Hub

# after
description: Push images to Docker Hub and GitHub Container Registry
```

This is the only `description:` in the file. The workflow `name: Docker` stays unchanged.

---

## Step 2 — Add Trivy image scan + SARIF upload to `package-docker.yml`

### Permissions

Add `security-events: write` to the top-level `permissions` block (it currently has `contents: read` and `packages: write`):

```yaml
permissions:
  contents: read
  packages: write
  security-events: write    # added — required for SARIF upload
```

### Build step — enable `load: true`

Add `load: true` to the existing `Build and publish image` step so the image is loaded into the local Docker daemon for scanning. Single-platform (linux/amd64) builds support this alongside `push`.

```yaml
- name: Build and publish image
  uses: docker/build-push-action@bcafcacb16a39f128d818304e6c9c0c18556b85f  # v7
  with:
    context: .
    file: ${{ matrix.dockerfile }}
    push: ${{ env.PUSH_IMAGES == 'true' }}
    load: true                                    # added
    tags: ${{ steps.meta.outputs.tags }}
    labels: ${{ steps.meta.outputs.labels }}
```

### Trivy scan step

Add after the build step. Pin `aquasecurity/trivy-action` to a SHA at implementation time (do not use a floating major-version tag).

```yaml
- name: Scan image for vulnerabilities
  uses: aquasecurity/trivy-action@<sha>  # vX.Y.Z — pin to SHA at implementation time
  with:
    image-ref: ${{ fromJSON(steps.meta.outputs.json).tags[0] }}
    format: sarif
    output: trivy-${{ matrix.image }}.sarif
    severity: CRITICAL,HIGH
    ignore-unfixed: true
    exit-code: '1'
```

**Severity policy:**
- Fail on `CRITICAL` or `HIGH` with a fix available (`ignore-unfixed: true` + `exit-code: '1'`).
- `MEDIUM` and `LOW` are included in the SARIF output but do not fail the build.
- Any ignored CVE must be added to `.trivyignore` with a documented reason, owner, and expiry (see Step 5).

### SARIF upload step

Add after the scan step, always running so results reach the Security tab before the job fails.

```yaml
- name: Upload Trivy SARIF results
  if: always()
  uses: github/codeql-action/upload-sarif@<sha>  # vX — pin to SHA at implementation time
  with:
    sarif_file: trivy-${{ matrix.image }}.sarif
    category: trivy-${{ matrix.image }}
```

---

## Step 3 — Add `.github/workflows/dependency-scan.yml`

**File:** `.github/workflows/dependency-scan.yml` (new)

**Triggers:** `pull_request` (branches: main), `push` (branches: main), `workflow_dispatch`.

**Steps:**

1. `actions/checkout` — SHA-pinned (use same SHA as `package-docker.yml`)
2. `actions/setup-dotnet` — .NET 10 SDK
3. `dotnet restore InfraGate.slnx`
4. Vulnerability check — fail on CRITICAL or HIGH:

```yaml
- name: Check NuGet package vulnerabilities
  run: |
    dotnet list InfraGate.slnx package --vulnerable --include-transitive 2>&1 | tee vuln-report.txt
    if grep -q 'Critical\|High' vuln-report.txt; then
      echo "FAIL: Critical or High severity NuGet vulnerabilities found."
      cat vuln-report.txt
      exit 1
    fi
```

5. Advisory — deprecated packages (non-blocking, `continue-on-error: true`):

```yaml
- name: Check deprecated NuGet packages (advisory)
  continue-on-error: true
  run: dotnet list InfraGate.slnx package --deprecated --include-transitive
```

6. Upload artifact (always):

```yaml
- name: Upload vulnerability report
  if: always()
  uses: actions/upload-artifact@<sha>
  with:
    name: nuget-vuln-report
    path: vuln-report.txt
```

**Note:** `dotnet list package --vulnerable` in .NET 8+ exits non-zero when vulnerabilities are found. The `grep` is belt-and-suspenders.

---

## Step 4 — Add `.github/dependabot.yml`

**File:** `.github/dependabot.yml` (new)

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
    open-pull-requests-limit: 5

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
    open-pull-requests-limit: 5
```

`directory: "/"` — `InfraGate.slnx` is at the repo root; Dependabot resolves NuGet projects from there.

---

## Step 5 — Add `.trivyignore` (policy placeholder)

**File:** `.trivyignore` (new, repo root)

Create even if no CVEs need ignoring now. Establishes the policy contract — prevents future ignores without documentation.

```
# .trivyignore — Trivy CVE ignore list
#
# Every entry MUST include:
#   - CVE identifier
#   - Reason for ignoring (e.g. "not exploitable in this context because...")
#   - Owner (GitHub username)
#   - Expiry date (YYYY-MM-DD) — re-evaluate when this date passes
#
# Example:
# CVE-XXXX-XXXXX # reason: not exploitable in this context | owner: @mirusser | expires: 2026-12-01
```

---

## Acceptance criteria (from the roadmap)

- [ ] CI scans both `kubernetes-mcp-guard-gateway` and `kubernetes-mcp-guard-devissuer` images.
- [ ] CI runs .NET dependency vulnerability scanning on the solution and fails on CRITICAL/HIGH with fix available.
- [ ] Dependabot is enabled for NuGet and GitHub Actions ecosystems.
- [ ] Scan failures (image and dependency) are understandable and actionable.
- [ ] `inputs.push_images.description` names both Docker Hub and GHCR.
- [ ] Any ignored CVE (image or dependency) has a documented reason, owner, and expiry.
- [ ] A follow-up roadmap entry tracks SBOM, provenance, and image signing as future hardening.

---

## Verification

1. `grep 'description:' .github/workflows/package-docker.yml` — mentions Docker Hub and GitHub Container Registry.
2. `grep 'security-events' .github/workflows/package-docker.yml` — returns `write`.
3. `grep -c 'trivy-action\|upload-sarif' .github/workflows/package-docker.yml` — returns 2.
4. `.github/workflows/dependency-scan.yml` exists; `grep 'vulnerable' .github/workflows/dependency-scan.yml` matches.
5. `grep 'nuget\|github-actions' .github/dependabot.yml` — returns two matches.
6. `.trivyignore` exists with policy comment header.
7. Local dry-run (before pushing):
   ```bash
   dotnet list InfraGate.slnx package --vulnerable --include-transitive
   ```
   Should exit 0 with no CVEs against current packages.
8. `git status` shows only the four deliverable files — no scope creep.

## Suggested commit shape

1. `ci(docker): fix description, add Trivy image scan and SARIF upload`
2. `ci: add NuGet dependency vulnerability scan workflow`
3. `ci: add Dependabot for NuGet and GitHub Actions`
4. `ci: add .trivyignore with policy comment template`

## Future supply-chain hardening (deferred, tracked here)

File as a GitHub issue or roadmap entry once Epic 6 ships:

- Generate SBOMs for published images (Syft or `docker buildx` SBOM attestations) and attach to GitHub Releases.
- Publish SLSA build provenance attestations.
- Sign images with cosign (keyless via GitHub OIDC).
- Document how users verify image signatures, SBOMs, and provenance.
