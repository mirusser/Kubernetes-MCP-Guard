using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.DevIssuer;

internal sealed class DevIssuerStore
{
    private const int ClientIdByteLength = 16;
    private const int AuthorizationCodeByteLength = 32;

    private readonly ConcurrentDictionary<string, DevClient> clients = [];
    private readonly ConcurrentDictionary<string, AuthorizationCode> authorizationCodes = [];

    public DevIssuerStore(DevIssuerOptions options)
    {
        RegisterClient(
            options.ApprovalClientId,
            [options.ApprovalRedirectUri],
            "InfraGate approval UI");
    }

    public DevClient RegisterClient(IReadOnlyCollection<string> redirectUris, string? clientName)
    {
        return RegisterClient(NewRandomValue(ClientIdByteLength), redirectUris, clientName);
    }

    public bool ClientAllowsRedirectUri(string clientId, string redirectUri)
    {
        return clients.TryGetValue(clientId, out var client) &&
               client.RedirectUris.Any(registered =>
                   string.Equals(registered, redirectUri, StringComparison.Ordinal));
    }

    public AuthorizationCode CreateAuthorizationCode(
        string clientId,
        string redirectUri,
        string codeChallenge,
        string resource,
        string scope,
        DateTimeOffset expiresAt)
    {
        var authorizationCode = new AuthorizationCode(
            NewRandomValue(AuthorizationCodeByteLength),
            clientId,
            redirectUri,
            codeChallenge,
            resource,
            scope,
            expiresAt);

        authorizationCodes[authorizationCode.Code] = authorizationCode;

        return authorizationCode;
    }

    public bool TryConsumeAuthorizationCode(
        string code,
        Func<AuthorizationCode, bool> validator,
        [NotNullWhen(true)] out AuthorizationCode? authorizationCode)
    {
        if (!authorizationCodes.TryGetValue(code, out var pendingCode) ||
            !validator(pendingCode))
        {
            authorizationCode = null;
            return false;
        }

        if (!authorizationCodes.TryRemove(code, out authorizationCode))
        {
            authorizationCode = null;
            return false;
        }

        return true;
    }

    private static string NewRandomValue(int byteLength)
    {
        var bytes = new byte[byteLength];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        return Base64UrlEncoder.Encode(bytes);
    }

    private DevClient RegisterClient(string clientId, IReadOnlyCollection<string> redirectUris, string? clientName)
    {
        var client = new DevClient(
            clientId,
            clientName,
            redirectUris.ToArray());

        clients[client.ClientId] = client;

        return client;
    }
}
