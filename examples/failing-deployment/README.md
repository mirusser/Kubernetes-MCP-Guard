# Failing Deployment — Demo Manifests

Demo fixtures for the end-to-end approval-gated walkthrough at [docs/demo-failing-deployment.md](../../docs/demo-failing-deployment.md).

- `deployment.yaml` — a deliberately broken `nginx-demo` Deployment + Service in the `mcp-nginx-demo` namespace. The container image tag `nginx:1.27-doesnotexist` is invalid, so Pods land in `ImagePullBackOff`. Apply this with `kubectl` to set up the demo's starting state.
- `fix.yaml` — the same Deployment + Service with a valid image (`nginx:1.27-alpine`). Reference manifest used by the alternate demo path that exercises `request_apply_manifest` instead of `request_set_deployment_image`.

The narrated walkthrough explains how to diagnose the failure with the read-only MCP tools, propose a fix as a digest-bound plan, approve it through the Gateway browser UI, apply it, and verify recovery.

## Observer Demo

The [InfraGate.Observer](../../src/InfraGate.Observer/README.md) automatically detects the broken deployment as an anomaly without any MCP client interaction.

**Prerequisites:** Bring up the full stack (Keycloak + Gateway + McpServer + Observer) via `docker compose up` or local dev, then apply `deployment.yaml`:

```bash
kubectl apply -f examples/failing-deployment/deployment.yaml
```

**Wait for a cycle** (default 60s cadence) or trigger one on demand:

```bash
curl -X POST http://localhost:3003/observe-now
```

The Observer returns a JSON array of `AnomalyReport` objects. Expect reports for:

| AnomalyKind | Target | Severity | Reason |
|---|---|---|---|
| `DeploymentUnavailable` | `deployment/nginx-demo` | `High` | `availableReplicas == 0` while `spec.replicas > 0` |
| `PodUnhealthy` | `pod/nginx-demo-*` | `High` | All pods in `ImagePullBackOff` |
| `ServiceNoEndpoints` | `service/nginx-demo` | `High` | 0 ready endpoints, selector matches no running pods |
| `WarningEvent` | various | `Medium` | Sustained `BackOff` events |

If the JSON file sink is enabled (`INFRA_GATE_OBSERVER_FILE_SINK_ROOT=.mcp-observer/findings`), each cycle writes a `{cycleId}.json` file.

**Apply the fix** and observe resolution:

```bash
kubectl apply -f examples/failing-deployment/fix.yaml
```

Within 2 observation cycles (default), the Observer emits `Status=Resolved` reports with `Severity=Low` for each previously-active anomaly.

**Clean up:**

```bash
kubectl delete -f examples/failing-deployment/deployment.yaml
```
