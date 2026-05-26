// ASPDEPR004/ASPDEPR008: suppressed — see HealthEndpointTests.cs rationale.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using InfraGate.Executor.Endpoints;
using InfraGate.Executor.Queue;
using InfraGate.Remediation.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class HandoffEndpointTests
{
    private static readonly RemediationProposalBatch ValidBatch = new()
    {
        CycleId = "cycle-1",
        EmittedAt = DateTimeOffset.UtcNow,
        Proposals = [],
    };

    private static RemediationProposalBatch BatchWithProposals(int count) =>
        new()
        {
            CycleId = "cycle-1",
            EmittedAt = DateTimeOffset.UtcNow,
            Proposals = Enumerable.Range(1, count)
                .Select(i => new RemediationProposal
                {
                    PlanId = $"plan-{i}",
                    AnomalyId = $"anomaly-{i}",
                    ProposedAt = DateTimeOffset.UtcNow,
                })
                .ToList(),
        };

    [Fact]
    public async Task PostHandoff_NoAuthHeader_Returns401()
    {
        using var server = CreateServer(TestAuthMode.Unauthenticated);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ExecutorConventions.HandoffProposalsEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_WrongAzpClaim_Returns403()
    {
        using var server = CreateServer(TestAuthMode.WrongAzp);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ExecutorConventions.HandoffProposalsEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_ValidPlannerBatch_Returns202()
    {
        using var server = CreateServer(TestAuthMode.ValidPlanner);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ExecutorConventions.HandoffProposalsEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_ValidBatch_ProposalsReachQueue()
    {
        var queue = CreateQueue(cap: 4);
        using var server = CreateServer(TestAuthMode.ValidPlanner, queue);
        using var client = server.CreateClient();
        var batch = BatchWithProposals(2);

        await client.PostAsJsonAsync(ExecutorConventions.HandoffProposalsEndpointPath, batch);

        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("plan-1", first!.PlanId);
        Assert.True(queue.Reader.TryRead(out var second));
        Assert.Equal("plan-2", second!.PlanId);
    }

    [Fact]
    public async Task PostHandoff_ConcurrencyCapExceeded_Returns429()
    {
        var queue = CreateQueue(cap: 1);
        using var server = CreateServer(TestAuthMode.ValidPlanner, queue);
        using var client = server.CreateClient();
        var batch = BatchWithProposals(2);

        using var response = await client.PostAsJsonAsync(
            ExecutorConventions.HandoffProposalsEndpointPath, batch);

        Assert.Equal(429, (int)response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_MalformedJson_Returns400()
    {
        using var server = CreateServer(TestAuthMode.ValidPlanner);
        using var client = server.CreateClient();
        using var content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(ExecutorConventions.HandoffProposalsEndpointPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ProposalQueue CreateQueue(int cap) =>
        new(Options.Create(new ExecutorOptions { GatewayBaseUrl = "http://localhost", ConcurrencyCap = cap }));

    private static TestServer CreateServer(TestAuthMode mode, ProposalQueue? queue = null)
    {
        queue ??= CreateQueue(cap: 64);

        return new TestServer(new WebHostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services =>
            {
                services.AddSingleton(queue);
                services.AddRouting();
                services.AddLogging();

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        options => options.Mode = mode);

                services
                    .AddAuthorizationBuilder()
                    .AddPolicy(ExecutorConventions.Policies.PlannerSender, policy =>
                        policy
                            .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                            .RequireAuthenticatedUser()
                            .RequireClaim(ExecutorConventions.Claims.AuthorizedParty,
                                          ExecutorConventions.ServiceClients.Planner));
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    try
                    {
                        await next(context).ConfigureAwait(false);
                    }
                    catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex)
                    {
                        context.Response.StatusCode = ex.StatusCode;
                    }
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapExecutorHandoffEndpoint();
                });
            }));
    }

    internal enum TestAuthMode
    {
        Unauthenticated,
        WrongAzp,
        ValidPlanner,
    }

    internal sealed class TestAuthHandlerOptions : AuthenticationSchemeOptions
    {
        public TestAuthMode Mode { get; set; }
    }

    internal sealed class TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<TestAuthHandlerOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestScheme";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Options.Mode switch
            {
                TestAuthMode.Unauthenticated =>
                    Task.FromResult(AuthenticateResult.Fail("No credentials")),

                TestAuthMode.WrongAzp =>
                    Task.FromResult(AuthenticateResult.Success(
                        new AuthenticationTicket(
                            new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ExecutorConventions.Claims.AuthorizedParty, "infra-gate-other")],
                                SchemeName)),
                            SchemeName))),

                TestAuthMode.ValidPlanner =>
                    Task.FromResult(AuthenticateResult.Success(
                        new AuthenticationTicket(
                            new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ExecutorConventions.Claims.AuthorizedParty, ExecutorConventions.ServiceClients.Planner)],
                                SchemeName)),
                            SchemeName))),

                _ => Task.FromResult(AuthenticateResult.Fail("Unknown mode")),
            };
        }
    }
}
