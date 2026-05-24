# ADR-0014: Per-Resource Anomaly Reports, Not Workload-Aggregated

**Date:** 2026-05-23
**Status:** Accepted

---

## Context

When a Kubernetes workload fails, several resources show symptoms at once. The canonical example: a Deployment with a typo'd image causes both Pods to enter `ImagePullBackOff`, the Deployment's `availableReplicas` drops to `0`, the backing Service loses its endpoints, and Warning events fire repeatedly.

There are two coherent ways for the Anomaly Observer to report this:

- **Workload-aggregated.** Emit a single `DeploymentUnavailable` report whose `Evidence` lists the affected Pods and the loss of Service endpoints. Per-pod details live inside the parent report. Fewer reports per cycle; the executor sees one anomaly to act on.
- **Per-resource.** Emit a separate `AnomalyReport` for each resource showing a symptom: one `DeploymentUnavailable`, one `ServiceNoEndpoints`, one `PodUnhealthy` per failing Pod, one `WarningEvent` per recent event. Stable `AnomalyId` per resource; more reports per cycle.

Aggregation requires a grouping step: the Observer (or the LLM) must decide which Pod symptoms belong to which Deployment, and a Service may belong to a Deployment by selector matching that is not always 1:1. The `AnomalyId` dedupe key would have to widen to include "the set of contributing resources," which makes Resolved-emission ambiguous when a subset clears.

Per-resource reporting keeps the classification logic simple: one signal → one report → one stable `AnomalyId`. The price is that a single broken Deployment produces 4+ reports per cycle.

## Decision

V1 of the Anomaly Observer emits **per-resource** `AnomalyReport`s. Each anomaly is independently keyed by `(AnomalyKind, ResourceKind, Namespace, Name)` for the dedupe store, and each report's `AnomalyId` is a stable hash of those four values. The cycle runner does no grouping or aggregation.

The executor (out of v1 scope) is the appropriate place to correlate related reports by ownership references, label selectors, or proximity in time. Doing the correlation there keeps the Observer's contract minimal and lets different consumers correlate differently.

## Consequences

- **Higher report volume per cycle on broken workloads.** A Deployment with two failing pods plus its Service yields 4 reports in v1 versus 1 in the aggregated model. Dedupe keeps the noise within a cycle window manageable; cross-cycle dedupe means the steady-state cost is "4 reports emitted once" rather than "4 reports emitted every cycle."
- **`AnomalyId` stability is per-resource.** This is the right semantics for Resolved emission: when the Service gets endpoints back but Pods are still failing, the `ServiceNoEndpoints` report transitions to `Status = Resolved` independently of the still-Active `PodUnhealthy` reports.
- **Executors get both granularities for free.** They can act at the workload level (group by owner references in code) or at the resource level (act on each report). The Observer doesn't pre-decide for them.
- **Sinks see a clean shape.** `AnomalyHandoffBatch` is a flat list; consumers do not have to recursively walk evidence to discover sub-anomalies.
- The decision should be revisited if (a) operators report alert fatigue from large clusters, or (b) the executor consistently does the same grouping logic — at which point a configurable "aggregate before handoff" mode becomes a sensible v2 addition.
