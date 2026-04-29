using Microsoft.IdentityModel.Tokens;

namespace InfraGate.DevIssuer;

internal sealed class DevIssuerSigningKey : IDisposable
{
    private readonly System.Security.Cryptography.RSA rsa = System.Security.Cryptography.RSA.Create(2048);

    public DevIssuerSigningKey()
    {
        SecurityKey = new RsaSecurityKey(rsa)
        {
            KeyId = Guid.NewGuid().ToString("N")
        };
        SigningCredentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.RsaSha256);
    }

    public RsaSecurityKey SecurityKey { get; }

    public SigningCredentials SigningCredentials { get; }

    public IDictionary<string, object?> CreateJwks()
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        return new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.Keys] = new object[]
            {
                new Dictionary<string, object?>
                {
                    [DevIssuerConventions.Json.JsonWebKeyType] = DevIssuerConventions.OAuth.RsaKeyType,
                    [DevIssuerConventions.Json.Use] = DevIssuerConventions.OAuth.SignatureKeyUse,
                    [DevIssuerConventions.Json.KeyId] = SecurityKey.KeyId,
                    [DevIssuerConventions.Json.Algorithm] = DevIssuerConventions.OAuth.RsaSha256Algorithm,
                    [DevIssuerConventions.Json.JsonWebKeyModulus] = Base64UrlEncoder.Encode(parameters.Modulus),
                    [DevIssuerConventions.Json.JsonWebKeyExponent] = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        };
    }

    public void Dispose()
    {
        rsa.Dispose();
    }
}
