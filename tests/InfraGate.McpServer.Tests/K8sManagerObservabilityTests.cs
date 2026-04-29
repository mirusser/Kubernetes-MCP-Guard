using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests;

public sealed class K8sManagerObservabilityTests
{
    [Fact]
    public async Task GetEventsAsync_RejectsDisallowedNamespace()
    {
        var manager = CreateManager();

        var result = await manager.GetEventsAsync("other", null, null, 50, CancellationToken.None);

        Assert.Contains("Namespace 'other' is not allowed", result);
    }

    [Fact]
    public async Task GetEventsAsync_RejectsLimitOutsideBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetEventsAsync("demo", null, null, 101, CancellationToken.None);

        Assert.Contains("Limit must be between 1 and 100", result);
    }

    [Fact]
    public async Task GetPodLogsAsync_RejectsTailLinesOutsideBounds()
    {
        var manager = CreateManager();

        var result = await manager.GetPodLogsAsync("demo", "demo-pod", null, 501, previous: false, CancellationToken.None);

        Assert.Contains("TailLines must be between 1 and 500", result);
    }

    [Fact]
    public async Task GetResourceAsync_RejectsSecretKind()
    {
        var manager = CreateManager();

        var result = await manager.GetResourceAsync("demo", "Secret", "demo-secret", CancellationToken.None);

        Assert.Contains("Secret resource details are intentionally unavailable", result);
    }

    [Fact]
    public async Task GetResourceAsync_RejectsUnsupportedKind()
    {
        var manager = CreateManager();

        var result = await manager.GetResourceAsync("demo", "Ingress", "demo-ingress", CancellationToken.None);

        Assert.Contains("Unsupported resource kind 'Ingress'", result);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsCompactEventSummary()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("""
                                                                           {
                                                                             "apiVersion": "events.k8s.io/v1",
                                                                             "kind": "EventList",
                                                                             "metadata": { "resourceVersion": "1" },
                                                                             "items": [
                                                                               {
                                                                                 "metadata": { "name": "demo-event", "namespace": "demo" },
                                                                                 "type": "Warning",
                                                                                 "reason": "Failed",
                                                                                 "action": "Pulling",
                                                                                 "note": "failed to pull image",
                                                                                 "eventTime": "2026-04-29T00:00:00Z",
                                                                                 "reportingController": "kubelet",
                                                                                 "reportingInstance": "node-1",
                                                                                 "regarding": {
                                                                                   "apiVersion": "v1",
                                                                                   "kind": "Pod",
                                                                                   "namespace": "demo",
                                                                                   "name": "demo-pod"
                                                                                 }
                                                                               }
                                                                             ]
                                                                           }
                                                                           """));
        var manager = CreateManager(api);

        var result = await manager.GetEventsAsync(
            "demo",
            "app=demo",
            "regarding.name=demo-pod",
            2,
            CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("demo", root.GetProperty("namespace").GetString());
        Assert.Equal(2, root.GetProperty("limit").GetInt32());
        Assert.Equal("demo-event", root.GetProperty("events")[0].GetProperty("name").GetString());
        Assert.Equal("Pod", root.GetProperty("events")[0].GetProperty("regarding").GetProperty("kind").GetString());
        Assert.Equal("/apis/events.k8s.io/v1/namespaces/demo/events", api.LastRequest?.Path);
        Assert.Contains("labelSelector=app%3Ddemo", api.LastRequest?.Query);
        Assert.Contains("fieldSelector=regarding.name%3Ddemo-pod", api.LastRequest?.Query);
        Assert.Contains("limit=2", api.LastRequest?.Query);
    }

    [Fact]
    public async Task GetPodLogsAsync_ReturnsBoundedLogSummary()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Text("line one\nline two"));
        var manager = CreateManager(api);

        var result = await manager.GetPodLogsAsync(
            "demo",
            "demo-pod",
            "web",
            7,
            previous: true,
            CancellationToken.None);

        Assert.StartsWith("{", result);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("demo-pod", root.GetProperty("podName").GetString());
        Assert.Equal("web", root.GetProperty("container").GetString());
        Assert.Equal(7, root.GetProperty("tailLines").GetInt32());
        Assert.Equal(65536, root.GetProperty("limitBytes").GetInt32());
        Assert.Equal("line one\nline two", root.GetProperty("log").GetString());
        Assert.Equal("/api/v1/namespaces/demo/pods/demo-pod/log", api.LastRequest?.Path);
        Assert.Contains("container=web", api.LastRequest?.Query);
        Assert.Contains("limitBytes=65536", api.LastRequest?.Query);
        Assert.Contains("previous=true", api.LastRequest?.Query);
        Assert.Contains("tailLines=7", api.LastRequest?.Query);
    }

    [Fact]
    public async Task GetResourceAsync_ReturnsConfigMapSummaryWithoutValues()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json("""
                                                                           {
                                                                             "apiVersion": "v1",
                                                                             "kind": "ConfigMap",
                                                                             "metadata": {
                                                                               "name": "demo-config",
                                                                               "namespace": "demo",
                                                                               "labels": { "app": "demo" }
                                                                             },
                                                                             "immutable": true,
                                                                             "data": {
                                                                               "password": "supersecret",
                                                                               "setting": "enabled"
                                                                             },
                                                                             "binaryData": {
                                                                               "blob": "AAAA"
                                                                             }
                                                                           }
                                                                           """));
        var manager = CreateManager(api);

        var result = await manager.GetResourceAsync("demo", "ConfigMap", "demo-config", CancellationToken.None);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        Assert.Equal("ConfigMap", root.GetProperty("kind").GetString());
        Assert.Equal("demo-config", root.GetProperty("name").GetString());
        Assert.Contains(root.GetProperty("dataKeys").EnumerateArray(), key => key.GetString() == "password");
        Assert.Contains(root.GetProperty("binaryDataKeys").EnumerateArray(), key => key.GetString() == "blob");
        Assert.DoesNotContain("supersecret", result);
        Assert.DoesNotContain("enabled", result);
        Assert.DoesNotContain("AAAA", result);
        Assert.Equal("/api/v1/namespaces/demo/configmaps/demo-config", api.LastRequest?.Path);
    }

    private static K8sManager CreateManager(TestKubernetesApi? api = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { "demo" },
            root);
        var client = api is null
            ? null
            : new Kubernetes(new KubernetesClientConfiguration
            {
                Host = api.Url,
                SkipTlsVerify = true
            });

        return new K8sManager(options, new ApprovalStore(options), client!);
    }

    private sealed class TestKubernetesApi : IAsyncDisposable
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

        public CapturedRequest? LastRequest { get; private set; }

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
            var request = new CapturedRequest(
                context.Request.HttpMethod,
                context.Request.Url?.AbsolutePath ?? string.Empty,
                context.Request.Url?.Query.TrimStart('?') ?? string.Empty);
            LastRequest = request;

            var response = handler(request);
            var body = Encoding.UTF8.GetBytes(response.Body);
            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }

        private static int GetFreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, port: 0);
            socket.Start();

            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string Query);

    private sealed record TestResponse(int StatusCode, string ContentType, string Body)
    {
        public static TestResponse Json(string body) => new((int)HttpStatusCode.OK, "application/json", body);

        public static TestResponse Text(string body) => new((int)HttpStatusCode.OK, "text/plain", body);
    }
}
