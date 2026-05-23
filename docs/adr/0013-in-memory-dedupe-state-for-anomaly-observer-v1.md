# ADR-0013: In-Memory Dedupe State for the Anomaly Observer v1

**Date:** 2026-05-23
**Status:** Accepted

---

## Context

The Anomaly Observer suppresses repeated emission of the same anomaly within a configurable dedupe window (default `5` cycles) and emits `Status = Resolved` reports when an active anomaly disappears for `2` consecutive cycles. Both behaviours require per-anomaly state keyed by `(AnomalyKind, ResourceKind, Namespace, Name)`.

The state can live in several places:

- **In-memory only** (`ConcurrentDictionary<DedupKey, ActiveAnomalyState>`). Zero deployment dependencies, zero schema migration, trivial to reason about. Restart loses all state, so the first post-restart cycle re-emits every currently active anomaly as a fresh report.
- **Single JSON file on a mounted volume.** ~30 lines of code (read on startup, write after each cycle). Survives restarts. Introduces a file-locking concern under concurrent `/observe-now` calls.
- **Reuse `InfraGate.Approvals.Postgres`.** Strongest "production-grade" option. ~3× the implementation cost, adds an operational dependency on Postgres for what is fundamentally ephemeral state, and conflates a side-quest concern with the existing approval persistence schema.

The Observer is currently positioned as an experimental reference implementation alongside the rest of InfraGate. The dedupe state has a useful but bounded lifetime — the next cycle re-derives "is this anomaly still active" from live cluster state, not from stored history. The state is an optimisation against noise, not a system of record.

## Decision

The Observer holds dedupe state **in process memory only** for v1. The store is `ConcurrentDictionary<DedupKey, ActiveAnomalyState>` behind an `IAnomalyDedupeStore` seam. There is no disk persistence, no database, and no replication. Restart and crash both reset the store to empty; the first post-restart cycle therefore re-emits every currently anomalous resource as a new `AnomalyReport`.

The seam exists explicitly so that v2 can introduce a persistent implementation without touching cycle orchestration. Likely v2 candidates are a single JSON file on the mounted findings volume, or a small table reusing the existing Postgres connection if `InfraGate.Approvals.Postgres` is in deployment.

## Consequences

- **A rolling restart will cause a re-emission storm** on any cluster that already has active anomalies. This is documented in `src/InfraGate.Observer/README.md` and in §1.6.10 / §4 of the implementation roadmap. Downstream sinks must be idempotent against re-emission (which they are: `AnomalyId` is a stable hash of `(Kind, ResourceRef)`, so executors can dedupe by ID across batches).
- **The Resolved-status mechanic does not survive restart.** An anomaly that was Active before restart and is absent after restart will not produce a `Status = Resolved` report. The executor sees the absence implicitly (no further reports), but does not get an explicit clearing event for the prior incident.
- The `IAnomalyDedupeStore` seam isolates the v2 migration cost. The cycle runner depends only on the interface; the in-memory implementation can be swapped without changes elsewhere.
- This decision should be revisited when (a) operators report annoyance with restart re-emission storms in practice, or (b) the Observer moves from reference implementation to a position where missed `Resolved` events affect downstream automation. Either trigger justifies promoting state to disk or Postgres.
