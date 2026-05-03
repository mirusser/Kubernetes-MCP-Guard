# Releasing Kubernetes MCP Guard

This file owns the release process for Kubernetes MCP Guard. Releases are marked **pre-release** while the project is experimental; do not mark a release as stable until the project exits the experimental phase.

## Release Checklist

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

- ...

### Known limitations

- ...

### Upgrade notes

- ...
```

## Smoke Test

An end-to-end published-image smoke test (booting `compose.release.yaml`, verifying the gateway and DevIssuer endpoints, and running one read-only tool call) is owned by Epic 2 / `compose.release.yaml`. See that file once it lands.
