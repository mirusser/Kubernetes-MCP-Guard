# Future hardening — container non-root data permissions

## Current state

Both `devissuer.Dockerfile` and `mcp-gateway.Dockerfile` use `USER $APP_UID` (1654).
The gateway writes approval and guardrail files to `/data/approvals` and `/data/guardrails`.
For local dev via Docker Compose, these are bind-mounted from host dirs created by
`scripts/create-demo-kubeconfig.sh` with `chmod 1777` (world-writable + sticky bit, like `/tmp`).

## Future production/Kubernetes hardening

The `chmod 1777` approach is not suitable for production. Replace with one of:

1. **Kubernetes `securityContext`** — set `runAsUser: 1654`, `runAsGroup: 1654`, and use a
   `PersistentVolumeClaim` for `/data`. The PVC filesystem will be owned by 1654:1654 automatically
   when the pod runs as that user.

2. **`fsGroup`** — set `securityContext.fsGroup: 1654` on the pod. Kubernetes changes the ownership
   of the mounted volume to match `fsGroup`, so the container's app user (UID 1654) can write.

3. **Init container** — use an init container running as root to `chown 1654:1654` the data
   directories on a shared volume before the main container starts.

4. **Explicit `runAsUser: 1654`** — in a plain Docker Compose or `docker run` deployment, run the
   container with `--user 1654:1654` and ensure the host data directories are `chown`-ed to 1654.

5. Move the files all together for prod.

Dev/demo:
  local files under /data/approvals and /data/guardrails

Production:
  approvals -> database table
  guardrail config -> database or versioned config store
  audit trail -> append-only DB table / event log

## What not to do

- Do not run the container as root in production.
- Do not use `chmod 777` / `chmod 1777` in production — it exposes approval and guardrail data to
  any process on the host.
- Do not `chown -R /app` — the app directory should remain root-owned so a compromised process
  cannot modify binaries or configuration inside the image.
