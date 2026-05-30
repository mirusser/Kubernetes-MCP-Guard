# ADR-0018: `propose_plan` Is a New MCP Tool, Not an Argument on `request_*`

**Date:** 2026-05-24
**Status:** Accepted

---

## Context

The gateway today exposes `request_*` mutation tools (`request_apply_manifest`, `request_scale_deployment`, `request_restart_deployment`, ...). Each one builds a **Plan Envelope** through `IDomainPlanBuilder.BuildAsync`, opens an **Approval Challenge** with **Same-Subject Approval Policy**, and returns the approval URL to the MCP caller. The implicit assumption is that the MCP caller is operated by a human who will *see* the returned URL in their MCP client and click through.

The **Remediation Planner** (ADR-0017) is a non-human MCP client. There is no human in front of its MCP client to see the URL. The approval channel must be out-of-band — an email to the on-call operator, with a routing token (the **Approval Access Code**) that lands them on the existing approval page.

Three shapes were considered for the Planner's gateway interface:

- **(i) `propose_plan` replaces `request_*` for the autonomous path.** A new tool, called only by the **Planner Service Identity**, that atomically builds the Plan Envelope (via the same `IDomainPlanBuilder`), assigns **Operator Approval Policy** (ADR-0019), generates an **Approval Access Code**, and dispatches an email. Returns `planId`. `request_*` tools remain unchanged for human-driven MCP clients.
- **(ii) `propose_plan` follows `request_*` as a two-step ceremony.** Planner calls `request_*` to get a "draft" planId, then calls `propose_plan(planId)` to escalate. Introduces a new "draft" state in the Plan Envelope lifecycle.
- **(iii) Add a `notifyVia` argument to `request_*`.** No new tool; opt-in `notifyVia: "email", approver: "ops@…"` parameters on existing `request_*` tools.

Option (ii) requires the **Generic Approval Core** to model a Plan Envelope state that exists *before* an Approval Challenge is created. Today that state doesn't exist — `request_*` immediately opens a challenge. Adding it for one consumer is a real behaviour change with no other beneficiary.

Option (iii) is smaller on the MCP surface but bundles two contracts onto one tool. The human and autonomous callers have:

- Different identity models (human OAuth subject vs `client_credentials` machine identity).
- Different scopes (`mcp:tools` vs `mcp:tools.propose`).
- Different approval policies (Same-Subject vs Operator Approval).
- Different notification mechanisms (return URL inline vs email + Approval Access Code).

Sharing one tool means each `request_*` implementation has to branch on caller identity to decide which contract it's in. The branching leaks the autonomous-path concern into every existing mutation tool.

Option (i) is the most surface-additive of the three but the least invasive to existing code: the Planner gets one clean tool, the gateway's existing `request_*` dispatch is unchanged, and the autonomous-only concerns (Operator Approval Policy assignment, email, access code) live behind one entry point.

## Decision

`propose_plan` is added to `McpGatewayConventions.ToolNames` as a new MCP tool. Its contract:

```
propose_plan(operationType: string, arguments: Record<string, object>)
  -> { planId: string, accessCodeSent: bool, codeExpiresAt: timestamp }
```

`operationType` is constrained to the v1 mutation tools (`restart_deployment`, `scale_deployment`) by the gateway's allowlist, mapped to the same `IDomainPlanBuilder.BuildAsync` invocation `request_*` already uses. `arguments` matches the schema the corresponding `request_*` tool would have accepted.

The handler is a thin orchestration that:

1. Verifies the caller's scope includes `mcp:tools.propose`.
2. Calls `IDomainPlanBuilder.BuildAsync(operationType, arguments, requester=service:planner, approvalPolicy=OperatorApproval)` — the builder signature is extended (per ADR-0019) to accept the policy so every adapter stamps it on the envelope.
3. Generates a one-time-use **Approval Access Code** via a new `IApprovalAccessCodeStore`.
4. Dispatches an email via a new `IApprovalEmailSender` (ADR refers; SMTP via `System.Net.Mail`, Mailpit in dev).
5. Returns the result envelope.

`request_*` tools are unchanged. Mutation tools the Planner is allowed to propose are listed both in the Planner's client-side whitelist (defense-in-depth) and in the gateway's server-side scope-to-operation allowlist (authoritative).

## Consequences

- **Per-caller-type contracts stay clean.** Human-driven callers continue to use `request_*` with the existing return-URL-inline contract. The Planner uses `propose_plan` with the email-plus-code contract. Neither path branches on the other.
- **Operator Approval Policy assignment is centralised.** Every plan originated through `propose_plan` declares **Operator Approval Policy**. No code path can accidentally create an autonomous-originated plan with **Same-Subject Approval**.
- **Email and Approval Access Code generation are gated at one place.** The autonomous-only concerns never touch `request_*` dispatch.
- **The MCP tool surface grows by one tool**, not by an argument across N tools. This is the trade-off explicitly accepted in option (i)'s favour.
- **Scope mapping is one entry**, not N entries. `mcp:tools.propose` maps to exactly `propose_plan`. Adding the next autonomous operation later is one allowlist edit.
- **Decision *would* be wrong** if the Planner had to call many different propose-style tools (one per operation), because then we'd have re-created the `request_*` shape under a different prefix. The judgement that justifies the single-tool shape is that `operationType + arguments` is sufficient parameterisation; the gateway's allowlist constrains the LLM's choice space without requiring N tools.
- **Reversal cost is moderate.** Renaming `propose_plan` to `request_*_autonomous` or merging into `request_*` later is mechanical refactor at the MCP layer and a non-trivial change to client whitelists, scope mappings, and audit-identity routing.
