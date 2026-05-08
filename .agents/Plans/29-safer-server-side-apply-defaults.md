# Implementation Plan: Safer Server-Side Apply Defaults

## Summary
- Make manifest server-side apply non-forcing by default for `Deployment`, `Service`, and `ConfigMap`.
- Remove `force: true` from both manifest dry-run calls and final approved manifest apply calls.
- Keep the public MCP contract unchanged: no `force` flag, no `request_force_apply_manifest`, and no `AllowForceApply` setting in this PR.

## Interfaces And Behavior
- Public tools remain `request_apply_manifest(namespace, manifest)` and `apply_approved_plan(planId)` with no model-provided force option.
- Plan JSON remains unchanged.
- Audit event names remain unchanged; ownership conflicts are captured through existing `dry_run_failed` or `apply_failed` audit entries with a clear conflict message.
- Conflict message should use this canonical text, followed by Kubernetes details:

```text
Apply refused by Kubernetes field ownership conflict.
The plan was not forced because force apply can take ownership of fields from another manager.
Re-request the plan after reconciling the live object, or use an explicitly approved force-apply flow if enabled.
```

## Key Changes
- In `K8sManager.DryRun.cs`, omit the nullable `force` argument from `PatchNamespacedDeploymentWithHttpMessagesAsync`, `PatchNamespacedServiceWithHttpMessagesAsync`, and `PatchNamespacedConfigMapWithHttpMessagesAsync`.
- In `K8sManager.Apply.cs`, omit the nullable `force` argument from final manifest apply calls for Deployment, Service, and ConfigMap.
- Keep `dryRun=All`, `fieldManager=infra-gate-mcp`, `fieldValidation=Strict`, cancellation tokens, and existing supported-kind restrictions.
- Do not change scale, restart, set-image, or delete flows; they are not server-side apply manifest operations.
- Add a small internal formatter/helper for HTTP/Kubernetes `409 Conflict` responses in manifest apply/dry-run paths. Do not retry automatically with force.

## Task Breakdown
1. Update manifest dry-run apply calls to omit `force`; verify all request-time SSA dry-run PATCHes omit `force=`.
2. Update approved manifest apply calls to omit `force`; verify real SSA PATCHes omit `force=`.
3. Add conflict formatting for manifest SSA `409 Conflict`; preserve original API details after the canonical refusal text.
4. Ensure request-time conflicts create no plan, pre-apply conflicts mutate nothing, and final apply conflicts return failure through existing audit flow.
5. Strengthen model-facing contract tests so neither gateway nor server tools expose `force` or `allowForceApply`.

## Test Plan
- Update `K8sManagerRequestTests.RequestApplyManifestAsync_CreatesPlan_ForSupportedManifest` to assert dry-run PATCH queries include `dryRun=All`, `fieldManager`, `fieldValidation=Strict`, and no `force=`.
- Add request-time conflict coverage: fake a 409 conflict from manifest dry-run, assert canonical conflict text, no `PlanId`, no pending file, and `dry_run_failed` audit text.
- Add approved apply coverage: approve an apply-manifest plan and assert request dry-run, pre-apply dry-run, and real apply PATCHes all omit `force=`.
- Add pre-apply conflict coverage: fake a 409 conflict immediately before apply, assert no real mutation and conflict audit text.
- Extend tool schema/contract tests to assert `force` is absent from `request_apply_manifest` and `apply_approved_plan`.
- Verify with:
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`

## Assumptions
- Because KubernetesClient exposes `force` as nullable `bool?`, omitting it is preferred over passing `false`; this keeps the query parameter absent and relies on Kubernetes’ non-force default.
- Existing audit events are sufficient as long as the conflict message is recorded.
- The optional future force-apply flow is out of scope for this implementation.
- No README/doc update is required because current docs describe SSA but do not document forced ownership behavior.
