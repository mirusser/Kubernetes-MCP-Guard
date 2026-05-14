Yes — I think your two concerns are exactly the two that would come up in a serious design review.

I’d phrase them like this:

> The generic part is the approval lifecycle.
> The non-generic part is the domain-specific evidence that makes a plan meaningful.

That distinction matters a lot.

## 1. Generic plan generation is genuinely hard

You are right: Kubernetes is unusually friendly for this pattern.

Kubernetes gives you useful primitives:

* server-side dry-run
* structured manifests
* resource versions / live state
* diffable desired vs current state
* admission/policy hooks
* RBAC
* namespaces and typed resources

Many other systems do not.

For example:

* a SaaS API may not have dry-run
* an email API can show a message preview, but cannot “dry-run” sending
* a payment API may support idempotency keys, but not full simulation
* a database migration may be explainable, but not perfectly reversible
* a CI/CD deployment may have a plan, but the actual runtime effects are only partly knowable
* an IAM change may be diffable syntactically, but hard to reason about semantically

So yes, **a universal “plan” schema is probably unrealistic**.

But a universal **plan envelope** may be realistic.

Something like:

```json
{
  "planId": "...",
  "operationType": "kubernetes.apply",
  "target": "...",
  "digest": "sha256:...",
  "createdAt": "...",
  "expiresAt": "...",
  "requester": "...",
  "riskLevel": "high",
  "evidence": {
    "summary": "...",
    "diff": "...",
    "dryRun": "...",
    "policyFindings": [...]
  }
}
```

The standard would not say every service must support Kubernetes-style dry-run. It would say:

> A mutation plan must contain a canonical payload digest and may attach domain-specific evidence such as diff, dry-run, policy findings, preview, explain plan, simulation result, cost estimate, or rollback plan.

That makes your idea more portable.

## 2. Making MCP servers responsible for security is a real concern

This is also true.

Some MCP implementers will not want to build:

* approval UIs
* OAuth browser flows
* durable state
* audit logs
* policy engines
* replay protection
* reviewer assignment
* enterprise approval routing

And they may be right not to.

Oracle’s HITL model is basically an approval/workflow orchestration model: Oracle describes it as a way to obtain human approval, feedback, and oversight in agentic AI automation, with workflows, forms, assigned tasks, and process automation workspace completion. ([Oracle Docs][1])

That kind of platform is designed to own the workflow state. Your pattern asks the MCP mutation layer to own at least part of that state.

So the standard should probably **not require every MCP server to implement the whole approval system internally**.

A better model is:

> The MCP server must be able to participate in a mutation-approval contract. The approval authority may be internal to the server, a gateway, or an external workflow engine.

In other words, separate roles:

* **Planner**: produces the mutation plan and digest
* **Approval authority**: handles human approval, identity, TTL, delegation, audit
* **Executor**: verifies approved digest and applies mutation
* **Policy engine**: optionally validates the plan
* **Audit sink**: records the decision and execution

In your implementation, your gateway plays several of these roles. In an enterprise environment, Oracle / ServiceNow / Jira / Backstage / custom workflow could play the approval-authority role.

That makes the idea less threatening to MCP implementers.

## More concerns I see

### Concern 3: canonicalization is hard

“Hash the plan” sounds simple until two systems serialize the same intent differently.

Questions:

* Are JSON object keys sorted?
* Are defaults included?
* Are timestamps included in the hashed material?
* Are comments removed from YAML?
* Are Kubernetes server-defaulted fields included?
* Is the digest over the original request, normalized intent, rendered manifest, or final API payload?

For a standard, this is critical. You probably need the concept of a **canonical approval payload**:

> The digest must be computed over a deterministic, canonical representation of the exact mutation intent that the executor will use.

Without this, people can claim hash-bound approval while hashing the wrong thing.

### Concern 4: plans may contain secrets

A mutation plan might include:

* Kubernetes Secrets
* API tokens
* database passwords
* cloud credentials
* private URLs
* customer data
* environment variables

But the human reviewer needs enough information to approve.

So the profile needs a redaction story:

* digest covers the full payload
* UI may show redacted fields
* audit may store redacted display data
* sensitive raw plan storage must be protected
* reviewer should see that hidden fields exist

This is tricky. If the human cannot see the secret value, are they approving the exact payload? Maybe they are approving “a secret will be changed,” not its value. That is acceptable, but it must be explicit.

### Concern 5: digest-bound approval does not prove semantic safety

A human may approve the exact payload and still not understand its impact.

For example:

* `replicas: 0` is syntactically obvious but operationally dangerous
* an IAM policy diff may look small but grant broad privilege
* a Kubernetes NetworkPolicy may accidentally cut off traffic
* a deployment image tag may point to mutable content
* a Helm chart change may render many hidden resources

So digest binding solves **payload substitution**, not **human comprehension**.

You should be clear about that. It is one security property, not a complete safety system.

### Concern 6: stale-state checks are domain-specific

Your Kubernetes flow can re-run dry-run and diff. Other systems may only be able to check weaker conditions:

* resource version unchanged
* ETag unchanged
* object last-modified unchanged
* idempotency key unused
* preview still matches
* external workflow ticket still open
* no check available

So the generic profile should probably support different **freshness predicates**, not mandate dry-run.

Maybe:

```text
freshnessCheck:
  type: dry_run | etag_match | resource_version_match | preview_recompute | none
```

And high-risk operations should declare when no freshness check is possible.

### Concern 7: approval delegation is more complex than same-subject

Same-subject mode is great for your initial threat model.

But real organizations often need:

* requester ≠ approver
* manager approval
* break-glass approval
* two-person approval
* service owner approval
* namespace owner approval
* change-window approval
* CAB-style approval

So same-subject should be one policy mode, not the only model.

Better language:

> The profile requires requester/approver binding according to policy. Same-subject is the simplest mode; delegated or multi-party approval can be added by the approval authority.

### Concern 8: state introduces reliability and attack-surface concerns

Once the server stores pending plans and approval challenges, you now need to care about:

* garbage collection
* plan expiration
* storage encryption
* backups
* lock contention
* multi-replica consistency
* crash recovery
* partial execution
* race conditions
* audit log integrity

That is probably why some teams prefer external workflow/state-machine systems.

This does not kill the idea. It suggests the standard should allow external approval/state backends.

### Concern 9: MCP URL elicitation already puts responsibility on the server

This is important: MCP already expects stateful behavior for URL-mode elicitation. The spec says URL mode lets servers send users to external URLs for out-of-band interactions, and that servers implementing elicitation must securely associate state with users, not with session IDs alone. It also says remote MCP servers should derive user identity from credentials, such as the `sub` claim. ([modelcontextprotocol.io][2])

So your idea is not introducing statefulness from nowhere. MCP already has a stateful out-of-band pattern. You are proposing a more specific state machine for high-risk mutations.

But the burden is real.

### Concern 10: boundaries with authorization are easy to confuse

Approval is not authorization.

A user may approve an action they are not authorized to perform. Or an authorized user may still need approval.

MCP has ongoing thinking around tool-level authorization metadata: one SEP proposes optional tool-level scope requirements because existing tool annotations like `readOnlyHint` and `destructiveHint` help with UX but do not express authorization requirements. ([GitHub][3])

Your proposal should stay separate:

* OAuth scopes / RBAC answer: “May this actor do it?”
* approval answers: “Was this specific operation reviewed and accepted?”
* policy answers: “Is this operation allowed under organizational rules?”
* digest binding answers: “Is this the same operation that was approved?”

Keeping those separate makes the architecture much clearer.

## What I’d change in your framing

I would stop thinking of it as:

> “MCP servers should implement my security system.”

And frame it as:

> “MCP mutation tools should expose enough structure for an approval authority to bind human approval to the exact operation later executed.”

That is much more flexible.

A possible standard shape:

```text
Plan
  produce canonical mutation payload
  produce digest
  attach domain-specific evidence

Challenge
  approval authority binds digest to identity, policy, TTL, and approval decision

Execute
  executor verifies digest, authorization, approval state, freshness predicate, and single-use semantics
```

Then each implementation can choose:

* embedded approval in the MCP server
* gateway-enforced approval
* external workflow engine
* Kubernetes admission integration
* enterprise change-management tool

## My honest opinion

Your idea is stronger if you **do not make dry-run or Kubernetes-style planning the standard**.

The standard-worthy part is smaller and cleaner:

> For high-risk mutations, approval should bind to a canonical digest of the mutation intent, and execution should verify that digest plus authorization, approval state, expiry, replay status, and any declared freshness checks.

Everything else is domain-specific evidence.

That solves both of your concerns:

1. It becomes generic enough for non-Kubernetes systems.
2. MCP implementations do not have to own all security machinery; they only need to produce/consume a portable approval contract.

So yes, your concerns are valid — but they do not undermine the idea. They help you refine it from “Kubernetes gateway architecture” into “MCP mutation approval profile.”

[1]: https://docs.oracle.com/en/cloud/paas/application-integration/human-loop/human-loop-agentic-ai-oracle-integration.html "Human in the Loop for Agentic AI in Oracle Integration"
[2]: https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation "Elicitation - Model Context Protocol"
[3]: https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1880 "SEP-1880: Tool-level scope requirements for MCP tools · Issue #1880 · modelcontextprotocol/modelcontextprotocol · GitHub"



----


That is a very good dream — and it is realistic.

The right positioning would be:

> **Reference implementation of a generic MCP mutation-approval profile, with Kubernetes as the first concrete adapter.**

That avoids making the standard Kubernetes-shaped while still using Kubernetes to prove it works against a real, high-risk system.

I’d structure the project mentally like this:

```text
generic approval core
  ├─ plan envelope
  ├─ canonical digest
  ├─ challenge lifecycle
  ├─ approval UI
  ├─ identity binding
  ├─ TTL / replay prevention
  ├─ audit events
  └─ executor contract

kubernetes adapter
  ├─ manifest normalization
  ├─ server-side dry-run
  ├─ diff
  ├─ policy checks
  ├─ resourceVersion / drift checks
  └─ kubectl/client-go apply/restart execution
```

The **generic core** should not know what a Deployment, Pod, Secret, or Helm chart is.

It should only know:

```text
planId
operationType
requester
canonicalPayloadDigest
displayEvidence
risk
approvalPolicy
freshnessChecks
executionState
auditEvents
```

Then the Kubernetes adapter supplies the domain-specific evidence:

```text
diff
dry-run result
policy findings
namespace/name/kind
resourceVersion expectations
rendered manifest
```

That is exactly how you turn your current implementation into something standard-shaped.

A good README tagline could be:

> A reference implementation for digest-bound human approval of MCP mutations. Kubernetes is the first adapter, used to demonstrate planning, dry-run, policy validation, approval, replay prevention, and audited execution.

Or more humble:

> An experimental reference implementation for a possible MCP mutation-approval profile, using Kubernetes as the first real-world integration.

The key design move is to define interfaces early:

```csharp
interface IMutationPlanner<TRequest>
{
    Task<MutationPlan> CreatePlanAsync(TRequest request, Subject requester);
}

interface IApprovalAuthority
{
    Task<ApprovalChallenge> CreateChallengeAsync(MutationPlan plan);
    Task<ApprovalDecision> VerifyApprovalAsync(string planId, string digest);
}

interface IMutationExecutor
{
    Task<ExecutionResult> ExecuteAsync(MutationPlan plan, ApprovalDecision approval);
}

interface IFreshnessChecker
{
    Task<FreshnessResult> CheckAsync(MutationPlan plan);
}
```

Even if your actual code is not polished yet, that interface separation will make the idea much more credible.

The eventual story becomes:

1. **Here is the profile.**
2. **Here is a generic lifecycle.**
3. **Here is a Kubernetes adapter that proves it on real infrastructure.**
4. **Here is how another adapter could implement the same profile for Terraform, GitHub Actions, IAM, databases, etc.**

That is a strong portfolio and standards story.

And honestly, for career value, this is much better than “I built a Kubernetes MCP wrapper.” The more impressive version is:

> “I identified a missing security abstraction in agentic infrastructure tooling, built a reference implementation, validated it with Kubernetes, and started an upstream standards discussion.”

That is the kind of sentence that gets attention.


----