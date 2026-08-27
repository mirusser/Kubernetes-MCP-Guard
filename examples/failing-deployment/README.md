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

## Remediation Demo

The [InfraGate.Planner](../../src/InfraGate.Planner/README.md) and [InfraGate.Executor](../../src/InfraGate.Executor/README.md) can consume Observer output, propose an approval-pending plan, send an Approval Access Code through Mailpit, and execute the approved plan through the gateway. The required handoff and SMTP settings are documented in [docs/configuration.md](../../docs/configuration.md).

Current fixture caveat: `deployment.yaml` uses an invalid image. The Planner's operation menu includes `restart_deployment`, `scale_deployment`, and `set_deployment_image` — the LLM decides which one to propose, so an approved execution may resolve the anomaly outright (`set_deployment_image`) or leave the invalid image in place (`restart_deployment`/`scale_deployment`). If the image wasn't fixed, apply `fix.yaml` after the approved execution to observe the Observer's `Resolved` reports for this fixture.

**Prerequisites:** Bring up the local OAuth stack with Observer, Planner, Executor, Gateway, Keycloak, PostgreSQL, and Mailpit; then apply the broken deployment:

```bash
kubectl apply -f examples/failing-deployment/deployment.yaml
```

Wait for the Observer cycle to publish an anomaly batch to the Planner. The Planner should emit a `RemediationProposal` after it creates a plan through `propose_plan`, and Mailpit should receive the Approval Access Code email.

Open Mailpit at `http://127.0.0.1:8025`, copy the 8-character code, then open the gateway code-entry page:

```text
http://127.0.0.1:3001/approvals/code
```

Submit the code, sign in through the local Keycloak approval UI when prompted, review the plan, and approve it. The Executor waits through `wait_for_plan_approval`, then calls `execute_approved_plan` for the approved plan.

To observe resolution for the current invalid-image fixture, apply the valid manifest and wait for two more Observer cycles:

```bash
kubectl apply -f examples/failing-deployment/fix.yaml
```

**Clean up:**

```bash
kubectl delete -f examples/failing-deployment/deployment.yaml
```
