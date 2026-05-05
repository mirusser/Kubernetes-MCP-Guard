# When "Did You Approve This?" Isn't Enough: Rethinking AI-Gated Kubernetes Mutations

*How a security audit revealed that proving a plan's integrity is not the same as proving a human actually said yes and what I changed.*

TL;DR: Elicitations are not good enough to prove human approval

```text
Before:
MCP client ── elicitation approval ──> MCP server ──> Kubernetes

After:
MCP client ── gets URL ──> human browser ── OAuth + approval ──> Gateway ──> Kubernetes
```

---

## Prologue: The Problem with Giving an AI Access to Production

Kubernetes is infrastructure. Infrastructure is state. State, once changed, can be very hard to undo, especially when a deployment is scaled to zero replicas at 2 AM or a ConfigMap carrying connection strings is overwritten by a well-intentioned AI agent that misread a label selector.

The premise of this project (an MCP gateway that lets AI coding assistants interact with Kubernetes) required answering an uncomfortable question from day one: *how do you let an AI touch production without letting it destroy production?*

The answer the system was built around was a **plan-and-approve loop**: every mutation gets staged as a pending plan, committed to disk 
with a cryptographic hash, and only applied after a human explicitly confirms. No confirmation, no change. The AI can plan; only the human can act.

That was the theory. The implementation made it real or so it seemed.

---

## Chapter 1: The Original Architecture

### Plans, Hashes, and a Handshake

When an AI agent called a mutation tool, let's say, `request_scale_deployment` the server did not immediately patch the Kubernetes API. Instead, it:

1. Serialised the intended operation into a **plan file** (a JSON document describing the operation, the namespace, the affected objects, and any parameters).
2. Computed a **SHA-256 hash** of that file and stored it alongside.
3. Returned the plan ID and a human-readable summary to the agent.

The plan file lived under `.mcp-approvals/pending/`. Nothing touched Kubernetes yet.

When the agent subsequently called `apply_approved_plan(planId)`, the server triggered **MCP elicitation** — a protocol-level mechanism for an MCP server to pause execution and ask the connected client to prompt its user for input. The elicitation message looked roughly like this:

```
Approve this Kubernetes plan?

PlanId: 123...
Operation: scale
Namespace: mcp-nginx-demo
Objects:
  - apps/v1 Deployment mcp-nginx-demo/demo

Plan hash: 3a7f8c1e2d...

Respond with approve=true, the exact PlanId, and the exact Plan hash shown above.
```

The MCP client (whatever was running the AI session, be it a CLI tool, an IDE extension, or a custom agent harness) would display this prompt to the user. The user would mark `accept` or `decline`. The client would send that response back through the MCP protocol. The server would receive it, re-verify the hash, and either apply the plan or refuse.


```json
{ approve: true, planId: "123...", planHash: "3a7f8c1e2d..." }
```

The server now validated the echoed hash against the stored value using a constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to avoid timing side-channels. If the hashes did not match, the plan was refused with an explicit rejection message.

On paper, this was solid. The user sees the hash. The hash is bound to the plan content. The server re-verifies at apply time. A compromised response that forged the hash would be caught. A plan file tampered with between approval and apply would be caught.

The system proved **plan integrity**.

---

## Chapter 2: The Audit

### A Different Kind of Question

A security audit of the architecture examined the approval-gated mutation flow.

> **Auto-Approval Loophole — Human Presence Not Provable**

The finding made a distinction that the implementation had not made:

> *The hash proves plan integrity, not human presence.*

Read that again slowly. Hash binding confirms that the plan the server is about to apply matches the plan content the user was shown the hash of. It says nothing about whether a human actually read the prompt, considered it, and consciously marked `accept`.

The specific attack path:

> A compromised MCP client can:
> 1. Receive the elicitation request, client is the transport layer, it sees everything.
> 2. Never render the prompt to the human SRE.
> 3. Automatically respond `Accept`. The hash is fetched by the server from the pending plan file, so the client does not even need to know or forward it independently.

### The Malicious Browser Extension Analogy

The audit offered a clarifying analogy: *"This is structurally equivalent to a malicious browser extension auto-clicking a payment confirmation dialog. The transaction hash on the payment doesn't help if the extension intercepts the click event before the human sees it."*

The hash binds the *content* of what is being approved. But if the approval signal itself (the click, the keypress, the `approve: true` JSON blob) is generated by the same process that is supposed to be showing the human the content, then the hash binding protects against one attack (content swap) while leaving the more fundamental attack (human bypass) completely open.

### The Threat Model in Plain Terms

A compromised MCP client is not a hypothetical. Prompt injection — where malicious content embedded in data sources (Kubernetes labels, ConfigMap values, pod annotations) hijacks the AI's reasoning — is precisely the scenario the guardrails were built to detect. A sufficiently sophisticated prompt injection can turn an AI agent's tool calls against its operator. In that scenario, the MCP client process is effectively under attacker control.

The threat model the audit operated under assumed an attacker who had compromised the MCP client process through one of several plausible vectors: a malicious package, a supply-chain attack, or a prompt injection that achieved code execution. Under that assumption, the client controls the approval UI and therefore controls the approval decision.


A practical reproduction path against this approach is:

- Start the local demo deployment or equivalent source build.
- Authenticate a client to the gateway’s /mcp endpoint.
- Call one of the mutation planning tools, such as request_scale_deployment, and capture the returned PlanId.
- Invoke apply_approved_plan(planId).
- Instead of showing the elicitation to a human, have the client or harness automatically return approve=true and the same planId.
- Observe that the operation is applied and the approval file is written

---

## Chapter 3: Designing Out-of-Band Approval

### The Core Insight

To fix it, the approval signal needed to originate from **outside the MCP client's control plane**.

That may mean:
- A separate web UI served by the Gateway itself, requiring re-authentication.
- A push notification to a registered device (Slack, Teams, a mobile push-to-approve token).
- At minimum, a short-lived single-use token sent via a second channel that must be supplied in the apply call.

The option that could be implemented without external infrastructure dependencies was the first: **a Gateway-hosted browser approval UI with its own OAuth authentication flow**.

This became a plan: *Strong Out-of-Band Approval Flow*.

### The Structural Shift

The key architectural change was simple to state and significant to implement:

**The MCP client is no longer in the approval path.**

Instead of the MCP server sending an elicitation request through the MCP transport and waiting for the client to respond, the Gateway:

1. Returns an **approval URL** to the MCP client as plain tool response text.
2. The human opens that URL in a browser.
3. The browser authenticates to the Gateway using its **own OAuth session** — completely separate from the MCP JWT.
4. The Gateway renders the actual pending plan from disk — the client cannot influence what the browser shows.
5. The human clicks Approve or Deny.
6. The approval decision is recorded on the Gateway's filesystem.
7. The human returns to the MCP client and calls `apply_approved_plan` again. This time, the Gateway finds the recorded approval and forwards to the downstream server.

The MCP client receives a URL. It can choose whether or how to display that URL, but it no longer has a protocol-level mechanism to submit approval on the user’s behalf. The approval decision itself must happen at the Gateway-owned browser endpoint.

### The Challenge Architecture

Internally, the approval flow was built around **single-use approval challenges**:

When `apply_approved_plan(planId)` is called and no approval exists, the Gateway:

- Reads the pending plan and computes its current hash.
- Creates a challenge record containing:
  - A 32-byte cryptographically random challenge ID (64 hex characters — unguessable).
  - The `planId` and the current `planHash`.
  - The **requester's OAuth subject** (extracted from the JWT that called the MCP tool).
  - A creation timestamp and an expiry timestamp (default 15 minutes).
  - Status: `pending`.

The challenge is stored to disk, and the approval URL `https://gateway.host/approvals/{challengeId}` is returned to the client.

### The Browser Endpoints

The approval endpoints at `GET /approvals/{challengeId}` and `POST /approvals/{challengeId}/approve` are protected by a **browser cookie auth scheme** — `InfraGateApprovalCookie` backed by `InfraGateApprovalOAuth` (OAuth 2.0 authorization-code with PKCE S256).

These endpoints do not accept the same JWT bearer tokens used by the `/mcp` endpoint. An `Authorization: Bearer <jwt>` header on a browser-endpoint request gets a redirect to the OAuth login page. The two authentication surfaces are structurally separate.

The `GET /approvals/{challengeId}` endpoint renders the plan directly from disk, the Gateway reads the pending plan file and renders it into HTML. The MCP client cannot inject or modify what is shown. Additionally, it writes an **anti-forgery token** into the page, which must be submitted with any subsequent POST.

```
POST /approvals/{challengeId}/approve
Cookie: .infra-gate-approval=<session>
Content-Type: application/x-www-form-urlencoded

__RequestVerificationToken=<antiforgery-token>
```

When the approve POST arrives, the server runs a full validation chain:

1. **Anti-forgery token** - any CSRF attempt without the token from the GET is rejected.
2. **Browser OAuth subject** - extracted from the cookie session, compared against the challenge's `RequesterSubject` using ordinal equality. The person approving in the browser must be the same OAuth principal who triggered the MCP tool call that created the challenge.
3. **Challenge status** - must be `pending`. Already-approved or denied challenges cannot be reused.
4. **TTL** - `ExpiresAtUtc <= DateTimeOffset.UtcNow` transitions the challenge to `expired` and rejects the attempt.
5. **Plan hash drift** - the Gateway re-reads the current pending plan hash and compares it against the hash captured at challenge creation using `FixedTimeStringComparer.Equals` (constant-time). If someone tampered with the pending plan between challenge creation and approval, this rejects the attempt.

Only if all five checks pass does the Gateway write the approval file and mark the challenge as `approved`.

---

## Chapter 4: How This Closes The Vulnerability

Let us walk through the original attack step by step under the new architecture.

### Step 1: Client receives the elicitation request

**Before:** The elicitation was an MCP protocol message. The client was required to process it, display a prompt, collect input, and respond: all within the same process, all under the client's control.

**After:** There is no elicitation. MCP elicitation was removed entirely from the codebase. The client receives a plaintext string:

```
Approval required.
PlanId: 018fde...
Operation: scale
Namespace: mcp-nginx-demo
Objects:
  - apps/v1 Deployment mcp-nginx-demo/demo

Plan hash: 3a7f8c1e2d...
Approval URL: https://gateway.host/approvals/9b4c2f7a...

Open the approval URL in a browser, sign in with the same identity,
review the Gateway-rendered plan, then call apply_approved_plan again.
```

There is no protocol-level callback. There is nothing for the client to intercept.

### Step 2: Client never renders the prompt; client auto-responds "Yes"

**Before:** The client silently answered the elicitation with `{ approve: true, planId: "...", planHash: "..." }`. The server received this over the MCP transport and acted on it.

**After:** There is no MCP-level approval response. The `apply_approved_plan` tool accepts exactly one argument: `planId`. The test `ApplyApprovedPlan_ToolSchema_AcceptsOnlyPlanId` verifies this. The JSON schema for the tool contains no `hash`, `decision`, `approve`, or any approval-related field. There is no way to submit an approval decision through the MCP protocol.

The only way to record an approval is to POST to `/approvals/{challengeId}/approve`, a browser endpoint with its own auth scheme.

### Step 3: Client forwards the correct planId

**Before:** Forwarding the planId through MCP was sufficient to trigger the apply. The server accepted the elicitation response and applied the plan.

**After:** A second call to `apply_approved_plan(planId)` checks whether an **approved challenge** exists for that planId, that planHash, and that requester subject. All three must match. If no such challenge was approved via the browser, the Gateway issues a new challenge URL and returns another refusal.

The client calling `apply_approved_plan` in a loop accomplishes nothing beyond creating more challenge records, each of which expires after 15 minutes.

---

## Chapter 5: What Remains

Honest engineering requires acknowledging what is not solved.

### The Co-Compromise Scenario

The new flow is not proof against an attacker who compromises **both** the MCP client and the user's browser environment. For example, a malicious browser extension that can make authenticated requests using the user's existing session cookies.

Such an attacker could:
1. Extract the challenge ID from the tool response text.
2. Open `GET /approvals/{challengeId}` using the browser session.
3. Extract the anti-forgery token from the HTML response.
4. POST the approval.

This requires browser-level compromise in addition to MCP client compromise. That is a materially higher bar than the original vulnerability, which required only client compromise.

Full fix would require external infrastructure, meaning: to push-to-approve on a hardware-attested device, or a second-channel notification (SMS, push notification) that cannot be silently intercepted by a process running on the same machine. Those remain on the roadmap as hardening options.

### Same-Subject Binding Is Not Two-Person Authorization

The current flow enforces that the browser approver must be the **same OAuth subject** as the MCP requester. This means the person running the AI agent must also be the person who approves the plan.

This is a deliberate v1 decision: simple, auditable, predictable. It does not support a four-eyes / two-person approval model where a separate reviewer must sign off. That can be built on top of the existing challenge architecture if the use case demands it.

---

## Epilogue: The Lesson About "Proving Approval"

The elicitation flow looked secure. It had a hash. It had a schema. It had constant-time comparison. It had TOCTOU protection at apply time. Each of those controls was real and correct.

The gap was not in any of those mechanisms. The gap was in the trust model: the approval signal and the approval UI lived in the same process as the entity being defended against. No amount of cryptographic integrity checking can compensate for that structural problem.

Moving approval to an out-of-band channel (authenticated separately, rendered by the defender, submitted over an independent session) breaks the structural dependency. The MCP client can be compromised and still cannot approve a plan **by itself**. It can only deliver, suppress, or misrepresent the approval URL.

That is the difference between proving what was approved and proving who approved it.

---

## Postscript: Is This Just 2FA?

That's a sharp way to frame it, and it mostly holds but with a precise distinction worth making.

**Traditional 2FA** is about *authentication*: proving who you are through two independent factors before access is granted.

**What was implemented here** is closer to **out-of-band authorization**: proving not just who you are, but that *you* (the same identity, the same human) explicitly sanctioned a specific action through a channel independent of the one the AI is using.

The analogy that maps best is **bank wire transfer confirmation**: you initiate the transfer on the website (channel 1), but the bank sends a push notification to your phone that you must tap to confirm (channel 2). The channels are independent — compromising the website session doesn't give you the phone.

This system does the same shape of thing:

| Bank Wire | Your System |
|---|---|
| Web session initiates transfer | MCP JWT initiates `apply_approved_plan` |
| Push notification to phone (separate channel) | Browser OAuth session (separate auth scheme) |
| Bank verifies it's the same account | Same-subject check: browser `sub` must match MCP `sub` |
| One-time confirmation code | Single-use challenge with 32-byte random ID + TTL |

The key security property is not necessarily ‘two factors’; it is an action-specific approval path outside the MCP client’s control, bound back to the same identity and the same plan hash.

So: yes, in a loose sense, it's something like 2FA for Kubernetes mutations. But the more precise description is out-of-band, transaction-specific authorization. It does not merely ask, “is this user authenticated?” It asks, “did the same authenticated human approve this exact Kubernetes plan through a channel the MCP client cannot directly answer for them?”

If the browser approval flow also enforces WebAuthn, TOTP, device push, or another independent factor, then it becomes true step-up MFA for destructive Kubernetes actions. Without that, it is not quite 2FA; it is a separate approval channel with identity binding. Still a major security improvement over in-band MCP elicitation.

---

## Production consideration

For higher-assurance production workflows, a true second factor or second channel (such as push approval on a registered device, WebAuthn-bound confirmation, or a separate reviewer identity) would be stronger. 

The fix also introduces new side effects and engineering considerations. It adds user friction, requires a reachable browser-visible gateway URL, and converts approval from a simple MCP round-trip into a stateful, expiring workflow. In a multi-replica deployment, any challenge store or approval store will need a shared durable backend or shared volume; otherwise an approval written by one gateway instance may not be visible to another. The current public deployment materials are loopback-HTTP, single-host, and local-dev oriented, so the productionization work for the browser approval flow is non-trivial and should not be understated.

---

*Built with .NET 10, ASP.NET Core, MCP SDK 1.2, and a healthy distrust of in-band approval signals.*
