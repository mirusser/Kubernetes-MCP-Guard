# Kubernetes-MCP-Guard Security Implementation Roadmap

**Purpose:** convert the current security review findings into an actionable implementation plan.

---

## 0. Executive Summary

Kubernetes-MCP-Guard already has a strong security direction:

- OAuth-protected MCP gateway.
- Namespace allow-listing.
- Kubernetes RBAC as the hard boundary.
- Approval-gated mutation flow.
- SHA-256-bound pending plans.
- Browser approval separate from the AI/MCP channel.
- Prompt-injection scanning/redaction.
- Bounded logs/events.
- No exec/attach/port-forward/raw shell passthrough.

The next step is to move from **security-aware prototype** to **credible safety gateway**.

The highest-value missing pieces are:

1. A real Kubernetes manifest policy layer.
2. Server-side dry-run and diff before approval.
3. Safer server-side apply defaults.
4. Production-mode fail-closed startup checks.
5. Better approval-state durability/concurrency.
6. Less sensitive model-visible output.
7. Stronger handling of prompt-injection findings for mutation flows.
8. Finer-grained OAuth scopes.
9. Log/data redaction.
10. Expanded threat-model and security tests.

The project should **not** try to compete with broad Kubernetes MCP servers on tool count. Its stronger niche is:

> A security gateway for AI-assisted Kubernetes operations, focused on identity, policy, human approval, plan integrity, and auditability before cluster mutation.

---

## 1. Current Security Model: Keep These Foundations

These pieces should remain central.

### 1.1 Kubernetes RBAC remains the hard boundary

**Why keep it**

The gateway must not become the primary privilege boundary for Kubernetes. The Kubernetes API server should remain the final authority for whether the ServiceAccount or kubeconfig identity can perform an action.

**Implementation rule**

Every new tool must be designed so that:

- Kubernetes RBAC can independently deny the operation.
- Gateway checks are defense-in-depth, not a replacement for RBAC.
- Required Kubernetes verbs are documented in `docs/tool-permissions.md`.
- Demo RBAC stays namespace-scoped by default.

**Acceptance criteria**

- Every new mutation tool has a documented RBAC row.
- Integration tests prove the operation fails when Kubernetes RBAC denies it.
- No tool requires `cluster-admin` for the demo path.

---

### 1.2 OAuth/JWT gateway enforcement remains mandatory

**Why keep it**

MCP clients should not directly reach mutation tools without authenticated gateway enforcement. Tool annotations such as `ReadOnly` and `Destructive` are useful UX hints, but they must not be treated as authorization.

**Implementation rule**

- Continue validating issuer, audience, lifetime, signature, and required scope.
- Add finer-grained scopes later, but keep gateway auth mandatory for all tools.
- Keep static bearer tokens unsupported.

**Acceptance criteria**

- Unauthenticated calls fail.
- Authenticated calls without the required scope fail.
- Tests cover missing scope, wrong audience, expired token, wrong issuer, and invalid signature.

---

### 1.3 Approval-gated mutation remains the main differentiator

**Why keep it**

The most important security property is that the AI can request a mutation but cannot directly authorize final execution. Approval must happen outside the MCP/model channel.

**Implementation rule**

- `request_*` tools create plans.
- `apply_approved_plan` executes only after browser approval.
- Approval must bind to a plan hash.
- Approval must be single-use and time-limited.
- Browser approval must render server-side plan data, not text supplied by the AI.

**Acceptance criteria**

- Applying without approval is denied.
- Reusing an approval is denied.
- Expired approvals are denied.
- Mutated pending-plan content causes a hash mismatch and is denied.
- Browser approval by the wrong user is denied in same-subject mode.

---

## 2. P0 Implementation: Deep Manifest Policy Validator

### 2.1 Problem

The current manifest parser allows only selected kinds, which is good but not enough. A `Deployment` can still be dangerous even when its kind is allowed.

Examples:

- Privileged container.
- `hostPath` volume.
- `hostNetwork`, `hostPID`, or `hostIPC`.
- Added Linux capabilities such as `SYS_ADMIN`.
- ServiceAccount token mounting.
- Untrusted container image.
- `latest` image tag.
- `NodePort` or `LoadBalancer` service exposure.
- ConfigMap containing secrets by mistake.

Kind allow-listing should be treated as a **first gate**, not the policy engine.

### 2.2 Goal

Add a policy validator that analyzes parsed Kubernetes objects before a plan is created and again before apply.

### 2.3 Suggested design

Add a new folder:

```text
src/InfraGate.McpServer/Policy/
  K8sPolicyOptions.cs
  K8sPolicyValidator.cs
  K8sPolicyContext.cs
  K8sPolicyResult.cs
  K8sPolicyFinding.cs
  K8sPolicySeverity.cs
  Rules/
    DeploymentSecurityPolicy.cs
    ServiceExposurePolicy.cs
    ConfigMapDataPolicy.cs
    ImagePolicy.cs
```

Suggested records:

```csharp
public sealed record K8sPolicyOptions
{
    public bool EnforceRunAsNonRoot { get; init; } = true;
    public bool RequireReadOnlyRootFilesystem { get; init; } = false;
    public bool DenyPrivilegedContainers { get; init; } = true;
    public bool DenyHostNetwork { get; init; } = true;
    public bool DenyHostPid { get; init; } = true;
    public bool DenyHostIpc { get; init; } = true;
    public bool DenyHostPathVolumes { get; init; } = true;
    public bool DenyHostPorts { get; init; } = true;
    public bool DenyAddedCapabilities { get; init; } = true;
    public bool DenyLatestImageTag { get; init; } = true;
    public bool RequireImageDigest { get; init; } = false;
    public string[] AllowedImageRegistries { get; init; } = [];
    public string[] AllowedServiceAccounts { get; init; } = [];
    public bool DenyServiceTypeLoadBalancer { get; init; } = true;
    public bool DenyServiceTypeNodePort { get; init; } = true;
    public bool WarnOnConfigMapSecretLikeValues { get; init; } = true;
}
```

```csharp
public sealed record K8sPolicyFinding(
    K8sPolicySeverity Severity,
    string Code,
    string ObjectRef,
    string JsonPath,
    string Message,
    string Recommendation);

public enum K8sPolicySeverity
{
    Info,
    Warning,
    Deny
}

public sealed record K8sPolicyResult(IReadOnlyList<K8sPolicyFinding> Findings)
{
    public bool IsDenied => Findings.Any(f => f.Severity == K8sPolicySeverity.Deny);
}
```

### 2.4 Where to call it

Call policy validation immediately after manifest parsing:

```text
RequestApplyManifestAsync
  -> ValidateNamespace
  -> K8sManifestParser.ParseSupported
  -> K8sPolicyValidator.Validate
  -> if denied: return refusal summary
  -> CreatePlan with policy findings embedded

ApplyApprovedPlanAsync
  -> Load approved plan
  -> Reparse manifest
  -> Re-run K8sPolicyValidator
  -> Refuse if denied
  -> Continue to dry-run/apply
```

Policy must be re-run at apply time because pending files or state may be tampered with, and because policy options may have changed since plan creation.

### 2.5 Rules to implement first

#### Rule: deny privileged containers

**Why**

Privileged containers can access host-level resources and are rarely acceptable for AI-generated app deployments.

**Check**

For every container and initContainer:

```text
securityContext.privileged == true
```

**Finding**

```text
Severity: Deny
Code: DEPLOYMENT_PRIVILEGED_CONTAINER
Path: spec.template.spec.containers[i].securityContext.privileged
Message: Privileged containers are not allowed.
```

---

#### Rule: deny privilege escalation

**Why**

`allowPrivilegeEscalation: true` increases blast radius if the process or image is compromised.

**Check**

```text
securityContext.allowPrivilegeEscalation == true
```

Optionally warn if it is omitted, depending on your strictness level.

---

#### Rule: deny host namespace sharing

**Why**

`hostNetwork`, `hostPID`, and `hostIPC` break container isolation assumptions.

**Check**

```text
spec.template.spec.hostNetwork == true
spec.template.spec.hostPID == true
spec.template.spec.hostIPC == true
```

---

#### Rule: deny hostPath volumes

**Why**

`hostPath` can expose node filesystem paths to the workload.

**Check**

```text
spec.template.spec.volumes[*].hostPath != null
```

---

#### Rule: deny host ports

**Why**

`hostPort` bypasses normal Service/network policy expectations and can collide with node ports.

**Check**

```text
containers[*].ports[*].hostPort != null
```

---

#### Rule: deny added Linux capabilities

**Why**

Capabilities such as `SYS_ADMIN`, `NET_ADMIN`, `SYS_PTRACE`, and `DAC_READ_SEARCH` can dramatically increase blast radius.

**Check**

```text
securityContext.capabilities.add has any item
```

Start strict: deny any added capability. Later allow an explicit allow-list if needed.

---

#### Rule: image policy

**Why**

AI-generated manifests often use vague images. Unpinned or untrusted images are one of the easiest paths to unsafe cluster changes.

**Checks**

- Deny empty image.
- Deny implicit `latest`.
- Deny explicit `:latest`.
- Optionally require digest pinning with `@sha256:`.
- Optionally allow only configured registries.

**Examples**

Deny:

```text
nginx
nginx:latest
docker.io/library/nginx:latest
unknown.example.com/app:v1
```

Allow under strict policy:

```text
ghcr.io/example/app@sha256:...
123456789012.dkr.ecr.eu-west-1.amazonaws.com/app@sha256:...
```

---

#### Rule: Service exposure

**Why**

A `Service` can expose workloads beyond what the user expects.

**Checks**

Deny or require special approval for:

```text
spec.type == NodePort
spec.type == LoadBalancer
```

For the first implementation, deny both unless explicitly allowed in configuration.

---

#### Rule: ConfigMap secret-like content

**Why**

ConfigMaps are not Secrets, but users and AI-generated manifests often place passwords, tokens, keys, and connection strings in them.

**Check keys**

Warn on key names containing:

```text
password
passwd
pwd
secret
token
apikey
api_key
private_key
connectionstring
connection_string
```

**Check values**

Warn or redact on values matching common secret shapes:

```text
-----BEGIN PRIVATE KEY-----
AKIA[0-9A-Z]{16}
eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.
(?i)password\s*=
(?i)secret\s*=
```

Start with warnings. Do not block all ConfigMaps immediately because false positives are likely.

### 2.6 Plan data changes

Extend `K8sPlan` with policy information:

```csharp
public sealed record K8sPlan(
    string Id,
    string Operation,
    string Namespace,
    DateTimeOffset CreatedAtUtc,
    string Description,
    Dictionary<string, string> Parameters,
    K8sObjectRef[] Objects,
    string? Manifest,
    K8sPolicyFinding[] PolicyFindings,
    string PolicyProfile);
```

Include policy findings in:

- Pending plan file.
- Approval UI.
- Audit events.
- Hash computation.

### 2.7 Acceptance criteria

- Dangerous manifests are rejected before plan creation.
- Policy findings are visible in browser approval.
- Policy findings are included in the plan hash.
- Policy is re-run before apply.
- Apply is denied if policy now rejects the plan.
- Unit tests cover every rule.
- Integration tests prove denied manifests do not reach Kubernetes API mutation calls.

---

## 3. P0 Implementation: Server-Side Dry-Run Before Approval

### 3.1 Problem

Static validation is not enough. Kubernetes admission controllers, schema validation, quotas, RBAC, defaulting, and API-server validation can still reject the change.

Without dry-run, the browser approval may approve something that fails only during apply.

### 3.2 Goal

Before creating an approval plan, ask Kubernetes what would happen using server-side dry-run.

### 3.3 Suggested design

Add:

```text
src/InfraGate.McpServer/DryRun/
  K8sDryRunService.cs
  K8sDryRunResult.cs
```

```csharp
public sealed record K8sDryRunResult(
    bool Succeeded,
    string Message,
    IKubernetesObject[] ServerObjects,
    K8sObjectRef[] ObjectRefs);
```

For apply plans:

```text
RequestApplyManifestAsync
  -> parse
  -> policy validate
  -> server-side dry-run apply with dryRun=All
  -> store returned server-side objects/defaulted output
  -> create plan
```

For delete plans:

```text
RequestDeleteManifestAsync
  -> parse
  -> confirm each object exists or mark missing
  -> optionally dry-run delete if client/API supports it
  -> create plan
```

### 3.4 How to implement

Use Kubernetes API dry-run support by passing `dryRun=All` to the Kubernetes client method overload.

If the typed client overload does not expose dry-run cleanly for server-side apply in your current client version, use one of these approaches:

1. Check for overloads that include a `dryRun` parameter.
2. Use the generated client method with optional parameters.
3. Use the lower-level generic Kubernetes client request API.
4. As a temporary fallback, do a non-mutating read plus schema validation, but document that this is not a true dry-run.

Do not silently skip dry-run in production mode.

### 3.5 Store dry-run output

The dry-run result should be stored in the plan and included in the plan hash.

Store:

- Whether dry-run succeeded.
- API-server message.
- Server-defaulted manifest or normalized JSON.
- Warnings returned by the API server.
- Object references.

### 3.6 Acceptance criteria

- Plan creation fails if server-side dry-run fails.
- Browser approval page shows dry-run status.
- Audit event records dry-run failure.
- Tests cover invalid schema, unsupported field, namespace mismatch, and RBAC denial.
- Production mode refuses apply plans if dry-run could not be performed.

---

## 4. P0 Implementation: Diff Before Approval

### 4.1 Problem

Humans should approve the actual change, not just a raw manifest. A manifest can be long and difficult to understand.

### 4.2 Goal

Show a clear diff between the live object and the proposed server-side-dry-run object.

### 4.3 Suggested design

Add:

```text
src/InfraGate.McpServer/Diff/
  K8sDiffService.cs
  K8sDiffResult.cs
  K8sObjectNormalizer.cs
```

```csharp
public sealed record K8sDiffResult(
    K8sObjectRef Object,
    bool Exists,
    string Summary,
    string UnifiedDiff,
    string[] AddedPaths,
    string[] RemovedPaths,
    string[] ChangedPaths);
```

### 4.4 How to build the diff

For each object:

1. Read the live object from Kubernetes.
2. Normalize live object.
3. Normalize dry-run object.
4. Remove noisy fields.
5. Produce:
   - Human summary.
   - Unified YAML diff.
   - Optional JSON path changes.

Fields to remove before diff:

```text
metadata.managedFields
metadata.resourceVersion
metadata.uid
metadata.creationTimestamp
metadata.generation
metadata.annotations["kubectl.kubernetes.io/last-applied-configuration"]
status
```

For new objects, show:

```text
Object does not exist. This plan will create it.
```

For missing delete targets, show:

```text
Object does not exist. Delete will be a no-op or refused depending on policy.
```

### 4.5 Approval UI requirements

The browser approval page should show:

- Operation.
- Namespace.
- Requesting subject.
- Created time.
- Expiry time.
- Object list.
- Policy findings.
- Dry-run result.
- Diff.
- Risk level.
- Approve button.
- Deny button.

### 4.6 Hash binding

Include the diff and dry-run normalized output in the plan hash, or include the exact inputs that produce them.

Best option:

```text
hash = SHA-256(canonical-json(plan-core + policy-findings + dry-run-server-objects + diff-summary))
```

Do not include volatile fields such as file path or display-only timestamps.

### 4.7 Acceptance criteria

- Approval page shows a diff for updates.
- Approval page clearly shows create/delete actions.
- Diff excludes noisy metadata/status.
- Diff is hash-bound.
- Apply re-checks drift policy before writing.
- Tests cover create, update, delete, and no-op diffs.

---

## 5. P0 Implementation: Safer Server-Side Apply Defaults

### 5.1 Problem

Server-side apply with `force: true` can take field ownership from other managers. That can overwrite fields managed by humans, GitOps tools, Helm, or controllers.

For a safety-first gateway, force apply should not be the default.

### 5.2 Goal

Default to non-forcing server-side apply. Make force apply a separate, explicit, high-risk path.

### 5.3 Implementation

Change:

```csharp
force: true
```

to:

```csharp
force: false
```

in apply methods for:

- Deployment.
- Service.
- ConfigMap.

If the client method treats omitted force as false, omit it entirely.

### 5.4 Conflict behavior

When Kubernetes returns a field ownership conflict:

- Do not retry automatically with force.
- Return a clear error.
- Tell the user to re-request the plan or explicitly request a force apply if that feature is later added.
- Audit the conflict.

Example message:

```text
Apply refused by Kubernetes field ownership conflict.
The plan was not forced because force apply can take ownership of fields from another manager.
Re-request the plan after reconciling the live object, or use an explicitly approved force-apply flow if enabled.
```

### 5.5 Optional future force flow

If force apply is added later:

- Add `request_force_apply_manifest`, not a hidden flag.
- Require policy setting `AllowForceApply=true`.
- Mark risk level as high.
- Show field ownership conflict details in approval.
- Require a stronger approval mode in production.

### 5.6 Acceptance criteria

- Default apply does not pass `force: true`.
- Field ownership conflicts fail safely.
- Tests confirm force is not used by default.
- Force apply cannot be accidentally enabled through model-provided input.

---

## 6. P0 Implementation: Reduce Model-Visible Sensitive Output

### 6.1 Problem

Plan creation currently returns operational details to the MCP client. The model-visible response should be minimized because MCP clients, LLMs, logs, and transcripts may retain or expose content.

Specific issues to avoid:

- Internal filesystem paths.
- Full raw manifests.
- ConfigMap data.
- Secret-like values.
- Excessive plan internals.

### 6.2 Goal

Return only what the AI needs to continue the flow.

### 6.3 Suggested response shape

For `request_*` tools, return:

```text
PlanId: <id>
Status: pending_gateway_approval
Operation: apply
Namespace: mcp-nginx-demo
Objects:
 - apps/v1 Deployment mcp-nginx-demo/nginx
Policy: passed_with_1_warning
Risk: medium
Next step: call apply_approved_plan with this PlanId.
Browser approval will show the full server-rendered plan, policy findings, dry-run result, and diff.
```

Do not return:

```text
Pending file: ...
Manifest:
  [yaml]
    ...

```

### 6.4 Approval UI can show more detail

The browser approval page can show the full normalized manifest and diff because:

- It requires browser authentication.
- It is outside the model channel.
- It is the human review surface.

Even there, consider redacting ConfigMap values by default and adding a “reveal values” action for authenticated users.

### 6.5 Acceptance criteria

- MCP response does not include internal paths.
- MCP response does not echo full manifests.
- MCP response does not expose ConfigMap values.
- Existing guardrail manifest-redaction remains as a fallback, not the primary protection.
- Tests assert response text does not contain `Pending file:` or raw manifest blocks.

---

## 7. P0 Implementation: Production Mode Fail-Closed Checks

### 7.1 Problem

Development-friendly defaults can become production incidents if copied into a real environment.

Examples:

- DevIssuer accidentally used outside localhost.
- `RequireHttpsMetadata=false`.
- Kubeconfig fallback to the developer's current context.
- Default namespace allow-list.
- Weak approval/audit directory permissions.
- HTTP endpoints in shared environments.

### 7.2 Goal

Add explicit environment modes and fail closed in production.

### 7.3 Suggested config

```text
INFRA_GATE_ENVIRONMENT=Development|Production
```

or reuse standard .NET:

```text
DOTNET_ENVIRONMENT=Development|Production
ASPNETCORE_ENVIRONMENT=Development|Production
```

Add:

```csharp
public sealed record ProductionSafetyOptions
{
    public bool Enabled { get; init; }
    public bool AllowDevIssuer { get; init; } = false;
    public bool AllowHttpMetadata { get; init; } = false;
    public bool AllowDefaultKubeConfigFallback { get; init; } = false;
    public bool RequireExplicitAllowedNamespaces { get; init; } = true;
    public bool RequirePersistentApprovalStore { get; init; } = true;
}
```

### 7.4 Startup checks

In production mode, refuse startup if:

- OAuth authority is localhost DevIssuer.
- `INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA=false`.
- Gateway resource URL is localhost.
- No explicit kubeconfig or in-cluster ServiceAccount mode is configured.
- Namespace allow-list is missing or default-only without explicit confirmation.
- Approval store path is under temp/dev directory.
- Approval/audit store is not persistent.
- TLS/forwarded-header assumptions are invalid.

### 7.5 Safer Kubernetes config provider

Replace direct fallback logic with a provider:

```text
KubernetesConfigProvider
  -> if K8S_MCP_KUBECONFIG set: use it
  -> else if K8S_MCP_USE_IN_CLUSTER=true: use in-cluster config
  -> else if Development: BuildDefaultConfig()
  -> else: throw startup exception
```

### 7.6 Acceptance criteria

- Production mode fails without explicit Kubernetes auth config.
- Production mode fails if DevIssuer/HTTP metadata is configured.
- Development mode remains easy to run locally.
- Startup errors are clear and actionable.
- Tests cover all unsafe production combinations.

---

## 8. P0 Implementation: Approval Store Durability and Concurrency

### 8.1 Problem

File-backed approval state is easy to inspect and good for demos, but production needs stronger guarantees.

Risks:

- Race conditions if two apply calls run concurrently.
- Crash between API apply and applied marker write.
- Partial file writes.
- Multi-replica gateway/server cannot coordinate through local files.
- Audit log corruption or interleaving.
- No transactional state transitions.

### 8.2 Goal

Either harden the file store for single-node use or introduce a transactional store for production.

### 8.3 Short-term: hardened file store

Improve the current store with:

- Atomic writes: write to temp file, flush, rename.
- File locks per plan ID.
- Append-only audit writes with exclusive lock.
- State machine file per plan.
- Refuse invalid state transitions.
- Recovery behavior on startup.

State machine:

```text
Pending
ChallengeCreated
Approved
Denied
Expired
Applying
Applied
ApplyFailed
```

Allowed transitions:

```text
Pending -> ChallengeCreated
ChallengeCreated -> Approved
ChallengeCreated -> Denied
ChallengeCreated -> Expired
Approved -> Applying
Applying -> Applied
Applying -> ApplyFailed
ApplyFailed -> Applying only if retry policy allows
```

### 8.4 Longer-term: SQLite or PostgreSQL approval store

Add an interface:

```csharp
public interface IApprovalStore
{
    Task<CreatePlanResult> CreatePlanAsync(K8sPlan plan, CancellationToken ct);
    Task<ApprovedPlanResult> GetApprovedPlanAsync(string planId, CancellationToken ct);
    Task MarkAppliedAsync(K8sPlan plan, string expectedHash, CancellationToken ct);
    Task WriteAuditAsync(string eventType, object payload, CancellationToken ct);
}
```

Implement:

```text
FileApprovalStore
SqliteApprovalStore
PostgresApprovalStore
```

Database tables:

```text
plans
  id
  operation
  namespace
  created_at_utc
  canonical_plan_json
  plan_hash
  state
  requested_by_subject
  approved_by_subject
  approved_at_utc
  applied_at_utc

approval_challenges
  id
  plan_id
  requester_subject
  challenge_hash
  expires_at_utc
  consumed_at_utc
  state

audit_events
  id
  occurred_at_utc
  event_type
  plan_id
  subject
  payload_json
```

Use transactions for:

- Consuming approval.
- Moving `Approved -> Applying`.
- Moving `Applying -> Applied`.

### 8.5 Acceptance criteria

- Concurrent apply calls for the same plan cannot both execute.
- Crash/restart behavior is documented and tested.
- Audit writes are durable.
- Production mode can require SQLite/PostgreSQL.
- File store remains available for local demos.

---

## 9. P0 Implementation: Prompt-Injection Findings Should Affect Mutations

### 9.1 Problem

Prompt-injection scanning currently behaves mostly as warn/redact. That is acceptable for read-only troubleshooting, but mutation flows need stricter behavior.

If a model-provided manifest or tool argument contains high-risk prompt-injection text, the gateway should not simply warn and continue.

### 9.2 Goal

Make guardrail findings enforcement-aware.

### 9.3 Suggested policy

For read-only tools:

```text
request finding -> warn and audit
response finding -> redact and audit
```

For plan-creation tools:

```text
request finding severity high -> deny plan creation or require high-risk approval
request finding severity medium -> create plan with policy warning
response finding -> redact and audit
```

For `apply_approved_plan`:

```text
request finding -> deny
response finding -> redact and audit
```

### 9.4 Implementation design

Add:

```text
GuardrailEnforcementPolicy.cs
GuardrailDecision.cs
```

```csharp
public enum GuardrailDecisionAction
{
    Allow,
    Warn,
    Deny,
    RequireStepUpApproval
}
```

Decision inputs:

- Tool name.
- Tool annotation.
- Direction: request/response.
- Finding categories.
- Whether tool is mutation-related.
- Whether plan has already been browser-approved.

### 9.5 High-risk categories

Treat these as high-risk for mutation requests:

- `ignore-instructions`
- `tool-use`
- `secret-exfiltration`
- `authority-override`

### 9.6 Acceptance criteria

- Prompt-injection text in a mutation manifest does not silently proceed.
- Mutation request denial is audited.
- Read-only response redaction still works.
- Tests cover read-only allow/warn and mutation deny.

---

## 10. P0/P1 Implementation: Finer-Grained OAuth Scopes

### 10.1 Problem

A single `mcp:tools` scope is simple but coarse. Read-only diagnostics, log access, plan creation, and apply execution should not all require the same authority.

### 10.2 Goal

Split authorization by risk.

### 10.3 Suggested scopes

```text
mcp:k8s:read
mcp:k8s:logs
mcp:k8s:plan
mcp:k8s:apply
mcp:k8s:admin
```

Initial mapping:

```text
get_allowed_namespaces          -> mcp:k8s:read
get_k8s_status                  -> mcp:k8s:read
get_k8s_events                  -> mcp:k8s:read
get_k8s_resource                -> mcp:k8s:read
get_*_diagnostics               -> mcp:k8s:read
get_pod_logs                    -> mcp:k8s:logs
request_*                       -> mcp:k8s:plan
apply_approved_plan             -> mcp:k8s:apply
```

### 10.4 Backward compatibility

For local demos, allow:

```text
mcp:tools
```

as a legacy scope if:

```text
INFRA_GATE_ALLOW_LEGACY_SCOPE=true
```

In production mode, default this to false.

### 10.5 Acceptance criteria

- Read-only token cannot create plans.
- Plan token cannot apply.
- Logs require separate scope.
- Legacy `mcp:tools` can be enabled only explicitly.
- `docs/tool-permissions.md` is updated.

---

## 11. P1 Implementation: Log and Data Redaction

### 11.1 Problem

Pod logs can contain tokens, passwords, connection strings, API keys, customer data, and stack traces. Bounded size does not solve confidentiality.

### 11.2 Goal

Add optional redaction to model-visible log output and diagnostic text.

### 11.3 Suggested design

Add:

```text
src/InfraGate.McpServer/Redaction/
  SensitiveDataRedactor.cs
  SensitiveDataPattern.cs
  SensitiveDataRedactionOptions.cs
```

Default redaction patterns:

```text
AWS access key IDs
JWTs
Bearer tokens
Basic auth headers
Private keys
Connection strings
password=...
secret=...
token=...
api_key=...
```

### 11.4 Response behavior

Return:

```text
[redacted: possible secret]
```

Include summary:

```text
Redaction: 3 possible secrets were redacted from pod logs.
```

Audit only metadata, not the secret value:

```json
{
  "eventType": "log_redaction",
  "toolName": "get_pod_logs",
  "namespace": "mcp-nginx-demo",
  "pod": "example",
  "redactionCount": 3,
  "patterns": ["jwt", "password_assignment"]
}
```

### 11.5 Acceptance criteria

- Known secret-like values are redacted.
- Redaction can be disabled only in development.
- Raw secrets are never written to audit logs.
- Tests cover common secret formats.

---

## 12. P1 Implementation: Stronger Approval UI

### 12.1 Problem

Approval UI is the human safety checkpoint. It should help the user understand risk quickly.

### 12.2 Goal

Make approval review useful enough that a human can make a real decision.

### 12.3 Approval page should show

Top summary:

```text
Operation: apply
Namespace: production-api
Risk: high
Requester: user@example.com
Created: 2026-05-06T...
Expires: 2026-05-06T...
Plan hash: ...
```

Object table:

```text
apps/v1 Deployment production-api/api
v1 Service production-api/api
```

Policy section:

```text
Denied:
 - none

Warnings:
 - CONFIGMAP_SECRET_LIKE_KEY: key "ConnectionString" looks sensitive.
 - IMAGE_NOT_DIGEST_PINNED: image is tag-pinned but not digest-pinned.
```

Dry-run section:

```text
Server-side dry-run: succeeded
Admission warnings: none
```

Diff section:

```diff
- image: ghcr.io/example/api:v1
+ image: ghcr.io/example/api:v2
```

Actions:

```text
Deny
Approve
```

### 12.4 UX warnings

Use explicit warning blocks for:

- Image changes.
- Replica scale-down.
- Delete operations.
- External service exposure.
- Force apply.
- Policy warnings.
- ConfigMap sensitive values.

### 12.5 Acceptance criteria

- User can deny as well as approve.
- Policy warnings are visible before approval.
- Diff is visible before approval.
- Approval button is disabled for policy-denied plans.
- Browser page never trusts AI-provided display text.

---

## 13. P1 Implementation: Approval Modes and Separation of Duties

### 13.1 Problem

Same-subject approval is safe for demos, but production may require a different person or group to approve.

### 13.2 Goal

Support configurable approval policies.

### 13.3 Suggested modes

```text
SameSubject
AnyAuthenticatedApprover
ApproverGroup
TwoPerson
BreakGlass
```

### 13.4 Suggested config

```text
INFRA_GATE_APPROVAL_MODE=SameSubject|ApproverGroup|TwoPerson|BreakGlass
INFRA_GATE_APPROVER_GROUPS=k8s-approvers,sre
INFRA_GATE_BREAK_GLASS_GROUPS=platform-admins
```

### 13.5 Rules

SameSubject:

```text
approver == requester
```

ApproverGroup:

```text
approver has required group claim
```

TwoPerson:

```text
approver has required group claim
approver != requester
```

BreakGlass:

```text
approver has break-glass group
requires reason
extra audit event
short TTL
```

### 13.6 Acceptance criteria

- Same-subject remains default for local demo.
- Production can require two-person approval.
- Approval reason is stored for break-glass.
- Audit includes requester and approver identities.

---

## 14. P1 Implementation: Image Security Enhancements

### 14.1 Problem

Image choice is one of the most important security decisions in a Deployment.

### 14.2 Goal

Make image changes explicit, policy-controlled, and reviewable.

### 14.3 Features

- Deny `latest`.
- Warn on non-digest-pinned images.
- Allow-list registries.
- Optional deny-list registries.
- Optional cosign verification later.
- Show old and new image prominently in approval UI.
- Re-read live deployment image before applying `set-image`.

The current set-image flow already binds the current image and revalidates it before patching. Keep that behavior and extend it with image policy.

### 14.4 Acceptance criteria

- `request_set_deployment_image` denies `latest`.
- Untrusted registry is denied.
- Current-image drift causes re-plan requirement.
- Approval UI highlights image changes.
- Tests cover allowed and denied image cases.

---

## 15. P1 Implementation: Drift and Time-of-Check/Time-of-Use Policy

### 15.1 Problem

The live cluster may change between plan creation, approval, and apply.

### 15.2 Goal

Detect meaningful drift before applying.

### 15.3 Suggested design

At plan creation, store live object fingerprints for objects that already exist:

```text
apiVersion
kind
namespace
name
resourceVersion
normalized spec hash
selected metadata hash
```

At apply time:

- Re-read live objects.
- Recompute fingerprints.
- If drift exists, either:
  - deny and require re-plan; or
  - show drift in approval if detected before approval.

### 15.4 Drift policy

Default:

```text
deny_on_drift = true
```

Ignore drift in:

```text
status
metadata.resourceVersion
metadata.managedFields
metadata.generation
```

Consider meaningful drift in:

```text
spec
metadata.labels
metadata.annotations, except known noisy annotations
```

### 15.5 Acceptance criteria

- Plan is refused if live spec changed after approval.
- User gets a clear re-plan message.
- Tests cover live object changed between approval and apply.

---

## 16. P1 Implementation: Downstream Process Hardening

### 16.1 Problem

The gateway depends on a downstream MCP server process. The current design is suitable for local demos, but production needs better operational behavior.

### 16.2 Goal

Make downstream calls observable, bounded, and recoverable.

### 16.3 Tasks

- Per-call timeout.
- Max response size.
- Health check.
- Restart policy.
- Structured logs.
- Metrics for downstream latency/failures.
- Backpressure if requests queue.
- Clear failure messages.
- Optional separate deployment instead of child process.

### 16.4 Acceptance criteria

- Hung downstream calls time out.
- Oversized downstream responses are rejected/redacted.
- Gateway exposes health status.
- Metrics include success/failure/latency.

---

## 17. P1 Implementation: Audit Schema and Observability

### 17.1 Problem

Audit events exist, but production users will need a consistent schema and export path.

### 17.2 Goal

Make audit events useful for incident review and compliance.

### 17.3 Suggested audit events

```text
plan_requested
plan_policy_denied
plan_dry_run_failed
plan_created
approval_challenge_created
approval_viewed
approval_approved
approval_denied
approval_expired
approval_hash_mismatch
apply_requested
apply_denied
apply_started
apply_succeeded
apply_failed
guardrail_request_denied
guardrail_request_warned
guardrail_response_redacted
log_redacted
production_startup_refused
```

### 17.4 Common fields

```json
{
  "eventId": "uuid",
  "occurredAtUtc": "2026-05-06T12:00:00Z",
  "eventType": "plan_created",
  "planId": "...",
  "operation": "apply",
  "namespace": "mcp-nginx-demo",
  "objects": [
    {
      "apiVersion": "apps/v1",
      "kind": "Deployment",
      "namespace": "mcp-nginx-demo",
      "name": "nginx"
    }
  ],
  "subject": "user@example.com",
  "approverSubject": null,
  "clientId": "...",
  "sourceIpHash": "...",
  "planHash": "...",
  "policyProfile": "default",
  "risk": "medium",
  "result": "succeeded"
}
```

Do not log raw manifests, ConfigMap values, tokens, or pod log content by default.

### 17.5 Acceptance criteria

- Audit events are structured consistently.
- No secret-like values in audit logs.
- Audit export path is documented.
- Tests assert audit event shape for major flows.

---

## 18. P2 Implementation: Supply Chain and Deployment Hardening

### 18.1 Current good direction

The project already has CI and Docker build workflows. Keep improving this because security-oriented projects are judged by their own supply chain hygiene.

### 18.2 Suggested additions

- SBOM generation.
- Container image signing with cosign.
- Provenance/SLSA attestations.
- CodeQL or equivalent static analysis.
- Dependency review.
- Container runtime checks:
  - non-root user;
  - read-only root filesystem where possible;
  - dropped capabilities;
  - explicit writable mount only for approval/audit data.
- Helm chart or Kustomize production deployment.

### 18.3 Acceptance criteria

- Release publishes SBOM.
- Images are signed.
- Deployment manifests run as non-root.
- Security scan fails on high/critical vulnerabilities unless explicitly waived.

---

## 19. P2 Implementation: Gateway-in-Front-of-Other-MCP-Servers

### 19.1 Why consider this

Kubernetes-MCP-Guard should not try to out-feature broad Kubernetes MCP servers. A stronger long-term path is to become the security gateway that can sit in front of richer MCP backends.

### 19.2 Goal

Allow the gateway to enforce:

- OAuth.
- Scopes.
- Tool policy.
- Prompt-injection scanning.
- Human approval.
- Audit.
- Response redaction.

for downstream MCP servers.

### 19.3 Design idea

Add policy config:

```toml
[[tools]]
name = "kubernetes_apply"
risk = "high"
approval_required = true
guardrail_request_policy = "deny_on_high"
scopes = ["mcp:k8s:apply"]

[[tools]]
name = "kubernetes_get_pods"
risk = "low"
approval_required = false
scopes = ["mcp:k8s:read"]
```

The gateway can classify tools by:

- Exact name.
- Regex.
- MCP annotations.
- Argument patterns.
- Downstream server identity.

### 19.4 Acceptance criteria

- Gateway can block or approve tools from a downstream MCP server.
- Tool policies are declarative.
- Unknown destructive tools are denied by default in production.
- Read-only tools can pass through.

---

## 20. Suggested Implementation Order

### PR 1: Remove sensitive model-visible output

**Why first**

Low-risk, high-signal security improvement.

**Tasks**

- Remove pending file path from `CreateAndFormatPlanAsync`.
- Remove full manifest echo.
- Add tests.

**Expected size**

Small.

---

### PR 2: Change server-side apply default to non-force

**Why second**

Small code change with major safety value.

**Tasks**

- Change apply methods to not force.
- Add conflict handling message.
- Add tests/mocks if feasible.

**Expected size**

Small.

---

### PR 3: Add policy validator skeleton and first rules

**Why third**

This becomes the foundation for most future safety features.

**Tasks**

- Add policy result/finding types.
- Add validator.
- Implement:
  - privileged container deny;
  - hostPath deny;
  - hostNetwork/hostPID/hostIPC deny;
  - NodePort/LoadBalancer deny;
  - latest image deny.
- Call validator before plan creation.
- Add unit tests.

**Expected size**

Medium.

---

### PR 4: Add policy findings to plan and approval UI

**Why fourth**

Policy must be visible to the human.

**Tasks**

- Extend plan model.
- Hash policy findings.
- Render policy findings in approval UI.
- Add audit event for policy denial.

**Expected size**

Medium.

---

### PR 5: Add server-side dry-run

**Why fifth**

This requires API-client details and integration tests.

**Tasks**

- Add dry-run service.
- Store dry-run result.
- Fail plan on dry-run failure.
- Add integration tests.

**Expected size**

Medium/large.

---

### PR 6: Add diff before approval

**Why sixth**

Diff depends on dry-run and live reads.

**Tasks**

- Add object normalizer.
- Add diff service.
- Render diff in approval UI.
- Include diff/dry-run output in hash or derive from hash-bound data.

**Expected size**

Large.

---

### PR 7: Production mode startup checks

**Why seventh**

After core safety is improved, make unsafe deployment harder.

**Tasks**

- Add environment mode.
- Add startup validator.
- Fail closed on unsafe production config.
- Update docs.

**Expected size**

Medium.

---

### PR 8: Prompt-injection enforcement for mutation tools

**Why eighth**

Guardrails become enforcement-aware after policy/approval are stronger.

**Tasks**

- Add guardrail enforcement decisions.
- Deny high-risk findings for mutation tools.
- Add tests.

**Expected size**

Medium.

---

### PR 9: Approval store hardening

**Why ninth**

Important for production, but larger and more architectural.

**Tasks**

- Add store interface if not present.
- Harden file store.
- Add locks/state transitions.
- Add SQLite/PostgreSQL option later.

**Expected size**

Large.

---

### PR 10: Finer-grained scopes

**Why tenth**

Useful, but potentially impacts clients and docs.

**Tasks**

- Add per-tool scope mapping.
- Keep legacy scope only in dev/compat mode.
- Update docs/tests.

**Expected size**

Medium.

---

## 21. Security Test Plan

### 21.1 Policy tests

Create test manifest fixtures:

```text
tests/fixtures/policy/deny-privileged-deployment.yaml
tests/fixtures/policy/deny-hostpath-deployment.yaml
tests/fixtures/policy/deny-hostnetwork-deployment.yaml
tests/fixtures/policy/deny-capabilities-deployment.yaml
tests/fixtures/policy/deny-latest-image.yaml
tests/fixtures/policy/deny-loadbalancer-service.yaml
tests/fixtures/policy/warn-configmap-secret-like.yaml
tests/fixtures/policy/allow-minimal-safe-deployment.yaml
```

Test names:

```csharp
PolicyValidator_DeniesPrivilegedContainer()
PolicyValidator_DeniesHostPathVolume()
PolicyValidator_DeniesHostNetwork()
PolicyValidator_DeniesAddedCapabilities()
PolicyValidator_DeniesLatestImage()
PolicyValidator_DeniesLoadBalancerService()
PolicyValidator_WarnsOnSecretLikeConfigMapKey()
PolicyValidator_AllowsMinimalSafeDeployment()
```

### 21.2 Approval tests

```csharp
ApplyApprovedPlan_WithoutApproval_IsDenied()
ApplyApprovedPlan_WithExpiredApproval_IsDenied()
ApplyApprovedPlan_WithWrongSubject_IsDenied()
ApplyApprovedPlan_WithHashMismatch_IsDenied()
ApplyApprovedPlan_WithAlreadyAppliedPlan_IsDenied()
ApplyApprovedPlan_WithConcurrentCalls_OnlyAppliesOnce()
```

### 21.3 Dry-run/diff tests

```csharp
RequestApplyManifest_WhenDryRunFails_DoesNotCreatePlan()
RequestApplyManifest_WhenDryRunSucceeds_StoresServerObject()
ApprovalPage_ShowsDiffForExistingObject()
ApprovalPage_ShowsCreateForNewObject()
ApplyApprovedPlan_WhenLiveObjectDrifted_RequiresReplan()
```

### 21.4 Prompt-injection tests

```csharp
GuardedToolRunner_ReadOnlyRequestFinding_WarnsAndContinues()
GuardedToolRunner_MutationRequestFinding_Denies()
GuardedToolRunner_ResponseFinding_Redacts()
GuardedToolRunner_ApplyApprovedPlanRequestFinding_Denies()
```

### 21.5 Scope tests

```csharp
ReadScope_CanCallReadTools()
ReadScope_CannotCreatePlan()
PlanScope_CanCreatePlan()
PlanScope_CannotApply()
LogsScope_RequiredForPodLogs()
LegacyScope_DisabledInProduction()
```

### 21.6 Production startup tests

```csharp
ProductionMode_WithDevIssuer_RefusesStartup()
ProductionMode_WithHttpMetadata_RefusesStartup()
ProductionMode_WithDefaultKubeConfigFallback_RefusesStartup()
ProductionMode_WithoutExplicitNamespaces_RefusesStartup()
DevelopmentMode_AllowsLocalDefaults()
```

---

## 22. Documentation Updates

Add or update:

```text
docs/security-model.md
docs/tool-permissions.md
docs/policy.md
docs/approval-flow.md
docs/production-hardening.md
docs/threat-model.md
docs/audit-events.md
docs/redaction.md
```

### 22.1 `docs/policy.md`

Should explain:

- Policy profile.
- Deny vs warning.
- Default rules.
- How to configure image registries.
- How to configure service exposure.
- What is intentionally not checked yet.

### 22.2 `docs/production-hardening.md`

Should include:

- Production mode requirements.
- Identity provider requirements.
- TLS assumptions.
- Kubernetes ServiceAccount/RBAC guidance.
- Approval/audit persistence.
- Secrets/log redaction.
- Backup/retention notes.

### 22.3 `docs/threat-model.md`

Should explicitly list:

- Trusted components.
- Untrusted MCP clients.
- Untrusted/partially trusted AI output.
- Kubernetes API server as hard boundary.
- Out-of-scope compromised cluster/admin/IdP.
- Prompt injection assumptions.
- Approval bypass scenarios.
- TOCTOU/drift risks.
- Data leakage risks.

---

## 23. What Not To Build Yet

Avoid these until the safety foundation is stronger:

### 23.1 Do not add broad generic Kubernetes CRUD yet

Generic CRUD would make the project more like other Kubernetes MCP servers and increase blast radius before policy is ready.

### 23.2 Do not add `exec`, `attach`, or `port-forward`

These are high-risk interactive operations and undermine the safety story.

### 23.3 Do not add Secret reads

Secret read access would create immediate data-exfiltration risk through the model channel.

### 23.4 Do not add cluster-scoped writes

ClusterRole, ClusterRoleBinding, Namespace, CRD, and admission-policy mutations should remain out of scope for now.

### 23.5 Do not treat prompt-injection detection as a complete security boundary

Use it for enforcement signals, audit, and redaction, but keep RBAC, policy, approval, and audit as the real controls.

---

## 24. Practical Definition of “Production-Ready Enough”

This project should not claim full production readiness until at least the following are true:

- Production mode fails closed.
- Deep manifest policy exists.
- Dry-run is mandatory for apply plans.
- Human approval page shows diff and policy findings.
- Default apply is non-force.
- Approval store is concurrency-safe.
- Audit schema is stable.
- Finer-grained scopes exist.
- Log redaction exists.
- CI includes policy, approval, and security regression tests.
- Docs clearly distinguish local demo vs production deployment.

A reasonable wording before then:

> Kubernetes-MCP-Guard is an experimental security gateway for AI-assisted Kubernetes operations. It demonstrates approval-gated, auditable mutation workflows and is suitable for local demos and controlled internal evaluation. It is not yet production-certified.

---

## 25. The Most Important Architectural Principle

Every mutation should answer these questions before it reaches Kubernetes:

1. **Who requested it?**
2. **What exact object changes are proposed?**
3. **Does policy allow it?**
4. **Did Kubernetes dry-run accept it?**
5. **What is the diff from live state?**
6. **Who approved it?**
7. **Was the approved plan unchanged?**
8. **Did live state drift since approval?**
9. **What exactly was applied?**
10. **Can the audit log prove all of the above?**

If the project can answer those questions reliably, it will have a clear and valuable niche among Kubernetes-oriented MCP tools.
