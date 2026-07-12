# 31. Harden JWKS Validation Against Cache Poisoning and Key Rollover Race

Date: 2026-06-21

## Status

Accepted

## Context

The security audit finding F-11 identified weaknesses in the gateway's inbound JWT validation:

- IdentityModel's default `TokenValidationParameters.TryAllIssuerSigningKeys` is `true` on 8.x, so a
  token whose `kid` does not match any JWKS key is still tried against every key in the set.
- The gateway and downstream stdio validator relied on the framework's implicit
  `ConfigurationManager<OpenIdConnectConfiguration>` with its default 12-hour
  `AutomaticRefreshInterval`, leaving a large stale-trust window during key rollover.
- Neither path explicitly configured last-known-good behavior on transient JWKS fetch failure.

The gateway is the primary inbound-JWT validator; the downstream `DownstreamTokenValidator` is a
secondary internal consumer included for parity. Observer, Planner, and Executor are OAuth token
clients, not resource servers, and do not perform JWKS validation.

## Decision

1. **Unconditional strict `kid` matching.**
   - Set `TokenValidationParameters.TryAllIssuerSigningKeys = false` in both the gateway
     `JwtBearerOptions` and the downstream `DownstreamTokenValidator`.
   - Tokens whose `kid` header is missing or does not match a key in the active JWKS are rejected.
   - This is a correctness/safety property and is not exposed as a tunable option.

2. **Bounded JWKS cache refresh intervals.**
   - Both paths construct an explicit `ConfigurationManager<OpenIdConnectConfiguration>` with
     `AutomaticRefreshInterval = 5 minutes` and `RefreshInterval = 1 minute`.
   - The values are exposed as constants (`GatewayAuthConventions.DefaultJwksAutomaticRefreshInterval`,
     `GatewayAuthConventions.DefaultJwksMinimumRefreshInterval`, and
     `DownstreamAuthConventions.Defaults.JwksAutomaticRefreshInterval` /
     `JwksMinimumRefreshInterval`).
   - They are kept as constants rather than configuration knobs to avoid increasing the operator
     surface for a security-critical default.

3. **Last-known-good on fetch failure, without blanket superseded-key acceptance.**
   - We rely on `ConfigurationManager`'s built-in behavior: after a successful fetch, subsequent
     background refresh failures return the cached configuration instead of throwing.
   - We deliberately do **not** enable `TokenValidationParameters.ValidateWithLKG`, because that
     would widen the stale-trust window by accepting tokens signed by rotated-out keys.
   - First-fetch failure remains fail-closed, which is the existing correct behavior.

4. **Production HTTPS enforcement for downstream metadata.**
   - `McpGatewayOptions.ValidateProductionSafety` already enforced HTTPS/non-loopback for gateway
     OAuth authority, metadata, resource, approval, and introspection endpoints.
   - The same assertions are now applied to the downstream `Authority` and optional `MetadataAddress`,
     and `RequireHttpsMetadata` must be `true` in Production mode.
   - Local development continues to use HTTP over loopback; this is recorded as an accepted risk.

5. **No CA pinning in local development.**
   - The audit suggested pinning the JWKS TLS certificate to a CA "even in local/dev."
   - Local dev intentionally runs Keycloak over HTTP on loopback, so CA pinning is impractical there.
   - TLS remains enforced in Production via `RequireHttpsMetadata` and `RequireHttpsNonLoopbackUri`.

## Consequences

- A compromised JWKS response that injects an attacker-controlled key cannot be used to forge tokens
  unless the attacker also controls the `kid` value that matches a legitimate key.
- Rotated-out signing keys stop being trusted within the bounded refresh interval instead of up to
  12 hours.
- Transient JWKS/metadata fetch failures do not fail open and do not trigger per-request synchronous
  refetch storms.
- The local-development HTTP-over-loopback path keeps working unchanged.
- The downstream stdio validator gains the same hardening as the gateway.

## References

- `.agents/Plans/loose/f-11-jwks-cache-poisoning-key-rollover-mitigation-plan.md`
- `.agents/Plans/loose/security-audit.md` (F-11)
- `src/InfraGate.McpGateway.Auth/GatewayAuthentication.cs`
- `src/InfraGate.McpServer/DownstreamAuth/DownstreamTokenValidator.cs`
- `src/InfraGate.McpGateway/Configuration/McpGatewayOptions.cs`
- `src/InfraGate.McpGateway.Auth/GatewayAuthConventions.cs`
- `src/InfraGate.DownstreamAuth/DownstreamAuthConventions.cs`
