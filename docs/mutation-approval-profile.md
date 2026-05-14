# MCP Mutation Approval Profile Sketch

This document sketches a possible MCP mutation-approval profile. InfraGate is currently an experimental reference implementation of this shape, with Kubernetes as the first concrete adapter.

The profile goal is narrow: bind human approval to an exact mutation intent and an exact review snapshot, then allow execution only after approval and required pre-execution gates pass. It is not a full policy engine, authorization model, workflow product, or Kubernetes-specific planning format.

## Core Idea

The generic part is the approval lifecycle:

- plan identity
- intent and review digest binding
- plan validity and challenge TTL
- requester and approver binding
- approval challenge lifecycle
- replay prevention through execution reuse policy
- audit spine
- approval-bound execution semantics

The domain-specific part is the evidence and execution meaning:

- what a mutation intent means
- how impact is previewed
- whether dry-run exists
- whether diff exists
- how freshness or drift is checked
- how domain policy is evaluated
- what the human can safely see
- how execution retries and idempotency work

## Roles

**Generic Approval Core** owns the domain-independent approval lifecycle: plan envelopes, lifecycle state, digest checks, approval challenges, approval grants, audit spine, review snapshot canonicalization, and pre-execution gate orchestration. It does not define domain-specific review content, but it may host a review surface that renders adapter-provided evidence.

**Domain Adapter** defines, explains, and executes mutation intents for one target system. The Kubernetes adapter is the first adapter and owns Kubernetes mutation meaning, dry-run evidence, diffs, drift detection, Kubernetes policy checks, Kubernetes mutation-intent canonicalization, execution behavior, evidence artifact digests, and adapter audit payloads.

**Approval Authority** creates approval challenges, enforces approval policies, records approval decisions, and exposes approval grants for execution. In this repository, that role is currently implemented by the gateway plus approval store. Another implementation could delegate the role to an external workflow system.

**Review Surface** renders the immutable review snapshot identified by the review digest. It must not rely on model-supplied approval content as the source of truth.

## Plan Envelope

A plan envelope is a generic wrapper around one domain-specific mutation intent. It is not the mutation itself.

Minimum generic fields:

```json
{
  "planId": "opaque workflow identifier",
  "profile": "mcp.mutation-approval",
  "operationType": "adapter-specific operation label",
  "requester": {
    "subject": "authenticated requester subject"
  },
  "approvalPolicy": {
    "type": "same-subject"
  },
  "executionReusePolicy": {
    "type": "single-execution"
  },
  "validFrom": "2026-05-14T00:00:00Z",
  "validUntil": "2026-05-14T01:00:00Z",
  "freshnessPolicy": {
    "checks": [
      {
        "type": "adapter-defined"
      }
    ]
  },
  "intentDigest": {
    "algorithm": "sha-256",
    "canonicalization": "adapter-defined",
    "value": "..."
  },
  "reviewDigest": {
    "algorithm": "sha-256",
    "canonicalization": "profile-defined",
    "value": "..."
  },
  "evidenceArtifacts": {
    "adapter": "kubernetes",
    "items": [
      {
        "type": "diff",
        "digest": {
          "algorithm": "sha-256",
          "canonicalization": "adapter-defined",
          "value": "..."
        }
      }
    ]
  }
}
```

`planId` is an opaque workflow handle for MCP calls, approval URLs, audit correlation, and storage. It is not an integrity mechanism. `intentDigest` proves the executable mutation intent is the same. `reviewDigest` proves the human-approved review snapshot is the same.

## Digests

The profile uses two digest bindings:

- **Intent Digest** binds the exact executable mutation intent.
- **Review Digest** binds the immutable review snapshot, including plan-envelope metadata, the intent digest, evidence artifact digests or digest-bound references, redaction metadata, approval policy, execution reuse policy, freshness policy, requester, plan validity window, and review-surface context.

Every digest declares its algorithm and canonicalization. The generic approval core defines canonicalization for generic envelope metadata and the review digest. Each domain adapter defines canonicalization for its mutation intent and evidence artifacts.

## Approval Lifecycle

The generic lifecycle is:

1. Create a plan envelope for a domain-specific mutation intent.
2. Compute intent and review digests using declared canonicalization.
3. Expose trusted plan evidence through a review surface.
4. Create one or more short-lived approval challenges while the plan remains valid.
5. Record an approval, denial, rejection, or expiry through the approval authority.
6. On successful approval, issue or reference an approval grant bound to the plan identifier, intent digest, review digest, requester, approver, approval policy, expiry, and reuse constraints.
7. Before execution, verify all pre-execution gates.
8. Execute through the domain adapter only if every required gate passes.
9. Record the outcome in the audit trail.

Same-subject approval is the default approval policy: the approver must be the same authenticated subject as the requester. Other approval policies, such as delegated approval or multi-party approval, are future extension points.

## Pre-Execution Gates

Approval is necessary but not sufficient. Immediately before mutation, approval-bound execution verifies:

- plan validity window still allows execution
- authorization check still passes
- approval authority reports a valid approval grant
- intent digest still matches executable intent
- review digest still matches approved review snapshot
- execution reuse policy allows another successful execution
- declared freshness policy passes
- required domain policy checks still pass

Freshness and domain policy meanings are adapter-owned, but the generic profile requires declared gates to be evaluated before execution. A freshness policy may contain zero or more freshness checks so adapters can combine checks such as dry-run, resource-version matching, preview recomputation, or other target-specific freshness signals.

## Replay And Reuse

The default execution reuse policy is single-execution: one approved plan envelope may authorize at most one successful execution.

Reusable plans are an explicit future extension point. They must opt in through an execution reuse policy that defines how many successful executions are allowed and under what conditions.

Retry behavior for failed or unknown execution attempts is domain-adapter-owned because target systems differ sharply in idempotency and failure semantics.

## Audit Spine

The profile requires a generic audit spine that proves the lifecycle:

- `plan.created`
- `challenge.created`
- `challenge.approved`
- `challenge.denied`
- `challenge.expired`
- `challenge.rejected`
- `grant.issued`
- `execution.started`
- `execution.blocked`
- `execution.failed`
- `execution.succeeded`

Generic audit events should carry plan identifier, intent digest, review digest, requester, approver when relevant, approval policy, grant identifier when relevant, timestamps, and outcome. Domain adapters may attach adapter audit payloads such as Kubernetes object references, namespaces, dry-run summaries, drift messages, and policy findings.

## Kubernetes Adapter Boundary

The Kubernetes adapter owns:

- Kubernetes mutation intents such as apply, delete, scale, restart, and set image
- Kubernetes object references
- manifest parsing and allow-lists
- server-side dry-run
- Kubernetes diff and drift detection
- Kubernetes domain policy checks
- Kubernetes mutation-intent canonicalization
- Kubernetes evidence artifact digests
- Kubernetes execution and retry behavior
- Kubernetes adapter audit payloads

It does not own approval challenge creation, approval policy enforcement, generic digest semantics, audit spine shape, or generic pre-execution gate orchestration.

## Current Repository Fit

The current implementation already proves several important properties:

- plans and approval challenges are separate records
- browser approval is out of band from MCP tool calls
- approval is bound to requester identity
- challenge TTL is enforced
- approved execution is hash-bound
- applied plans cannot be applied again
- Kubernetes dry-run and drift checks gate execution
- audit events record approval-flow transitions

The main architectural drift from the target profile is that the shared approval layer still models `K8sPlan` directly. Moving toward this profile means separating generic plan-envelope lifecycle from Kubernetes-specific mutation intent and evidence.
