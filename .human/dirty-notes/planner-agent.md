
The intended v1 shape from the roadmap is:

  1. Observer emits AnomalyHandoffBatch.
     The anomaly may include Suggested, but that is advisory input only.
  2. InfraGate.Planner process receives the batch at /handoff/anomalies.
  3. BatchProcessor is the Planner orchestrator.
     It filters/dedupes anomalies, gives the LLM the anomaly JSON, and allows bounded read-only investigation tool calls.
  4. The LLM does not call propose_plan.
     It returns decision JSON that is parsed into RemediationDecision { OperationType, Arguments, Reasoning }.
  5. BatchProcessor validates the decision.
     It checks operation allowlist and argument schema.
  6. BatchProcessor is the only Planner-side caller of propose_plan.
     After validation, it calls the gateway tool and gets back planId.
  7. Planner emits RemediationProposal to Executor.
     The proposal carries planId, anomalyId, proposedAt; execution waits for human approval later.