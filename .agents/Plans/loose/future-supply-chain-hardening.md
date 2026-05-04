# Future hardening — SBOM, provenance, and image signing

## Source

Deferred from [Epic 6 — Image and Dependency Scanning, Supply-Chain Checks](../18-epic-6-image-dependency-scanning-supply-chain.md).

## Context

Kubernetes MCP Guard publishes container images to Docker Hub and GHCR. A safety-focused
infrastructure project should generate SBOMs, publish build provenance attestations, and sign
images so consumers can verify what they're running.

Epic 6 itself is fully implemented (staged, not yet committed):
- Phase 1: `package-docker.yml` description fixed to name both Docker Hub and GHCR.
- Phase 2: Trivy image scan + SARIF upload in `package-docker.yml`, Dependabot for NuGet and
  GitHub Actions (`.github/dependabot.yml`), NuGet dependency vulnerability scan workflow
  (`.github/workflows/dependency-scan.yml`), and `.trivyignore` policy placeholder.
- Files staged: `package-docker.yml`, `dependency-scan.yml`, `dependabot.yml`, `.trivyignore`.

This document tracks the follow-up items (SBOM, provenance, signing) deferred from Epic 6.

## Future work

1. **SBOM generation** — Generate SBOMs for published images (Syft or `docker buildx` SBOM
   attestations) and attach them to GitHub Releases.
2. **SLSA provenance** — Publish build provenance attestations for each release.
3. **Image signing** — Sign images with cosign (keyless via GitHub OIDC is acceptable).
4. **Verification documentation** — Document how users can verify image signatures, SBOMs, and
   provenance from the published images.

None of these are required for v0.0.x but should be implemented before a stable release.
