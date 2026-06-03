namespace InfraGate.AgentGuardrails;

public sealed record class ModelVisibleContentDecision
{
    public ModelVisibleContentDecision(
        ModelVisibleContentAction Action,
        string Text,
        IReadOnlyList<string> Categories,
        string Reason,
        string? Digest = null)
    {
        this.Action = Action;
        this.Text = Text;
        this.Categories = (Categories ?? []).ToArray();
        this.Reason = Reason;
        this.Digest = Digest;
    }

    public ModelVisibleContentAction Action { get; init; }
    public string Text { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string Reason { get; init; }
    public string? Digest { get; init; }
}
