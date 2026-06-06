using System.Security.Cryptography;
using System.Text;
using InfraGate.McpGateway.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

internal sealed class DpopProofTestFactory : IDisposable
{
    private readonly ECDsa ecdsa;
    private readonly ECDsaSecurityKey privateKey;
    private readonly string publicJwkX;
    private readonly string publicJwkY;

    public string BoundKeyThumbprint { get; }

    public DpopProofTestFactory()
    {
        ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        privateKey = new ECDsaSecurityKey(ecdsa);

        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(privateKey);
        publicJwkX = fullJwk.X;
        publicJwkY = fullJwk.Y;
        BoundKeyThumbprint = ComputeEcP256Thumbprint("P-256", publicJwkX, publicJwkY);
    }

    public string CreateDpopBoundAccessToken(
        SecurityKey issuerSigningKey,
        string issuer,
        string audience,
        string scope = "mcp:tools",
        string subject = "subject-1",
        DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object>
            {
                [GatewayAuthConventions.Claims.Subject] = subject,
                [GatewayAuthConventions.Claims.Scope] = scope,
                [GatewayAuthConventions.DPoP.CnfClaim] = new Dictionary<string, string>
                {
                    [GatewayAuthConventions.DPoP.JktClaim] = BoundKeyThumbprint
                }
            },
            SigningCredentials = new SigningCredentials(issuerSigningKey, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public string CreateDpopProof(
        string accessToken,
        string method = "GET",
        string uri = "http://127.0.0.1:3001/mcp",
        string? jti = null,
        int iatOffsetSeconds = 0,
        string? overrideAth = null,
        string? overrideHtm = null,
        string? overrideHtu = null)
    {
        return BuildProof(
            accessToken: accessToken,
            jwkX: publicJwkX,
            jwkY: publicJwkY,
            signingKey: privateKey,
            method: method,
            uri: uri,
            jti: jti ?? Guid.NewGuid().ToString(),
            iatOffsetSeconds: iatOffsetSeconds,
            overrideAth: overrideAth,
            overrideHtm: overrideHtm,
            overrideHtu: overrideHtu);
    }

    public string CreateDpopProofWithWrongKey(
        string accessToken,
        string method = "GET",
        string uri = "http://127.0.0.1:3001/mcp")
    {
        using var wrongEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongKey = new ECDsaSecurityKey(wrongEcdsa);
        var wrongFullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(wrongKey);

        return BuildProof(
            accessToken: accessToken,
            jwkX: wrongFullJwk.X,
            jwkY: wrongFullJwk.Y,
            signingKey: wrongKey,
            method: method,
            uri: uri,
            jti: Guid.NewGuid().ToString(),
            iatOffsetSeconds: 0);
    }

    private static string BuildProof(
        string accessToken,
        string jwkX,
        string jwkY,
        SecurityKey signingKey,
        string method,
        string uri,
        string jti,
        int iatOffsetSeconds,
        string? overrideAth = null,
        string? overrideHtm = null,
        string? overrideHtu = null)
    {
        var ath = overrideAth ?? ComputeAth(accessToken);

        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = GatewayAuthConventions.DPoP.ProofTyp,
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["jwk"] = new Dictionary<string, string>
                {
                    ["crv"] = "P-256",
                    ["kty"] = "EC",
                    ["x"] = jwkX,
                    ["y"] = jwkY
                }
            },
            Claims = new Dictionary<string, object>
            {
                ["jti"] = jti,
                ["htm"] = overrideHtm ?? method,
                ["htu"] = overrideHtu ?? uri,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + iatOffsetSeconds,
                ["ath"] = ath
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    internal static string ComputeAth(string accessToken)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(bytes);
    }

    internal static string ComputeEcP256Thumbprint(string crv, string x, string y)
    {
        // RFC 7638: required members only, alphabetical order, no whitespace
        var canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncoder.Encode(bytes);
    }

    public void Dispose() => ecdsa.Dispose();
}
