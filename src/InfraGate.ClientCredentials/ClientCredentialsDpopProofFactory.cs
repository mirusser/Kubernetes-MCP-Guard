using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.ClientCredentials;

internal sealed class ClientCredentialsDpopProofFactory : IDisposable
{
    private readonly ECDsa ecdsa;
    private readonly ECDsaSecurityKey signingKey;
    private readonly Dictionary<string, string> publicJwk;

    public ClientCredentialsDpopProofFactory()
    {
        ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        signingKey = new ECDsaSecurityKey(ecdsa);
        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(signingKey);
        publicJwk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["crv"] = fullJwk.Crv,
            ["kty"] = fullJwk.Kty,
            ["x"] = fullJwk.X,
            ["y"] = fullJwk.Y
        };
    }

    public string CreateProof(HttpMethod method, Uri uri, string? accessToken = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = method.Method,
            ["htu"] = uri.GetLeftPart(UriPartial.Path),
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
            claims["ath"] = ComputeAth(accessToken);

        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = ClientCredentialsConventions.DPoP.ProofTyp,
            AdditionalHeaderClaims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ClientCredentialsConventions.DPoP.JwkHeaderName] = publicJwk
            },
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Dispose() => ecdsa.Dispose();

    private static string ComputeAth(string accessToken)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(bytes);
    }
}
