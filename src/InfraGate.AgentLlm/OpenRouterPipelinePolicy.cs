using System.ClientModel.Primitives;

namespace InfraGate.AgentLlm;

public sealed class OpenRouterPipelinePolicy : PipelinePolicy
{
    private const string RefererHeaderName = "HTTP-Referer";
    private const string TitleHeaderName = "X-Title";

    public const string DefaultRefererUrl = "https://github.com/mirusser/Kubernetes-MCP-Guard";
    public const string DefaultAppTitle = "infra-gate";

    public static readonly OpenRouterPipelinePolicy Default = new();

    private readonly string refererUrl;
    private readonly string appTitle;

    public OpenRouterPipelinePolicy(string refererUrl = DefaultRefererUrl, string appTitle = DefaultAppTitle)
    {
        this.refererUrl = refererUrl;
        this.appTitle = appTitle;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Set(RefererHeaderName, refererUrl);
        message.Request.Headers.Set(TitleHeaderName, appTitle);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Set(RefererHeaderName, refererUrl);
        message.Request.Headers.Set(TitleHeaderName, appTitle);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }
}
