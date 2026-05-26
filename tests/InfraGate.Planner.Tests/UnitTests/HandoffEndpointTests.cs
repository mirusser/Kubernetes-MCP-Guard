// ASPDEPR004/ASPDEPR008: suppressed — see HealthEndpointTests.cs rationale.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using InfraGate.Observer.Contracts;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class HandoffEndpointTests
{
    private static readonly AnomalyHandoffBatch ValidBatch = new()
    {
        CycleId = "cycle-1",
        EmittedAt = DateTimeOffset.UtcNow,
        Reports = [],
    };

    [Fact]
    public async Task PostHandoff_NoAuthHeader_Returns401()
    {
        using var server = CreateServer(TestAuthMode.Unauthenticated);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            PlannerConventions.HandoffAnomaliesEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_WrongAzpClaim_Returns403()
    {
        using var server = CreateServer(TestAuthMode.WrongAzp);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            PlannerConventions.HandoffAnomaliesEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_ValidObserverBatch_Returns202()
    {
        using var server = CreateServer(TestAuthMode.ValidObserver);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync(
            PlannerConventions.HandoffAnomaliesEndpointPath, ValidBatch);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostHandoff_ValidBatch_BatchEnqueuedInQueue()
    {
        var queue = new AnomalyBatchQueue();
        using var server = CreateServer(TestAuthMode.ValidObserver, queue);
        using var client = server.CreateClient();

        await client.PostAsJsonAsync(PlannerConventions.HandoffAnomaliesEndpointPath, ValidBatch);

        bool hasItem = queue.Reader.TryRead(out var dequeued);
        Assert.True(hasItem);
        Assert.Equal(ValidBatch.CycleId, dequeued!.CycleId);
    }

    [Fact]
    public async Task PostHandoff_MalformedJson_Returns400()
    {
        using var server = CreateServer(TestAuthMode.ValidObserver);
        using var client = server.CreateClient();
        using var content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(PlannerConventions.HandoffAnomaliesEndpointPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static TestServer CreateServer(TestAuthMode mode, AnomalyBatchQueue? queue = null)
    {
        queue ??= new AnomalyBatchQueue();

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
                    .AddPolicy(PlannerConventions.Policies.ObserverSender, policy =>
                        policy
                            .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                            .RequireAuthenticatedUser()
                            .RequireClaim(PlannerConventions.Claims.AuthorizedParty,
                                          PlannerConventions.ServiceClients.Observer));
            })
            .Configure(app =>
            {
                // BadHttpRequestException (malformed JSON) is normally converted to 400 by
                // WebApplication's built-in exception handler; replicate that in TestServer.
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
                    endpoints.MapPlannerHandoffEndpoint();
                });
            }));
    }

    internal enum TestAuthMode
    {
        Unauthenticated,
        WrongAzp,
        ValidObserver,
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
                                [new Claim(PlannerConventions.Claims.AuthorizedParty, "infra-gate-other")],
                                SchemeName)),
                            SchemeName))),

                TestAuthMode.ValidObserver =>
                    Task.FromResult(AuthenticateResult.Success(
                        new AuthenticationTicket(
                            new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(PlannerConventions.Claims.AuthorizedParty, PlannerConventions.ServiceClients.Observer)],
                                SchemeName)),
                            SchemeName))),

                _ => Task.FromResult(AuthenticateResult.Fail("Unknown mode")),
            };
        }
    }
}
