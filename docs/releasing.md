# Releasing Kubernetes MCP Guard

This file owns the release process for Kubernetes MCP Guard. Releases are marked **pre-release** while the project is experimental; do not mark a release as stable until the project exits the experimental phase.

## Release Checklist

1. Confirm unit tests pass on `main`.
2. Confirm integration tests pass (self-hosted runner, both `INFRA_GATE_RUN_INTEGRATION` and `INFRA_GATE_RUN_GATEWAY_INTEGRATION`).
3. Confirm the Keycloak integration tests pass on `main` via the `keycloak-tests.yml` CI workflow (requires Docker on `ubuntu-latest`).
4. Confirm `package-docker.yml` succeeds for the release tag.
5. Confirm Docker Hub images are pushed (`mirusser/kubernetes-mcp-guard-gateway`, `mirusser/kubernetes-mcp-guard-devissuer`).
6. Confirm GHCR images are pushed (`ghcr.io/mirusser/kubernetes-mcp-guard-gateway`, `ghcr.io/mirusser/kubernetes-mcp-guard-devissuer`).
7. Confirm the production Docker deploy job completed, or intentionally skip it if the `production` GitHub Environment is not configured.
8. Confirm GHCR packages are set to **public** if public pulls are intended; confirm Docker Hub repositories are public if intended.
9. Confirm the GitHub Packages page links back to the GitHub repository and has a description.
10. Confirm release notes include exact image names and tags.
11. Confirm `CHANGELOG.md` has a release-ready entry for the version.
12. Mark the GitHub release as **pre-release** while the project is experimental.
13. Verify quickstart commands (compose, README) reference the released tag.
14. Verify no secrets, tokens, or live credentials are present in docs, logs, sample manifests, or example env files.
15. Run the published-image smoke test from Epic 2 against the release tag before announcing.

## Release Notes Template

Paste this into GitHub Releases and fill in `VERSION` and the change sections:

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

- Summarize the `CHANGELOG.md` entry for this version.

### Known limitations

- ...

### Upgrade notes

- ...
```

## Smoke Test

Run the published-image smoke test manually against the release tag before announcing:

```bash
./scripts/create-demo-kubeconfig.sh --compose
TAG=vX.Y.Z ./scripts/smoke-test-release.sh
```

The script boots `deploy/mode-c/compose.release.yaml` from GHCR, waits for both the DevIssuer OIDC discovery endpoint and the gateway HTTP server, asserts the gateway returns a well-formed 401 auth challenge, then tears everything down. It exits non-zero and dumps logs on failure.

A CI workflow (`release-smoke-test.yml`) is planned once a Kubernetes-in-CI path (kind) is in place; until then this step is manual.
