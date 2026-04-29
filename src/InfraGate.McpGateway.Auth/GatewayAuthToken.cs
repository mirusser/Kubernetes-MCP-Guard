namespace InfraGate.McpGateway.Auth;

internal static class GatewayAuthToken
{
    public static bool IsStaticBearerToken(string authorization, GatewayAuthOptions options)
    {
        var bearerToken = options.BearerToken;
        if (!options.StaticBearerEnabled ||
            string.IsNullOrWhiteSpace(bearerToken) ||
            !TryGetBearerToken(authorization, out var token))
        {
            return false;
        }

        return ConstantTimeEquals(token, bearerToken);
    }

    private static bool TryGetBearerToken(string authorization, out string token)
    {
        var prefix = GatewayAuthConventions.AuthorizationScheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static bool ConstantTimeEquals(string actual, string expected)
    {
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);

        return actualBytes.Length == expectedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
