using System.ClientModel.Primitives;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        NormalizeResponse(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Set(RefererHeaderName, refererUrl);
        message.Request.Headers.Set(TitleHeaderName, appTitle);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        await NormalizeResponseAsync(message).ConfigureAwait(false);
    }

    private static void NormalizeResponse(PipelineMessage message)
    {
        var response = message.Response;
        if (response?.ContentStream is null) return;
        
        // System.ClientModel buffers HTTP responses by default. 
        response.ContentStream.Position = 0;
        using var reader = new StreamReader(response.ContentStream, leaveOpen: true);
        var content = reader.ReadToEnd();
        ReplaceFinishReason(response, content);
    }

    private static async ValueTask NormalizeResponseAsync(PipelineMessage message)
    {
        var response = message.Response;
        if (response?.ContentStream is null) return;

        response.ContentStream.Position = 0;
        using var reader = new StreamReader(response.ContentStream, leaveOpen: true);
        var content = await reader.ReadToEndAsync(message.CancellationToken).ConfigureAwait(false);
        ReplaceFinishReason(response, content);
    }

    private static void ReplaceFinishReason(PipelineResponse response, string content)
    {
        var modified = Regex.Replace(
            content,
            @"""finish_reason""\s*:\s*""(?!stop|length|content_filter|tool_calls|function_call)([^""]+)""",
            @"""finish_reason"":""stop""",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)
        );

        if (!ReferenceEquals(content, modified) && modified.Length != content.Length || !string.Equals(content, modified, System.StringComparison.Ordinal))
        {
            response.ContentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(modified));
        }
        else
        {
            response.ContentStream!.Position = 0;
        }
    }
}
