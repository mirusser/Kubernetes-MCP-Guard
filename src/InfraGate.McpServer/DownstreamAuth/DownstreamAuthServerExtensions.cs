using InfraGate.DownstreamAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpServer.DownstreamAuth;

internal static class DownstreamAuthServerExtensions
{
    /// <summary>
    /// Registers <see cref="DownstreamTokenValidator"/> when downstream auth is required.
    /// When <see cref="DownstreamAuthOptions.Required"/> is false, no validator is registered
    /// and the auth filter passes all requests through without validation.
    /// </summary>
    internal static IServiceCollection AddDownstreamAuth(
        this IServiceCollection services,
        DownstreamAuthOptions? options)
    {
        var authOptions = options ?? new DownstreamAuthOptions();

        if (!authOptions.Required)
        {
            return services;
        }

        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DownstreamTokenValidator>>();
            return new DownstreamTokenValidator(authOptions, logger);
        });

        return services;
    }
}
