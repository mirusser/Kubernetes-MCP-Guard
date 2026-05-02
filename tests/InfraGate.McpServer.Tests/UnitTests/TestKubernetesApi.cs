using System.Net;
using System.Net.Sockets;
using System.Text;

namespace InfraGate.McpServer.Tests.UnitTests;

internal sealed class TestKubernetesApi : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly Func<CapturedRequest, TestResponse> handler;
    private readonly Task listenTask;

    public TestKubernetesApi(Func<CapturedRequest, TestResponse> handler)
    {
        this.handler = handler;
        Url = $"http://127.0.0.1:{GetFreePort()}";
        listener.Prefixes.Add($"{Url}/");
        listener.Start();
        listenTask = Task.Run(ListenAsync);
    }

    public string Url { get; }

    public List<CapturedRequest> Requests { get; } = [];

    public CapturedRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        listener.Close();

        try
        {
            await listenTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or TimeoutException)
        {
        }
    }

    private async Task ListenAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            await HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        var request = new CapturedRequest(
            context.Request.HttpMethod,
            context.Request.Url?.AbsolutePath ?? string.Empty,
            context.Request.Url?.Query.TrimStart('?') ?? string.Empty,
            body);
        Requests.Add(request);

        var response = handler(request);
        var responseBody = Encoding.UTF8.GetBytes(response.Body);
        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;
        context.Response.ContentLength64 = responseBody.Length;
        await context.Response.OutputStream.WriteAsync(responseBody);
        context.Response.Close();
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, port: 0);
        socket.Start();

        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}

internal sealed record CapturedRequest(string Method, string Path, string Query, string Body);

internal sealed record TestResponse(int StatusCode, string ContentType, string Body)
{
    public static TestResponse Json(string body, int statusCode = (int)HttpStatusCode.OK) =>
        new(statusCode, "application/json", body);

    public static TestResponse Text(string body, int statusCode = (int)HttpStatusCode.OK) =>
        new(statusCode, "text/plain", body);
}
