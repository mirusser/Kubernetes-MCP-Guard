namespace InfraGate.DevIssuer;

internal static class DevIssuerConventions
{
    public const string DefaultUrl = "http://127.0.0.1:3011";
    public const string DefaultResource = "http://127.0.0.1:3001/mcp";
    public const string DefaultScope = "mcp:tools";
    public const string DefaultSubject = "infra-gate-dev-user";

    public static class EnvironmentVariables
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string Issuer = "INFRA_GATE_DEV_ISSUER_ISSUER";
        public const string Resource = "INFRA_GATE_DEV_ISSUER_RESOURCE";
        public const string Scope = "INFRA_GATE_DEV_ISSUER_SCOPE";
        public const string Subject = "INFRA_GATE_DEV_ISSUER_SUBJECT";
    }

    public static class ConfigurationKeys
    {
        public const string Urls = "urls";
    }

    public static class Endpoints
    {
        public const string AuthorizationServerMetadata = "/.well-known/oauth-authorization-server";
        public const string OpenIdConfiguration = "/.well-known/openid-configuration";
        public const string Jwks = "/jwks";
        public const string Register = "/register";
        public const string Authorize = "/authorize";
        public const string Token = "/token";
    }

    public static class OAuth
    {
        public const string AuthorizationCodeGrantType = "authorization_code";
        public const string CodeResponseType = "code";
        public const string NoneAuthenticationMethod = "none";
        public const string S256CodeChallengeMethod = "S256";
        public const string BearerTokenType = "Bearer";
        public const string PublicSubjectType = "public";
        public const string RsaKeyType = "RSA";
        public const string SignatureKeyUse = "sig";
        public const string RsaSha256Algorithm = "RS256";
    }

    public static class Parameters
    {
        public const string ClientId = "client_id";
        public const string Code = "code";
        public const string CodeChallenge = "code_challenge";
        public const string CodeChallengeMethod = "code_challenge_method";
        public const string CodeVerifier = "code_verifier";
        public const string GrantType = "grant_type";
        public const string RedirectUri = "redirect_uri";
        public const string Resource = "resource";
        public const string ResponseType = "response_type";
        public const string Scope = "scope";
        public const string State = "state";
    }

    public static class Json
    {
        public const string AccessToken = "access_token";
        public const string Algorithm = "alg";
        public const string AuthorizationEndpoint = "authorization_endpoint";
        public const string AuthorizationServer = "authorization_server";
        public const string ClientId = "client_id";
        public const string ClientName = "client_name";
        public const string CodeChallengeMethodsSupported = "code_challenge_methods_supported";
        public const string Error = "error";
        public const string ErrorDescription = "error_description";
        public const string ExpiresIn = "expires_in";
        public const string GrantTypes = "grant_types";
        public const string GrantTypesSupported = "grant_types_supported";
        public const string IdTokenSigningAlgValuesSupported = "id_token_signing_alg_values_supported";
        public const string Issuer = "issuer";
        public const string JsonWebKeyExponent = "e";
        public const string JsonWebKeyModulus = "n";
        public const string JsonWebKeyType = "kty";
        public const string KeyId = "kid";
        public const string Keys = "keys";
        public const string JwksUri = "jwks_uri";
        public const string RedirectUris = "redirect_uris";
        public const string RegistrationEndpoint = "registration_endpoint";
        public const string ResponseTypes = "response_types";
        public const string ResponseTypesSupported = "response_types_supported";
        public const string Scope = "scope";
        public const string ScopesSupported = "scopes_supported";
        public const string SubjectTypesSupported = "subject_types_supported";
        public const string TokenEndpoint = "token_endpoint";
        public const string TokenEndpointAuthMethod = "token_endpoint_auth_method";
        public const string TokenEndpointAuthMethodsSupported = "token_endpoint_auth_methods_supported";
        public const string TokenType = "token_type";
        public const string Use = "use";
    }

    public static class Claims
    {
        public const string ClientId = "client_id";
        public const string JwtId = "jti";
        public const string PreferredUsername = "preferred_username";
        public const string Scope = "scope";
        public const string Subject = "sub";
    }

    public static class Errors
    {
        public const string InvalidGrant = "invalid_grant";
        public const string InvalidRequest = "invalid_request";
        public const string InvalidScope = "invalid_scope";
        public const string UnauthorizedClient = "unauthorized_client";
        public const string UnsupportedGrantType = "unsupported_grant_type";
        public const string UnsupportedResponseType = "unsupported_response_type";
    }
}
