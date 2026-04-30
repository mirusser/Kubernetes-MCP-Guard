using System.Net;
using System.Net.Sockets;
using System.Text;
using InfraGate.McpServer;
using k8s;

namespace InfraGate.McpServer.Tests;

public sealed class K8sManagerSetImageTests
{
    [Fact]
    public async Task RequestSetDeploymentImageAsync_RejectsMissingContainer()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(DeploymentJson("nginx:1.27-alpine", includeSidecar: false)));
        var context = CreateManager(api);

        var result = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "sidecar",
            "nginx:1.28-alpine",
            CancellationToken.None);

        Assert.Contains("does not contain container 'sidecar'", result);
    }

    [Fact]
    public async Task RequestSetDeploymentImageAsync_CreatesPlanWithCurrentAndTargetImage()
    {
        await using var api = new TestKubernetesApi(_ => TestResponse.Json(DeploymentJson("nginx:1.27-alpine")));
        var context = CreateManager(api);

        var result = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            CancellationToken.None);
        var planId = ParsePlanId(result);
        var pending = await File.ReadAllTextAsync(
            Path.Combine(context.ApprovalRoot, "pending", $"{planId}.json"),
            CancellationToken.None);

        Assert.Contains("Operation: set-image", result);
        Assert.Contains("apps/v1 Deployment demo/demo", result);
        Assert.Contains("\"currentImage\": \"nginx:1.27-alpine\"", pending);
        Assert.Contains("\"image\": \"nginx:1.28-alpine\"", pending);
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_RefusesStaleDeploymentImage()
    {
        var requestReadCompleted = false;
        await using var api = new TestKubernetesApi(request =>
        {
            if (request.Path == "/apis/apps/v1/namespaces/demo/deployments/demo")
            {
                var image = requestReadCompleted ? "nginx:1.27.1-alpine" : "nginx:1.27-alpine";
                requestReadCompleted = true;

                return TestResponse.Json(DeploymentJson(image));
            }

            return TestResponse.Json("{}");
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            CancellationToken.None);
        var planId = await ApprovePlanAsync(context.ApprovalRoot, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);

        Assert.Contains("image changed from planned 'nginx:1.27-alpine' to 'nginx:1.27.1-alpine'", result);
        Assert.DoesNotContain(api.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task ApplyApprovedPlanAsync_PatchesOnlyPlannedContainerImage()
    {
        await using var api = new TestKubernetesApi(request => request.Path switch
        {
            "/apis/apps/v1/namespaces/demo/deployments/demo" => TestResponse.Json(DeploymentJson("nginx:1.27-alpine")),
            "/apis/apps/v1/namespaces/demo/deployments" => TestResponse.Json(ListJson("apps/v1", "DeploymentList", [DeploymentJson("nginx:1.28-alpine")])),
            "/api/v1/namespaces/demo/services" => TestResponse.Json(ListJson("v1", "ServiceList", [])),
            "/api/v1/namespaces/demo/configmaps" => TestResponse.Json(ListJson("v1", "ConfigMapList", [])),
            "/api/v1/namespaces/demo/pods" => TestResponse.Json(ListJson("v1", "PodList", [])),
            "/apis/apps/v1/namespaces/demo/replicasets" => TestResponse.Json(ListJson("apps/v1", "ReplicaSetList", [])),
            _ => TestResponse.Json(DeploymentJson("nginx:1.28-alpine"))
        });
        var context = CreateManager(api);
        var requestText = await context.Manager.RequestSetDeploymentImageAsync(
            "demo",
            "demo",
            "nginx",
            "nginx:1.28-alpine",
            CancellationToken.None);
        var planId = await ApprovePlanAsync(context.ApprovalRoot, requestText);

        var result = await context.Manager.ApplyApprovedPlanAsync(planId, CancellationToken.None);
        var patch = Assert.Single(api.Requests, request => request.Method == "PATCH");

        Assert.Contains("Updated apps/v1 Deployment demo/demo container 'nginx' image", result);
        Assert.Contains("\"name\":\"nginx\"", patch.Body);
        Assert.Contains("\"image\":\"nginx:1.28-alpine\"", patch.Body);
        Assert.DoesNotContain("sidecar", patch.Body);
    }

    private static ManagerContext CreateManager(TestKubernetesApi api)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-tests", Guid.NewGuid().ToString("N"));
        var options = new K8sMcpOptions(
            new HashSet<string>(StringComparer.Ordinal) { "demo" },
            root);
        var client = new Kubernetes(new KubernetesClientConfiguration
        {
            Host = api.Url,
            SkipTlsVerify = true
        });

        return new ManagerContext(new K8sManager(options, new ApprovalStore(options), client), root);
    }

    private static async Task<string> ApprovePlanAsync(string approvalRoot, string requestText)
    {
        var planId = ParsePlanId(requestText);
        var pendingPath = Path.Combine(approvalRoot, "pending", $"{planId}.json");
        var approvedPath = Path.Combine(approvalRoot, "approved", $"{planId}.sha256");
        var hash = await ApprovalStore.ComputeSha256Async(pendingPath, CancellationToken.None);
        await File.WriteAllTextAsync(approvedPath, hash, CancellationToken.None);

        return planId;
    }

    private static string ParsePlanId(string text) =>
        text.Split(Environment.NewLine)
            .Single(line => line.StartsWith("PlanId:", StringComparison.Ordinal))
            ["PlanId: ".Length..];

    private static string DeploymentJson(string image, bool includeSidecar = true)
    {
        var sidecar = includeSidecar
            ? """
              ,
                                  { "name": "sidecar", "image": "busybox:1.36" }
              """
            : string.Empty;

        return $$"""
                 {
                   "apiVersion": "apps/v1",
                   "kind": "Deployment",
                   "metadata": {
                     "name": "demo",
                     "namespace": "demo",
                     "generation": 1,
                     "labels": { "app": "demo" }
                   },
                   "spec": {
                     "replicas": 1,
                     "selector": { "matchLabels": { "app": "demo" } },
                     "template": {
                       "metadata": { "labels": { "app": "demo" } },
                       "spec": {
                         "containers": [
                           { "name": "nginx", "image": "{{image}}" }{{sidecar}}
                         ]
                       }
                     }
                   },
                   "status": {
                     "observedGeneration": 1,
                     "readyReplicas": 1,
                     "availableReplicas": 1,
                     "updatedReplicas": 1
                   }
                 }
                 """;
    }

    private static string ListJson(string apiVersion, string kind, IEnumerable<string> items) =>
        $$"""
          {
            "apiVersion": "{{apiVersion}}",
            "kind": "{{kind}}",
            "items": [
              {{string.Join(",", items)}}
            ]
          }
          """;

    private sealed record ManagerContext(K8sManager Manager, string ApprovalRoot);

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

        public List<CapturedRequest> Requests { get; } = [];

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

    private sealed record CapturedRequest(string Method, string Path, string Query, string Body);

    private sealed record TestResponse(int StatusCode, string ContentType, string Body)
    {
        public static TestResponse Json(string body) => new((int)HttpStatusCode.OK, "application/json", body);
    }
}
