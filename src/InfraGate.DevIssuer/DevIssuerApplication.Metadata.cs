namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static Dictionary<string, object?> CreateMetadata(HttpRequest request, DevIssuerOptions options)
    {
        var endpointBase = GetEndpointBase(request, options);

        return new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.Issuer] = TrimTrailingSlash(options.Issuer),
            [DevIssuerConventions.Json.AuthorizationEndpoint] = Endpoint(endpointBase, DevIssuerConventions.Endpoints.Authorize),
            [DevIssuerConventions.Json.TokenEndpoint] = Endpoint(endpointBase, DevIssuerConventions.Endpoints.Token),
            [DevIssuerConventions.Json.JwksUri] = Endpoint(endpointBase, DevIssuerConventions.Endpoints.Jwks),
            [DevIssuerConventions.Json.RegistrationEndpoint] = Endpoint(endpointBase, DevIssuerConventions.Endpoints.Register),
            [DevIssuerConventions.Json.ResponseTypesSupported] = new[] { DevIssuerConventions.OAuth.CodeResponseType },
            [DevIssuerConventions.Json.GrantTypesSupported] = new[] { DevIssuerConventions.OAuth.AuthorizationCodeGrantType },
            [DevIssuerConventions.Json.CodeChallengeMethodsSupported] = new[] { DevIssuerConventions.OAuth.S256CodeChallengeMethod },
            [DevIssuerConventions.Json.TokenEndpointAuthMethodsSupported] = new[] { DevIssuerConventions.OAuth.NoneAuthenticationMethod },
            [DevIssuerConventions.Json.ScopesSupported] = new[] { options.Scope },
            [DevIssuerConventions.Json.SubjectTypesSupported] = new[] { DevIssuerConventions.OAuth.PublicSubjectType },
            [DevIssuerConventions.Json.IdTokenSigningAlgValuesSupported] = new[] { DevIssuerConventions.OAuth.RsaSha256Algorithm }
        };
    }

    private static string GetEndpointBase(HttpRequest request, DevIssuerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InternalEndpointBase) &&
            Uri.TryCreate(options.InternalEndpointBase, UriKind.Absolute, out var internalEndpointBase) &&
            string.Equals(request.Host.Host, internalEndpointBase.Host, StringComparison.OrdinalIgnoreCase))
        {
            return options.InternalEndpointBase;
        }

        return options.Issuer;
    }

    private static string Endpoint(string endpointBase, string path)
    {
        return TrimTrailingSlash(endpointBase) + path;
    }
}
