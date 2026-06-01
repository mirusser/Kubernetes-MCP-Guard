using System.Security.Claims;
using InfraGate.Observer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverA2AAuthTests
{
    [Fact]
    public async Task PlannerSenderPolicy_AllowsValidAzp()
    {
        var services = new ServiceCollection();
        services.AddAuthorizationBuilder()
            .AddPolicy(ObserverConventions.Policies.PlannerSender, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ObserverConventions.Claims.AuthorizedParty, ObserverConventions.ServiceClients.Planner);
            });
        services.AddLogging();
        services.AddOptions();
        using var provider = services.BuildServiceProvider();

        var authService = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ObserverConventions.Claims.AuthorizedParty, ObserverConventions.ServiceClients.Planner)], "jwt"));

        var result = await authService.AuthorizeAsync(user, ObserverConventions.Policies.PlannerSender);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PlannerSenderPolicy_RejectsInvalidAzp()
    {
        var services = new ServiceCollection();
        services.AddAuthorizationBuilder()
            .AddPolicy(ObserverConventions.Policies.PlannerSender, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ObserverConventions.Claims.AuthorizedParty, ObserverConventions.ServiceClients.Planner);
            });
        services.AddLogging();
        services.AddOptions();
        using var provider = services.BuildServiceProvider();

        var authService = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ObserverConventions.Claims.AuthorizedParty, "some-other-client")], "jwt"));

        var result = await authService.AuthorizeAsync(user, ObserverConventions.Policies.PlannerSender);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PlannerSenderPolicy_RejectsUnauthenticated()
    {
        var services = new ServiceCollection();
        services.AddAuthorizationBuilder()
            .AddPolicy(ObserverConventions.Policies.PlannerSender, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ObserverConventions.Claims.AuthorizedParty, ObserverConventions.ServiceClients.Planner);
            });
        services.AddLogging();
        services.AddOptions();
        using var provider = services.BuildServiceProvider();

        var authService = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await authService.AuthorizeAsync(user, ObserverConventions.Policies.PlannerSender);

        Assert.False(result.Succeeded);
    }
}
