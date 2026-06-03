using System.Security.Cryptography;
using System.Text;

namespace InfraGate.AgentGuardrails;

public sealed class CompositeModelVisibleContentGuard(
    IReadOnlyList<IModelVisibleContentGuard> guards,
    AgentGuardrailMetrics metrics,
    ILogger<CompositeModelVisibleContentGuard> logger,
    ModelVisibleContentOptions options,
    IModelVisibleContentAudit? audit = null) : IModelVisibleContentGuard
{
    public async Task<ModelVisibleContentDecision> EvaluateAsync(
        ModelVisibleContent content,
        CancellationToken cancellationToken)
    {
        if (content.Text.Length > options.MaximumInputCharacters)
        {
            var oversizedDecision = new ModelVisibleContentDecision(
                ModelVisibleContentAction.Quarantine,
                AgentGuardrailConventions.DefaultQuarantinePlaceholder,
                [],
                AgentGuardrailConventions.Reasons.ExceededMaximumInputCharacters);

            metrics.RecordModelVisibleDecision(
                ModelVisibleContentAction.Quarantine,
                content.Source,
                content.AgentName,
                evaluationDurationMs: 0);

            if (audit is not null)
            {
                try
                {
                    var blockDigest = ComputeDigest(content.Text);
                    await audit.PersistAsync(blockDigest, content.Source, content.AgentName, oversizedDecision, cancellationToken)
                        .ConfigureAwait(false);
                }
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    logger.LogError(ex, "Model-visible content audit persistence failed for {Source}/{AgentName}",
                        content.Source, content.AgentName);
                    metrics.RecordModelVisibleDegraded(content.Source, content.AgentName);
                }
            }

            return oversizedDecision;
        }

        var sw = Stopwatch.StartNew();

        var strongestAction = ModelVisibleContentAction.Allow;
        IReadOnlyList<string> strongestCategories = [];
        string strongestReason = AgentGuardrailConventions.Reasons.None;
        var current = content;

        foreach (var guard in guards)
        {
            var decision = await guard.EvaluateAsync(current, cancellationToken).ConfigureAwait(false);

            if (ActionStrength(decision.Action) > ActionStrength(strongestAction))
            {
                strongestAction = decision.Action;
                strongestCategories = decision.Categories;
                strongestReason = decision.Reason;
            }

            if (decision.Action == ModelVisibleContentAction.Redact)
                current = current with { Text = decision.Text };

            if (strongestAction == ModelVisibleContentAction.BlockModelIngestion)
                break;
        }

        string modelText = strongestAction switch
        {
            ModelVisibleContentAction.Quarantine => AgentGuardrailConventions.DefaultQuarantinePlaceholder,
            ModelVisibleContentAction.BlockModelIngestion => AgentGuardrailConventions.DefaultBlockedPlaceholder,
            _ => current.Text,
        };

        string? digest = null;
        if (strongestAction == ModelVisibleContentAction.Quarantine ||
            strongestAction == ModelVisibleContentAction.BlockModelIngestion)
        {
            digest = ComputeDigest(content.Text);
        }

        var final = new ModelVisibleContentDecision(strongestAction, modelText, strongestCategories, strongestReason, digest);

        sw.Stop();
        metrics.RecordModelVisibleDecision(strongestAction, content.Source, content.AgentName, sw.Elapsed.TotalMilliseconds);

        if (digest is not null && audit is not null)
        {
            try
            {
                await audit.PersistAsync(digest, content.Source, content.AgentName, final, cancellationToken)
                    .ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Broad catch is intentional: audit write failure must never suppress the guard's block decision.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Model-visible content audit persistence failed for {Source}/{AgentName}",
                    content.Source, content.AgentName);
                metrics.RecordModelVisibleDegraded(content.Source, content.AgentName);
            }
        }

        return final;
    }

    private static string ComputeDigest(string text)
    {
        var inputBytes = Encoding.UTF8.GetBytes(text);
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static int ActionStrength(ModelVisibleContentAction action) => action switch
    {
        ModelVisibleContentAction.Allow => 0,
        ModelVisibleContentAction.Redact => 1,
        ModelVisibleContentAction.Quarantine => 2,
        ModelVisibleContentAction.BlockModelIngestion => 3,
        // Defensive against invalid enum values from external sources.
        _ => 0,
    };
}
