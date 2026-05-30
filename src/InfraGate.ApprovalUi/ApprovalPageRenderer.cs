using InfraGate.ApprovalUi.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace InfraGate.ApprovalUi;

public sealed class ApprovalPageRenderer : IApprovalPageRenderer, IDisposable, IAsyncDisposable
{
    private readonly HtmlRenderer renderer;

    public ApprovalPageRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        renderer = new HtmlRenderer(serviceProvider, loggerFactory);
    }

    public Task<string> RenderApprovalPageAsync(ApprovalPageData pageData)
    {
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ApprovalPageContent>(
                ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["PageData"] = pageData
                })).ConfigureAwait(false);
            return component.ToHtmlString();
        });
    }

    public Task<string> RenderDecisionPageAsync(DecisionPageData decisionData)
    {
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<DecisionPage>(
                ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Data"] = decisionData
                })).ConfigureAwait(false);
            return component.ToHtmlString();
        });
    }

    public Task<string> RenderCodePageAsync(ApprovalCodePageData codePageData)
    {
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<CodePage>(
                ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Data"] = codePageData
                })).ConfigureAwait(false);
            return component.ToHtmlString();
        });
    }

    public void Dispose()
    {
        renderer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await renderer.DisposeAsync().ConfigureAwait(false);
    }
}
