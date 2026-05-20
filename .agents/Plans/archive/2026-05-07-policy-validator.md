# Policy Validator — Implementation Plan

**Source:** `minimum-for-demo.md` item 1, cross-referenced with `security-roadmap.md` section 2 (PR 3) and section 2.4.

**Goal:** Block dangerous manifests before plan creation and again at apply time (tamper prevention). Surface policy warnings to the model in the plan response text.

---

## Confirmed Decisions

- `K8sPolicyOptions.Default` used at call sites — no env-var configuration for the demo.
- Policy types are `internal` to `InfraGate.McpServer`; not shared with `InfraGate.Approvals`.
- `K8sPolicyFinding[]` are **not** embedded in `K8sPlan` yet (that is PR 4 — requires hash + UI changes).
- **Delete plans are not policy-checked** — a delete manifest identifies what to remove, not what to create. Running a privileged-container check on a delete manifest is a false positive.
- `ConfigMap` secret-like keys are `Warning` (not `Deny`) and **are surfaced** in the plan response text returned to the model.
- No `JsonPath` field in findings — Code + ObjectRef + Message is sufficient for the demo.

---

## Files to Create

All new files live in a new folder: `src/InfraGate.McpServer/Policy/`

### `K8sPolicySeverity.cs`

```csharp
namespace InfraGate.McpServer.Policy;

internal enum K8sPolicySeverity { Warning, Deny }
```

### `K8sPolicyFinding.cs`

```csharp
namespace InfraGate.McpServer.Policy;

internal sealed record K8sPolicyFinding(
    K8sPolicySeverity Severity,
    string Code,
    string ObjectRef,   // e.g. "apps/v1 Deployment demo/nginx"
    string Message);
```

### `K8sPolicyResult.cs`

```csharp
namespace InfraGate.McpServer.Policy;

internal sealed record K8sPolicyResult(IReadOnlyList<K8sPolicyFinding> Findings)
{
    public bool IsDenied => Findings.Any(f => f.Severity == K8sPolicySeverity.Deny);

    public string FormatRefusal() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == K8sPolicySeverity.Deny)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));

    public string FormatWarnings() =>
        string.Join(Environment.NewLine, Findings
            .Where(f => f.Severity == K8sPolicySeverity.Warning)
            .Select(f => $"  [{f.Code}] {f.Message} ({f.ObjectRef})"));
}
```

### `K8sPolicyOptions.cs`

```csharp
namespace InfraGate.McpServer.Policy;

internal sealed record K8sPolicyOptions
{
    public bool DenyPrivilegedContainers { get; init; } = true;
    public bool DenyHostPathVolumes { get; init; } = true;
    public bool DenyHostNetwork { get; init; } = true;
    public bool DenyHostPid { get; init; } = true;
    public bool DenyHostIpc { get; init; } = true;
    public bool DenyAddedCapabilities { get; init; } = true;
    public bool DenyLatestImageTag { get; init; } = true;
    public bool DenyServiceTypeNodePort { get; init; } = true;
    public bool DenyServiceTypeLoadBalancer { get; init; } = true;
    public bool WarnOnConfigMapSecretLikeKeys { get; init; } = true;

    public static K8sPolicyOptions Default { get; } = new();
}
```

### `K8sPolicyValidator.cs`

Static class. Entry point: `Validate(IReadOnlyList<IKubernetesObject<V1ObjectMeta>>, K8sPolicyOptions) -> K8sPolicyResult`

Dispatch per object type to three private helpers:

**`ValidateDeployment(V1Deployment, K8sPolicyOptions, List<K8sPolicyFinding>)`**

Iterates `spec.template.spec.containers` and `spec.template.spec.initContainers` (null-safe) for:
- `DenyPrivilegedContainers`: `container.SecurityContext?.Privileged == true`
- `DenyAddedCapabilities`: `container.SecurityContext?.Capabilities?.Add?.Count > 0`
- `DenyLatestImageTag`: image is null, empty, has no `:` separator (implicit latest), or ends with `:latest`

Iterates `spec.template.spec.volumes` (null-safe) for:
- `DenyHostPathVolumes`: `volume.HostPath != null`

Pod-spec level:
- `DenyHostNetwork`: `spec.template.spec.HostNetwork == true`
- `DenyHostPid`: `spec.template.spec.HostPID == true`
- `DenyHostIpc`: `spec.template.spec.HostIPC == true`

**`ValidateService(V1Service, K8sPolicyOptions, List<K8sPolicyFinding>)`**
- `DenyServiceTypeLoadBalancer`: `spec.Type == "LoadBalancer"`
- `DenyServiceTypeNodePort`: `spec.Type == "NodePort"`

**`ValidateConfigMap(V1ConfigMap, K8sPolicyOptions, List<K8sPolicyFinding>)`**
- `WarnOnConfigMapSecretLikeKeys`: check each data key (case-insensitive) against:
  `["password","passwd","pwd","secret","token","apikey","api_key","private_key","connectionstring","connection_string"]`

ObjectRef format helper: `$"{obj.ApiVersion} {obj.Kind} {obj.Metadata.NamespaceProperty}/{obj.Metadata.Name}"`

Policy codes come from `K8sConventions.PolicyCodes` (see below) — no magic strings inside the validator.

---

## Files to Modify

### `src/InfraGate.McpServer/K8sConventions.cs`

Add a new nested static class `PolicyCodes`:

```csharp
public static class PolicyCodes
{
    public const string DeploymentPrivilegedContainer = "DEPLOYMENT_PRIVILEGED_CONTAINER";
    public const string DeploymentHostPath            = "DEPLOYMENT_HOST_PATH";
    public const string DeploymentHostNamespace       = "DEPLOYMENT_HOST_NAMESPACE";
    public const string DeploymentAddedCapabilities   = "DEPLOYMENT_ADDED_CAPABILITIES";
    public const string ImageLatestTag                = "IMAGE_LATEST_TAG";
    public const string ServiceLoadBalancer           = "SERVICE_LOAD_BALANCER";
    public const string ServiceNodePort               = "SERVICE_NODE_PORT";
    public const string ConfigMapSecretLikeKey        = "CONFIG_MAP_SECRET_LIKE_KEY";
}
```

### `src/InfraGate.McpServer/K8sManager.Requests.cs`

Surgical edit in `RequestApplyManifestAsync`, after the `ParseSupported` try/catch, before `CreatePlan`:

```csharp
var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
if (policyResult.IsDenied)
{
    return Task.FromResult(
        $"Manifest rejected by policy:{Environment.NewLine}{policyResult.FormatRefusal()}");
}
```

Surface warnings in the plan response by appending to `CreateAndFormatPlanAsync` output:
```
Policy warnings:
  [CONFIG_MAP_SECRET_LIKE_KEY] Key 'password' looks like a secret. (v1 ConfigMap demo/demo-config)
```

Pass `policyResult` into `CreateAndFormatPlanAsync` (or return it alongside the plan) so warnings can be appended to the formatted response string.

### `src/InfraGate.McpServer/K8sManager.Apply.cs`

Surgical edit in `ApplyManifestPlanAsync`, after the re-parse and `SameObjects` check, before `ApplyObjectAsync`:

```csharp
var policyResult = K8sPolicyValidator.Validate(parsed.Objects, K8sPolicyOptions.Default);
if (policyResult.IsDenied)
{
    return ApplyResult.Failed(
        $"Apply refused by policy (re-validated at apply time):{Environment.NewLine}{policyResult.FormatRefusal()}");
}
```

This closes the tamper vector: editing the pending plan file cannot bypass policy.

---

## Test File to Create

`tests/InfraGate.McpServer.Tests/UnitTests/K8sPolicyValidatorTests.cs`

Tests call `K8sManifestParser.ParseSupported` + `K8sPolicyValidator.Validate` together — the same path as the real code. Each test uses an inline YAML constant. Test names follow `Method_State_ExpectedResult`.

| Test | Rule | Expected |
|---|---|---|
| `Validate_PrivilegedContainer_IsDenied` | privileged=true | Deny, DEPLOYMENT_PRIVILEGED_CONTAINER |
| `Validate_NonPrivilegedContainer_IsAllowed` | privileged=false/omitted | no deny |
| `Validate_HostPathVolume_IsDenied` | hostPath volume | Deny, DEPLOYMENT_HOST_PATH |
| `Validate_EmptyDirVolume_IsAllowed` | emptyDir volume | no deny |
| `Validate_HostNetwork_IsDenied` | hostNetwork=true | Deny, DEPLOYMENT_HOST_NAMESPACE |
| `Validate_HostPid_IsDenied` | hostPID=true | Deny, DEPLOYMENT_HOST_NAMESPACE |
| `Validate_HostIpc_IsDenied` | hostIPC=true | Deny, DEPLOYMENT_HOST_NAMESPACE |
| `Validate_AddedCapabilities_IsDenied` | capabilities.add=[SYS_ADMIN] | Deny, DEPLOYMENT_ADDED_CAPABILITIES |
| `Validate_DroppedCapabilitiesOnly_IsAllowed` | capabilities.drop=[ALL] | no deny |
| `Validate_LatestImageTag_IsDenied` | image: nginx:latest | Deny, IMAGE_LATEST_TAG |
| `Validate_ImplicitLatestImage_IsDenied` | image: nginx (no tag) | Deny, IMAGE_LATEST_TAG |
| `Validate_PinnedImageTag_IsAllowed` | image: nginx:1.27-alpine | no deny |
| `Validate_LoadBalancerService_IsDenied` | type: LoadBalancer | Deny, SERVICE_LOAD_BALANCER |
| `Validate_NodePortService_IsDenied` | type: NodePort | Deny, SERVICE_NODE_PORT |
| `Validate_ClusterIPService_IsAllowed` | type: ClusterIP | no deny |
| `Validate_OmittedServiceType_IsAllowed` | no type field | no deny |
| `Validate_ConfigMapSecretLikeKey_IsWarned` | key: password | Warning only, CONFIG_MAP_SECRET_LIKE_KEY, not Deny |
| `Validate_SafeMinimalDeployment_HasNoFindings` | safe deployment | empty findings |

---

## Documentation to Create

### `src/InfraGate.McpServer/Policy/README.md`

Verbose documentation covering:

1. **Purpose** — what the policy validator is and why it exists
2. **Architecture** — how the types relate and where `K8sPolicyValidator.Validate` is called
3. **Call chain** — where policy runs in the request and apply flows (with code pointers)
4. **Severity levels** — `Deny` vs `Warning` and what each means for the flow
5. **Rules reference** — one section per rule:
   - Rule name and policy code
   - What it checks and on which Kubernetes object field
   - Why it exists (security rationale)
   - Example manifest that triggers it
   - Example finding output
6. **`K8sPolicyOptions` reference** — each flag, its default, and what it controls
7. **Extending the validator** — how to add a new rule (field to check → finding → test)
8. **What is not checked** — intentional gaps and why (e.g. delete plans, Secret reads, cluster-scoped resources)
9. **Future work** — link to roadmap sections: embedding findings in plan hash (PR 4), approval UI rendering (PR 4), log/data redaction (PR 11)

---

## Verify Step

```bash
dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj
```

All existing tests must continue to pass. The new test class adds 18 new passing tests. No changes to `ApprovalStore`, `K8sManifestParser` public API, or any tool names.
