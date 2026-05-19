# Lazy-Only Challenge Expiration — No Background Sweep

## Context

The mutation-approval profile requires a `challenge.expired` audit spine event for every expired Approval Challenge. The current implementation handles expiration lazily: `ExpireChallengeAsync` transitions a challenge to `expired` status and emits the audit event only when the challenge is explicitly accessed (approval page load, cancel endpoint). If no one revisits a challenge after its TTL elapses, the challenge file remains on disk with `pending` status indefinitely and no `challenge.expired` event is ever emitted.

A background `IHostedService` sweep could close this audit gap by periodically scanning challenge files and calling `ExpireChallengeAsync` for any challenge past its `ExpiresAtUtc`.

## Decision

Do not implement a background expiration sweep. Keep lazy-only expiration.

## Reasons

Expired challenges are already behaviorally inert through two independent guards:

1. `ValidatePendingChallengeAsync` checks `ExpiresAtUtc <= now` before allowing approval and calls `ExpireChallengeAsync` on the spot — the event fires if the challenge is ever touched.
2. `ApprovalChallengeStore.FindPendingAsync` filters by `ExpiresAtUtc > now` — expired challenges are invisible to deduplication and grant flow.

The only gap is audit completeness for the "requester walks away" case: the `challenge.expired` event is never emitted for abandoned challenges. That is an audit trail gap, not a behavioral correctness gap. No execution can be authorized, no challenge can be approved, and no grant can be issued through an expired challenge regardless.

The added complexity of a hosted service, configurable sweep interval, and thread-safe file scanning is not justified by closing an audit-only gap in an experimental reference implementation.

## Consequences

- `challenge.expired` audit events are emitted only when an expired challenge is explicitly accessed.
- Stale `pending` challenge files may accumulate on disk; they are harmless but visible to operators inspecting the challenge directory.
- A future implementation targeting production audit completeness requirements should introduce a background sweep that reuses the existing `ExpireChallengeAsync` path.
