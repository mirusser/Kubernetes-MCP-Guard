# K8s Policy Validator

Static analysis that blocks dangerous Kubernetes manifests before a plan is created and again at apply time.

---

## Purpose

The Kubernetes API allows manifests that are syntactically valid but operationally dangerous — privileged containers, host-path volumes, NodePort services, and so on. Kind allow-listing (enforced by `K8sManifestParser`) is the first gate; the policy validator is the second.

Policy runs twice:
1. **At plan creation** (`RequestApplyManifestAsync`) — gives early feedback to the model and prevents plans containing unsafe manifests.
2. **At apply time** (`ApplyManifestPlanAsync`) — closes the tamper vector. A pending-plan file that has been edited to remove a policy-violating object after creation is caught here.

Delete plans are intentionally not policy-checked; a delete manifest identifies what to remove, not what to run.

---

## Architecture

```
K8sPolicyOptions      — configuration flags, all on by default; K8sPolicyOptions.Default is used at all call sites
K8sPolicySeverity     — Deny | Warning
K8sPolicyFinding      — one finding: Severity, Code, ObjectRef, Message
K8sPolicyResult       — list of findings + IsDenied + FormatRefusal() + FormatWarnings()
K8sPolicyValidator    — static entry point: Validate(objects, options) -> K8sPolicyResult
K8sConventions.PolicyCodes — string constants for each rule code
```

All types are `internal` to `InfraGate.McpServer`.

---

## Call Chain

### Plan creation

```
RequestApplyManifestAsync
  -> ValidateNamespace
  -> K8sManifestParser.ParseSupported          // kind allow-list
  -> K8sPolicyValidator.Validate               // policy check
  -> if IsDenied: return refusal text to model
  -> CreatePlan
  -> CreateAndFormatPlanAsync                  // appends warnings to plan response if any
```

### Apply

```
ApplyManifestPlanAsync
  -> K8sManifestParser.ParseSupported          // re-parse from stored manifest
  -> SameObjects check                         // tamper check on object refs
  -> K8sPolicyValidator.Validate               // re-run policy
  -> if IsDenied: return ApplyResult.Failed
  -> ApplyObjectAsync (per object)
```

---

## Severity Levels

| Severity | Effect |
|---|---|
| `Deny` | Blocks plan creation; blocks apply. Model receives a refusal message listing all denied findings. |
| `Warning` | Plan creation proceeds; warnings are appended to the plan response text returned to the model. Apply is not blocked by warnings. |

---

## Rules Reference

### DEPLOYMENT_PRIVILEGED_CONTAINER

**Object:** `apps/v1 Deployment`  
**Field:** `spec.template.spec.containers[*].securityContext.privileged`  
**Severity:** Deny  
**Why:** Privileged containers have full access to the host kernel. AI-generated app deployments have no legitimate need for this.  
**Trigger:**
```yaml
containers:
  - name: app
    securityContext:
      privileged: true
```

---

### DEPLOYMENT_HOST_PATH

**Object:** `apps/v1 Deployment`  
**Field:** `spec.template.spec.volumes[*].hostPath`  
**Severity:** Deny  
**Why:** hostPath mounts expose node filesystem paths to the container. This can leak sensitive node data or be used for container escapes.  
**Trigger:**
```yaml
volumes:
  - name: host-vol
    hostPath:
      path: /etc
```

---

### DEPLOYMENT_HOST_NAMESPACE

**Object:** `apps/v1 Deployment`  
**Fields:** `spec.template.spec.hostNetwork`, `hostPID`, `hostIPC`  
**Severity:** Deny  
**Why:** These flags break container isolation by sharing the node's network, process, or IPC namespace.  
**Trigger:**
```yaml
spec:
  hostNetwork: true   # or hostPID: true or hostIPC: true
```

---

### DEPLOYMENT_ADDED_CAPABILITIES

**Object:** `apps/v1 Deployment`  
**Field:** `spec.template.spec.containers[*].securityContext.capabilities.add`  
**Severity:** Deny  
**Why:** Added capabilities such as `SYS_ADMIN` or `NET_ADMIN` dramatically increase the blast radius of a compromised container.  
**Trigger:**
```yaml
securityContext:
  capabilities:
    add:
      - SYS_ADMIN
```

---

### IMAGE_LATEST_TAG

**Object:** `apps/v1 Deployment` (containers and initContainers)  
**Field:** `spec.template.spec.containers[*].image`  
**Severity:** Deny  
**Why:** Unpinned images allow silent upgrades that change behavior without review. Applied to: empty image, no `:` separator (implicit latest), or explicit `:latest` suffix.  
**Trigger:**
```yaml
image: nginx          # implicit latest
image: nginx:latest   # explicit latest
```
**Allowed:**
```yaml
image: nginx:1.27-alpine
```

---

### SERVICE_LOAD_BALANCER

**Object:** `v1 Service`  
**Field:** `spec.type`  
**Severity:** Deny  
**Why:** LoadBalancer services provision external cloud load balancers, which may expose workloads beyond the intended cluster boundary and incur cost.  
**Trigger:**
```yaml
spec:
  type: LoadBalancer
```

---

### SERVICE_NODE_PORT

**Object:** `v1 Service`  
**Field:** `spec.type`  
**Severity:** Deny  
**Why:** NodePort bypasses standard Service/network policy expectations and binds directly on node ports, which may conflict with other workloads.  
**Trigger:**
```yaml
spec:
  type: NodePort
```

---

### CONFIG_MAP_SECRET_LIKE_KEY

**Object:** `v1 ConfigMap`  
**Field:** `data` (key names)  
**Severity:** Warning  
**Why:** ConfigMaps are not encrypted. Keys named `password`, `token`, `apikey`, `connectionstring`, etc. are likely secrets that should live in a `Secret` resource.  
**Checked key patterns (case-insensitive, substring match):**
`password`, `passwd`, `pwd`, `secret`, `token`, `apikey`, `api_key`, `private_key`, `connectionstring`, `connection_string`  
**Trigger:**
```yaml
data:
  password: hunter2
```
**Finding:**
```
[CONFIG_MAP_SECRET_LIKE_KEY] Key 'password' looks like a secret. Use a Secret resource instead.
```

---

## K8sPolicyOptions Reference

| Property | Default | Controls |
|---|---|---|
| `DenyPrivilegedContainers` | `true` | `DEPLOYMENT_PRIVILEGED_CONTAINER` rule |
| `DenyHostPathVolumes` | `true` | `DEPLOYMENT_HOST_PATH` rule |
| `DenyHostNetwork` | `true` | hostNetwork check in `DEPLOYMENT_HOST_NAMESPACE` |
| `DenyHostPid` | `true` | hostPID check in `DEPLOYMENT_HOST_NAMESPACE` |
| `DenyHostIpc` | `true` | hostIPC check in `DEPLOYMENT_HOST_NAMESPACE` |
| `DenyAddedCapabilities` | `true` | `DEPLOYMENT_ADDED_CAPABILITIES` rule |
| `DenyLatestImageTag` | `true` | `IMAGE_LATEST_TAG` rule |
| `DenyServiceTypeNodePort` | `true` | `SERVICE_NODE_PORT` rule |
| `DenyServiceTypeLoadBalancer` | `true` | `SERVICE_LOAD_BALANCER` rule |
| `WarnOnConfigMapSecretLikeKeys` | `true` | `CONFIG_MAP_SECRET_LIKE_KEY` rule |

`K8sPolicyOptions.Default` is used at all call sites. No env-var configuration for the demo.

---

## Extending the Validator

1. Add a new `bool` property to `K8sPolicyOptions` (default `true` for Deny, `true` for Warning).
2. Add a new `const string` to `K8sConventions.PolicyCodes`.
3. Add the check to the appropriate `Validate*` private method in `K8sPolicyValidator` (or create a new one for a new object type).
4. Add a test in `K8sPolicyValidatorTests` following the `Validate_State_ExpectedResult` naming pattern, using an inline YAML manifest constant.

---

## What Is Not Checked

- **Delete plans** — a delete manifest names objects to remove, not to run. Privileged-container checks on a delete manifest are false positives.
- **Secret objects** — Secret reads are out of scope (see security roadmap section 23.3).
- **Cluster-scoped resources** — ClusterRole, Namespace, CRD mutations are out of scope.
- **ConfigMap values** — only key names are checked; value-pattern matching (JWT shape, private key headers) is future work.
- **Image registry allow-lists** — planned for a future policy option.

---

## Future Work

- PR 4: Embed `K8sPolicyFinding[]` in `K8sPlan`, include in hash, render in approval UI.
- PR 4: Audit event for policy denial (`plan_policy_denied`).
- PR 11: Log and data redaction — extend `WarnOnConfigMapSecretLikeKeys` to also check values against secret-shape patterns.
- Future: `AllowedImageRegistries` option for registry allow-listing.
