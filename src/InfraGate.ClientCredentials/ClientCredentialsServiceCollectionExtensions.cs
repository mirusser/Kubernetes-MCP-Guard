using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InfraGate.ClientCredentials;

public static class ClientCredentialsServiceCollectionExtensions
{
    public static IServiceCollection AddClientCredentialsTokenProvider(
        this IServiceCollection services,
        ClientCredentialsTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.AddHttpClient();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IClientCredentialsTokenProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new ClientCredentialsTokenProvider(
                options,
                httpClientFactory.CreateClient(nameof(ClientCredentialsTokenProvider)),
                TimeProvider.System,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClientCredentialsTokenProvider>>());
        });

        return services;
    }

    public static IServiceCollection AddClientCredentialsBearerHandler(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ClientCredentialsBearerHandler>(sp =>
        {
            var tokenProvider = sp.GetRequiredService<IClientCredentialsTokenProvider>();
            return new ClientCredentialsBearerHandler(
                tokenProvider,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClientCredentialsBearerHandler>>());
        });

        return services;
    }

    public static IHttpClientBuilder AddClientCredentialsBearerHandler(
        this IHttpClientBuilder builder)
    {
        builder.Services.AddClientCredentialsBearerHandler();
        builder.AddHttpMessageHandler(sp =>
            sp.GetRequiredService<ClientCredentialsBearerHandler>());

        return builder;
    }
}
