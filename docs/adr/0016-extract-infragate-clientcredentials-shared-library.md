# ADR-0016: Extract `InfraGate.ClientCredentials` Shared Library Now

**Date:** 2026-05-23
**Status:** Accepted

---

## Context

The Anomaly Observer authenticates to the gateway via OAuth `client_credentials` against the local Keycloak realm. The required code is small but error-prone: a cached token, a refresh-near-expiry timer, a one-time forced refresh on 401, thread-safe acquisition, and an `HttpMessageHandler` that injects the bearer header on every outgoing request.

`InfraGate.DownstreamAuth` already contains the same mechanical pattern for the Gateway → McpServer HTTP direction (`IDownstreamServiceTokenProvider`, `DownstreamAuthConventions`, `DownstreamAuthOptions`). That code is operational infrastructure for a feature that is not currently on the roadmap — today the Gateway speaks stdio to McpServer, not HTTP, so `InfraGate.DownstreamAuth` is partly built but unexercised in the running gateway.

Three paths were considered:

- **(i) Copy the ~80 LOC pattern into `InfraGate.Observer`.** Zero coupling to existing code. Quick. Invites silent drift between the two copies over time.
- **(ii) Reference `InfraGate.DownstreamAuth` directly from `InfraGate.Observer`.** Mechanically works. Semantically wrong — `DownstreamAuth` is named and shaped for the gateway → downstream direction. Coupling the Observer to a not-yet-exercised piece of infrastructure means a change to `DownstreamAuth` (for an eventual HTTP downstream rollout) can ripple back into the Observer.
- **(iii) Extract a new `InfraGate.ClientCredentials` shared library now.** Holds `IClientCredentialsTokenProvider`, `ClientCredentialsTokenProvider`, `ClientCredentialsBearerHandler`, options, and conventions. Both `InfraGate.DownstreamAuth` and `InfraGate.Observer` consume it. Costs a small refactor of `DownstreamAuth` today.

Path (iii) violates YAGNI in the strict sense — extracting before a third consumer exists is the textbook over-engineering anti-pattern. The grilling explicitly weighed that and chose (iii) anyway, because the cost of *not* extracting is that two consumers (with a third — an executor — known to be coming) develop independent copies that drift, and consolidating later requires reconciling drift in addition to extracting.

## Decision

Create `src/InfraGate.ClientCredentials/` as a new shared library before implementing the Observer's auth path. Migrate `InfraGate.DownstreamAuth` to consume it in the same change. The Observer references `InfraGate.ClientCredentials` and inherits the cached-token + bearer-injector pattern verbatim.

The library's public surface is intentionally minimal:

- `IClientCredentialsTokenProvider` (acquire / refresh / cache).
- `ClientCredentialsBearerHandler` (`DelegatingHandler` injecting the bearer + retrying once on 401).
- `ClientCredentialsTokenOptions` (record holding token endpoint, client id, client secret, scope, audience).
- `ClientCredentialsServiceCollectionExtensions` (DI registration helpers).
- `ClientCredentialsConventions` (shared constants).

The library is a **deep module** in the architecture vocabulary: a small interface protects substantial behaviour (cache, refresh timing, retry, thread safety, configuration validation).

## Consequences

- **`InfraGate.DownstreamAuth` is refactored in the same task that creates the library.** Its public surface is preserved; only the internals are replaced by calls into the new library. The existing `DownstreamAuth.Tests` suite must remain green as the gate on the refactor.
- A third consumer arriving later (the executor agent, when it begins calling `execute_approved_plan` against the gateway) requires no further extraction — it just references `InfraGate.ClientCredentials` and configures its own options.
- Bugs in token acquisition are fixed once and benefit every consumer.
- A reader exploring the codebase sees a single canonical home for the client_credentials pattern. They are not faced with two near-duplicate implementations and the question of which one to trust.
- The decision *would* be wrong if `InfraGate.Observer` had been the only ever consumer of this code. The judgement that justifies it is that the executor agent is concretely planned, and the gateway-to-server HTTP path remains an open possibility on a longer horizon. If both of those expectations evaporate, ADR-0016 looks like premature abstraction in retrospect. The cost of being wrong is small: one extra project reference and ~80 LOC of indirection.
