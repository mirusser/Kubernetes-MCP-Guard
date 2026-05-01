namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static readonly TimeSpan authorizationCodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan accessTokenLifetime = TimeSpan.FromMinutes(30);

    public static IServiceCollection AddDevIssuer(this IServiceCollection services, DevIssuerOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<DevIssuerStore>();
        services.AddSingleton<DevIssuerSigningKey>();

        return services;
    }

    public static IEndpointRouteBuilder MapDevIssuer(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.AuthorizationServerMetadata,
            (HttpRequest request, DevIssuerOptions options) => Results.Json(CreateMetadata(request, options)));
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.OpenIdConfiguration,
            (HttpRequest request, DevIssuerOptions options) => Results.Json(CreateMetadata(request, options)));
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.Jwks,
            (DevIssuerSigningKey signingKey) => Results.Json(signingKey.CreateJwks()));
        endpoints.MapPost(DevIssuerConventions.Endpoints.Register, RegisterClientAsync);
        endpoints.MapGet(DevIssuerConventions.Endpoints.Authorize, Authorize);
        endpoints.MapPost(DevIssuerConventions.Endpoints.Token, TokenAsync);

        return endpoints;
    }
}
