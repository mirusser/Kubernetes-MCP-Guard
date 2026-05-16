# Implementation Plan: ADR 0002 Opaque Plan Identifiers And Separate Digests

## Summary

Implement ADR 0002 by making `planId` a purely opaque workflow handle, removing remaining approved-hash authorization paths, and naming pending-plan hashes only as challenge drift checks. Execution remains authorized only by an `ApprovalGrant` bound to `IntentDigest` and `ReviewDigest`.

## Key Changes

- Keep `PlanEnvelope.Id` as the C# and stored JSON property for this pass; treat it explicitly as the **Plan Identifier** and do not use it as an integrity value.
- In `InfraGate.Approvals`, change new plan IDs from timestamp-prefixed IDs to bare random opaque IDs, using 128-bit lowercase hex. Keep existing safe-ID validation broad enough to read old lowercase/dash plan IDs.
- Add a generic `ReviewSurfaceContext` metadata field to `PlanEnvelope` and include it in `ReviewDigest` canonicalization. Use constants, not repeated strings, for the gateway browser review surface and Kubernetes review renderer version.
- Rename `ApprovalChallenge.PlanHash` to `PendingPlanHash`, including stored challenge JSON and challenge audit payloads. Keep this hash only for pending-file drift detection; do not use it as execution authorization.
- Remove or retire legacy approved-hash APIs and types from `ApprovalStore`: `GetApprovedPlanAsync`, `ApprovePendingPlanAsync`, `ApprovedPlanResult`, `ApprovedPath` on result records, and `approved/*.sha256` directory creation. Existing approved-hash files should be ignored; callers must use grants.
- Keep Kubernetes intent canonicalization narrow: operation, namespace, parameters, objects, and manifest only. Do not include `planId`, requester, review evidence, dry-run, diff, policy findings, or review-surface metadata in `IntentDigest`.

## Test Plan

- Add/update `ApprovalStoreTests` for opaque plan ID shape, old `id` envelopes rejected as old format, and grants remaining the only execution authorization.
- Add/update digest tests proving:
  - changing executable intent changes `IntentDigest`;
  - changing review evidence, requester, validity, `planId`, or review context changes `ReviewDigest`;
  - changing review-only evidence does not change `IntentDigest`.
- Add/update gateway tests for `pendingPlanHash` persistence, digest drift rejection, pending-file drift rejection, same-subject approval, grant issuance, and legacy approved-hash files being ignored.
- Update Kubernetes manager apply/request tests to use grants instead of approved hash helpers, while retaining a regression test that an approved hash without a grant cannot mutate.
- Run:
  - `dotnet test tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj`
  - `dotnet test tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj`
  - `dotnet test InfraGate.slnx`

## Assumptions

- No migration is required for in-flight approval challenges; they are short-lived and can be re-requested.
- Existing pending envelopes that use `id` remain readable for compatibility; malformed or incomplete envelopes still get the existing “Re-request the plan” behavior.
- This does not add reusable plans, delegated approval policies, or a full evidence-artifact schema. The v1 `ReviewDigest` binds the full adapter payload plus review context; explicit evidence artifact records can be a later schema version.
