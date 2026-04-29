namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static IDictionary<string, object?> CreateMetadata(DevIssuerOptions options)
    {
        return new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.Issuer] = TrimTrailingSlash(options.Issuer),
            [DevIssuerConventions.Json.AuthorizationEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Authorize),
            [DevIssuerConventions.Json.TokenEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Token),
            [DevIssuerConventions.Json.JwksUri] = Endpoint(options, DevIssuerConventions.Endpoints.Jwks),
            [DevIssuerConventions.Json.RegistrationEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Register),
            [DevIssuerConventions.Json.ResponseTypesSupported] = new[] { DevIssuerConventions.OAuth.CodeResponseType },
            [DevIssuerConventions.Json.GrantTypesSupported] = new[] { DevIssuerConventions.OAuth.AuthorizationCodeGrantType },
            [DevIssuerConventions.Json.CodeChallengeMethodsSupported] = new[] { DevIssuerConventions.OAuth.S256CodeChallengeMethod },
            [DevIssuerConventions.Json.TokenEndpointAuthMethodsSupported] = new[] { DevIssuerConventions.OAuth.NoneAuthenticationMethod },
            [DevIssuerConventions.Json.ScopesSupported] = new[] { options.Scope },
            [DevIssuerConventions.Json.SubjectTypesSupported] = new[] { DevIssuerConventions.OAuth.PublicSubjectType },
            [DevIssuerConventions.Json.IdTokenSigningAlgValuesSupported] = new[] { DevIssuerConventions.OAuth.RsaSha256Algorithm }
        };
    }

    private static string Endpoint(DevIssuerOptions options, string path)
    {
        return TrimTrailingSlash(options.Issuer) + path;
    }
}
