# Failing Deployment — Demo Manifests

Demo fixtures for the end-to-end approval-gated walkthrough at [docs/demo-failing-deployment.md](../../docs/demo-failing-deployment.md).

- `deployment.yaml` — a deliberately broken `nginx-demo` Deployment + Service in the `mcp-nginx-demo` namespace. The container image tag `nginx:1.27-doesnotexist` is invalid, so Pods land in `ImagePullBackOff`. Apply this with `kubectl` to set up the demo's starting state.
- `fix.yaml` — the same Deployment + Service with a valid image (`nginx:1.27-alpine`). Reference manifest used by the alternate demo path that exercises `request_apply_manifest` instead of `request_set_deployment_image`.

The narrated walkthrough explains how to diagnose the failure with the read-only MCP tools, propose a fix as a hash-bound plan, approve it (via Codex elicitation or `scripts/approve-plan.sh`), apply it, and verify recovery.
