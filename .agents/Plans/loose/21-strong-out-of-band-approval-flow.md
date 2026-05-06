# Strong Out-of-Band Approval Flow

## Summary

Replace MCP in-band approval elicitation with a Gateway-hosted, browser-based approval flow. The MCP client can only request application of a pending plan; if the plan is not already approved, Gateway returns a trusted approval URL. The human opens that URL, authenticates with OAuth, reviews the actual pending plan loaded from the shared approval store, and approves or denies there.

This directly mitigates the compromised-client case where an MCP client shows Plan A while submitting approval for Plan B, because the client no longer controls the approval UI or approval payload.

## Key Changes

- Create a focused shared library project named `InfraGate.Approvals`.
- Move approval-domain contracts and helpers into it:
  - `K8sPlan`
  - `K8sObjectRef`
  - `ApprovalStore`
  - approval result records
  - `FixedTimeStringComparer`
  - approval storage conventions/constants
- Reference `InfraGate.Approvals` from both `InfraGate.McpServer` and `InfraGate.McpGateway`.
- Keep Kubernetes execution logic in `InfraGate.McpServer`; only approval storage/contracts become shared.

- Remove static bearer authentication from Gateway.
- Gateway `/mcp` uses OAuth/JWT bearer authentication only.
- Add browser cookie auth for approval pages, backed by OAuth authorization-code + PKCE.
- Use DevIssuer for local approval UI auth in development; external issuers must configure an approval UI public client.

- Change Gateway `apply_approved_plan(planId)` behavior:
  - If the plan already has a valid approval hash, forward to the downstream MCP server.
  - If approval is missing, read the pending plan and current hash from the shared approval store.
  - Create a short-lived, single-use approval challenge bound to `planId`, `planHash`, and the authenticated MCP requester subject.
  - Return an approval URL to the MCP client instead of starting MCP elicitation.
  - The client cannot approve, deny, or provide a hash through MCP.

- Add Gateway approval endpoints:
  - `GET /approvals/{challengeId}` shows the current pending plan loaded by Gateway.
  - `POST /approvals/{challengeId}/approve` writes approval only after validating auth, challenge status, expiry, requester subject, and current plan hash.
  - `POST /approvals/{challengeId}/deny` records denial and prevents challenge reuse.
- Store challenges under the approval root, for example `.mcp-approvals/challenges/{challengeId}.json`.
- Challenge records include:
  - challenge id
  - plan id
  - plan hash
  - requester subject
  - requester client id/auth context when available
  - created/expiry timestamps
  - status
  - approver subject
  - decision timestamp

- Require same-subject approval by default:
  - The browser-authenticated approver subject must match the MCP requester subject captured in the challenge.
  - Reject approval if the subject differs.
  - This can be revisited later for explicit two-person approval, but v1 should optimize for strong human-presence binding.

- Remove server-side MCP elicitation for approvals.
- `InfraGate.McpServer` applies only when an approved hash file already exists and still matches the pending plan.
- If approval is missing or stale, the server returns an approval-required/refused result.
- Keep TOCTOU protection: approval writes still recompute the pending plan hash immediately before writing the approved hash.

## Public Interfaces

- MCP approval schema no longer exists for apply approval.
- Gateway tool response for unapproved plans includes:
  - approval required status
  - plan id
  - current plan hash
  - approval URL
  - expiry timestamp
- Gateway approval UI displays:
  - plan id
  - operation
  - namespace
  - affected objects
  - plan hash
  - requester identity
  - approval expiry
  - approve/deny actions
- New Gateway options:
  - approval UI base URL
  - approval challenge TTL, default 10-15 minutes
  - OAuth authorization endpoint
  - OAuth token endpoint
  - approval UI client id
  - approval callback path
- DevIssuer gains local configuration for a pre-registered approval UI client and redirect URI.

## Test Plan

- Gateway integration tests:
  - unapproved `apply_approved_plan` returns an approval URL and does not call MCP elicitation
  - approval page requires browser authentication
  - approval page renders the actual pending plan and hash from disk
  - approve writes the approved hash and marks the challenge approved
  - a second `apply_approved_plan` forwards to the downstream server and applies
  - deny marks the challenge denied and later apply still requires approval
  - expired challenge cannot approve
  - reused challenge cannot approve twice
  - plan hash drift between challenge creation and approval is rejected
  - browser subject mismatch is rejected
  - compromised-client scenario: MCP-provided approval content is ignored because no MCP approval payload is accepted

- Server tests:
  - unapproved pending plan refuses without elicitation
  - approved matching pending plan applies
  - stale approval hash refuses after pending plan changes
  - approval store constant-time hash comparison remains covered

- Auth tests:
  - static bearer authentication is removed
  - `/mcp` accepts valid OAuth/JWT bearer tokens
  - `/approvals/*` uses browser cookie auth
  - DevIssuer local approval client can complete auth-code + PKCE flow

- Documentation checks:
  - architecture diagram shows MCP returning an approval URL, not an approval form
  - docs no longer describe static bearer as a supported Gateway mode
  - local dev docs explain starting DevIssuer and using the approval UI

## Assumptions

- Strong mitigation is preferred over preserving direct stdio approval UX.
- Direct server usage may still create and inspect plans, but mutation approval is only supported through the Gateway OOB flow.
- Same-user approval is the required v1 policy.
- `InfraGate.Approvals` is intentionally approval-specific, avoiding a vague generic shared project.
- Existing pending/approved plan file formats remain compatible unless a challenge/audit file is being added.
