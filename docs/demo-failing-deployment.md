# Demo — Recover a Failing Deployment Through the Approval Gate

This walkthrough exercises the full Kubernetes MCP Guard safety model end-to-end against a deliberately broken Deployment: read-only diagnosis, mutation proposal, hash-bound approval, exact-plan apply, verification, and audit inspection. Every change to the cluster goes through a `request_*` plan and an explicit approval before `apply_approved_plan` writes it. The production-grade controls behind this flow (RBAC boundary, JWT validation, threat model) will be documented in `docs/security-model.md` (planned in Epic 4); this doc is operational, not a security argument.

The demo runs entirely against the local minikube cluster set up by the project's quickstart — no production credentials and no real OIDC provider required.

## Prerequisites

- Gateway and DevIssuer running per [README "How To Run"](../README.md#how-to-run-) (Option 1 — published images, or Option 2 — build from source).
- `./scripts/create-demo-kubeconfig.sh --compose` already executed in this checkout (this provisions the `mcp-nginx-demo` namespace, RBAC, ServiceAccount, and the kubeconfig the gateway mounts).
- An MCP client connected to `http://127.0.0.1:3001/mcp`. Codex CLI per the README is the reference client. Any MCP client supporting elicitation works; clients without elicitation use the `scripts/approve-plan.sh` fallback in [Step 4](#step-4--approve-the-plan).
- `kubectl` available locally — only needed in [Step 1](#step-1--deploy-the-broken-workload) and [Step 8](#step-8--cleanup) to apply and remove the broken manifest directly. The fix is applied through the gateway.

## Step 1 — Deploy the broken workload

```bash
kubectl apply -f examples/failing-deployment/deployment.yaml -n mcp-nginx-demo
```

The workload is intentionally broken (invalid image tag), so we apply it with `kubectl` to establish the failing starting state. The gateway is for the *fix* — applying a known-broken object through it would just produce a pending plan to deploy something we already know does not work.

**What you should see:** `deployment.apps/nginx-demo created` and `service/nginx-demo created`. Within ~30 seconds the Pods enter `ImagePullBackOff`.

## Step 2 — Inspect with read-only tools

Ask the MCP client to call these tools (no approval needed — read-only):

1. `get_k8s_status` with `namespace=mcp-nginx-demo`.
   **What you should see:** the `nginx-demo` Deployment with `desired=2 ready=0 available=0`, two Pods listed, one `nginx-demo` Service.

2. `get_k8s_events` with `namespace=mcp-nginx-demo`.
   **What you should see:** Events of type `Warning` reporting `Failed to pull image "nginx:1.27-doesnotexist"` and `Back-off pulling image …`.

3. `get_pod_diagnostics` with `namespace=mcp-nginx-demo` and `podName=<one of the failing pods from step 1>`.
   **What you should see:** container `nginx` in state `waiting: ImagePullBackOff` (or `ErrImagePull`), Pod conditions showing it never became Ready, the same image-pull events as the namespace view but scoped to this Pod.

`get_pod_logs` returns nothing here because the container never started — that empty result is itself a diagnostic signal. The `Events` + container state is what tells the story.

## Step 3 — Propose the fix (mutation plan)

Primary path — narrowest possible change:

```text
request_set_deployment_image(
  namespace = "mcp-nginx-demo",
  name      = "nginx-demo",
  container = "nginx",
  image     = "nginx:1.27-alpine"
)
```

**What you should see:** the tool returns a `PlanId` (a short identifier like `20260503-a1b2c3d4`) and a plan summary describing the patch. **Nothing has been applied to the cluster yet.** A pending plan file appears at `.mcp-approvals/pending/<PlanId>.json`.

### Alternate — `request_apply_manifest`

The same fix can be planned by submitting `examples/failing-deployment/fix.yaml` through `request_apply_manifest`. Prefer `request_set_deployment_image` for narrow image swaps (smaller diff, easier review); prefer `request_apply_manifest` when the change spans multiple fields, replicas, ports, or several resources at once.

## Step 4 — Approve the plan

Two paths — pick the one your MCP client supports.

### Codex CLI / elicitation path

Call `apply_approved_plan(planId="<PlanId>")`. The MCP server sends an elicitation request back to the client; Codex prompts you with the plan summary. Approving the prompt triggers the apply (Step 5 happens immediately as part of the same call).

**What you should see:** an interactive prompt in your MCP client. After you approve, the tool returns the apply result.

### `scripts/approve-plan.sh` fallback (clients without elicitation)

```bash
./scripts/approve-plan.sh <PlanId>
```

The script reads `.mcp-approvals/pending/<PlanId>.json`, computes its SHA-256, and writes the hash to `.mcp-approvals/approved/<PlanId>.sha256`. After this file exists, the next `apply_approved_plan(planId="<PlanId>")` call succeeds without an elicitation prompt.

This is **not** a way to bypass approval — the file-system action *is* the approval. The hash is bound to the exact pending plan; any later edit to the pending JSON breaks the match (see Step 5).

**What you should see:** `Approved <PlanId>` and the path to the approval file.

## Step 5 — Apply

```text
apply_approved_plan(planId = "<PlanId>")
```

The server re-reads `pending/<PlanId>.json`, recomputes its hash, and compares it to `approved/<PlanId>.sha256`. On match, it applies the plan against the Kubernetes API.

**What you should see:** the tool returns the apply result describing the patched Deployment. Within seconds, Kubernetes pulls the new image and the Pods become Ready.

If anything edits `pending/<PlanId>.json` between approval and apply, the recomputed hash no longer matches and the server refuses to apply, emitting an `approval_hash_mismatch` audit entry. This is the tamper-detection guarantee in action.

## Step 6 — Verify recovery

Re-run the read-only tools from Step 2:

- `get_k8s_status` should now show `desired=2 ready=2 available=2`, both Pods `Running`.
- `get_pod_diagnostics` should show the `nginx` container in state `running`.
- `get_pod_logs` (optional) now returns nginx's startup lines — the container is alive.

## Step 7 — Inspect the audit

Two JSONL streams record the demo. Both live under volumes mounted by `deploy/mode-c/compose.yaml` and `deploy/mode-c/compose.release.yaml`.

### Server-side (`.mcp-approvals/audit.jsonl`)

Records the plan lifecycle: `plan_requested`, `plan_approved`, `plan_applied` (and on a tampered plan, `approval_hash_mismatch` + `apply_denied`). One entry shape:

```json
{"timestampUtc":"2026-05-03T12:34:56.789Z","eventName":"plan_applied","payload":{"id":"20260503-a1b2c3d4","operation":"setImage","namespace":"mcp-nginx-demo","hash":"sha256:…"}}
```

### Gateway-side (`.mcp-guardrails/audit.jsonl`)

Records each MCP tool call observed at the gateway with subject identity, action (`warn`, `redact`, `audit`), and category. One entry shape:

```json
{"timestamp":"2026-05-03T12:34:56.789Z","toolName":"request_set_deployment_image","direction":"request","action":"audit","categories":["tool-use"],"planId":"20260503-a1b2c3d4","subject":"ada","authenticationType":"oauth-jwt"}
```

You should be able to thread one `planId` through both streams and see the full request → plan → approve → apply trail.

## Step 8 — Cleanup

```bash
kubectl delete -f examples/failing-deployment/deployment.yaml -n mcp-nginx-demo
```

The gateway-mediated equivalent is `request_delete_manifest` followed by `apply_approved_plan` — the demo uses `kubectl` here for symmetry with Step 1, since the workload was applied with `kubectl` to begin with. Either path works; pick the one that fits the story you want to tell.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `apply_approved_plan` returns "plan not found" | The gateway and server must share the same `K8S_MCP_APPROVAL_ROOT`. The Mode C compose files mount `.mcp-approvals` into both containers; if you run a custom setup, point both at the same directory. |
| `approval_hash_mismatch` audit entry, apply refused | `pending/<PlanId>.json` was edited between approval and apply (manually, or by re-running `request_*` and overwriting). Generate a fresh plan and approve that one. |
| Codex elicitation prompt never appears | The connected MCP client does not support elicitation. Use the `scripts/approve-plan.sh` fallback in Step 4. |
| Pods stuck in `ImagePullBackOff` after Step 5 | The replica count is 2 and the rollout takes a few seconds; re-run `get_k8s_status`. If it persists, check `get_k8s_events` for a different pull error (e.g. registry rate limiting). |
