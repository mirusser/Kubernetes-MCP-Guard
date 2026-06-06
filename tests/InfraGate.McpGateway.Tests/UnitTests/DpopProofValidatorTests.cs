using System.Security.Cryptography;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Auth.Dpop;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DpopProofValidatorTests
{
    private const string Method = "GET";
    private const string Uri = "http://127.0.0.1:3001/mcp";

    private readonly DpopProofTestFactory factory = new();
    private readonly SecurityKey issuerKey = new SymmetricSecurityKey(
        "0123456789abcdef0123456789abcdef"u8.ToArray());

    private string AccessToken => factory.CreateDpopBoundAccessToken(
        issuerKey, "https://issuer.example.com", "http://127.0.0.1:3001/mcp");

    [Fact]
    public async Task ValidateAsync_ValidProof_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri);

        var result = await validator.ValidateAsync(new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NotAJwt_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext("not.a.valid.jwt.input", AccessToken, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongTypHeader_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        // Build a proof JWT with typ = "JWT" instead of "dpop+jwt"
        var wrongTypProof = CreateProofWithCustomHeader(
            factory, token, new Dictionary<string, object> { ["typ"] = "JWT", ["jwk"] = GetJwkDict() });

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(wrongTypProof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_SymmetricAlgorithm_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var hmacKey = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray());
        var hmacProof = CreateHmacProof(token, hmacKey);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(hmacProof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_InvalidSignature_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProofWithWrongKey(token, Method, Uri);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongHtm_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri, overrideHtm: "POST");

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongHtu_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri,
            overrideHtu: "http://other.example.com/mcp");

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredIat_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri, iatOffsetSeconds: -400);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredExpClaim_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri, expires: DateTime.UtcNow.AddMinutes(-10));

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongAth_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri, overrideAth: "aW52YWxpZA");

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongCnfJkt_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        // Use a key whose thumbprint does NOT match the factory's bound key
        var proof = factory.CreateDpopProofWithWrongKey(token, Method, Uri);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_MissingCnfJkt_ReturnsFailure()
    {
        var validator = CreateValidator();
        // Access token without cnf claim
        var tokenWithoutCnf = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://issuer.example.com",
            Audience = "http://127.0.0.1:3001/mcp",
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object> { ["sub"] = "subject-1", ["scope"] = "mcp:tools" },
            SigningCredentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256)
        });
        var proof = factory.CreateDpopProof(tokenWithoutCnf, Method, Uri);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, tokenWithoutCnf, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ValidateAsync_ReusedJti_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        const string fixedJti = "test-replay-jti";
        var proof = factory.CreateDpopProof(token, Method, Uri, jti: fixedJti);

        var first = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));
        var second = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.True(first.IsValid);
        Assert.False(second.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ReusedJtiForDifferentSubject_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var firstToken = factory.CreateDpopBoundAccessToken(
            issuerKey,
            "https://issuer.example.com",
            "http://127.0.0.1:3001/mcp",
            subject: "subject-1");
        var secondToken = factory.CreateDpopBoundAccessToken(
            issuerKey,
            "https://issuer.example.com",
            "http://127.0.0.1:3001/mcp",
            subject: "subject-2");
        const string fixedJti = "test-subject-scoped-jti";
        var firstProof = factory.CreateDpopProof(firstToken, Method, Uri, jti: fixedJti);
        var secondProof = factory.CreateDpopProof(secondToken, Method, Uri, jti: fixedJti);

        var first = await validator.ValidateAsync(
            new DpopProofValidationContext(firstProof, firstToken, Method, Uri));
        var second = await validator.ValidateAsync(
            new DpopProofValidationContext(secondProof, secondToken, Method, Uri));

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_HtuWithQueryString_MatchesUriWithoutQuery()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        // Proof htu has trailing query; request URI has no query — should still match
        var proof = factory.CreateDpopProof(token, Method, Uri,
            overrideHtu: Uri + "?foo=bar");

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ComputeAth_MatchesTestFactory()
    {
        const string token = "eyJhbGciOiJIUzI1NiJ9.payload.signature";
        var fromValidator = DpopProofValidator.ComputeAth(token);
        var fromFactory = DpopProofTestFactory.ComputeAth(token);
        Assert.Equal(fromFactory, fromValidator);
    }

    [Fact]
    public void ComputeJwkThumbprint_MatchesTestFactory()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ecdsa);
        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(key);
        var publicJwk = new JsonWebKey { Kty = "EC", Crv = fullJwk.Crv, X = fullJwk.X, Y = fullJwk.Y };

        var fromValidator = DpopProofValidator.ComputeJwkThumbprint(publicJwk);
        var fromFactory = DpopProofTestFactory.ComputeEcP256Thumbprint(fullJwk.Crv, fullJwk.X, fullJwk.Y);

        Assert.Equal(fromFactory, fromValidator);
    }

    [Fact]
    public async Task ValidateAsync_PrivateKeyInJwk_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa));
        var proof = CreateProofWithPrivateKeyInJwk(token, fullJwk);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("private key", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_FutureIat_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = factory.CreateDpopProof(token, Method, Uri, iatOffsetSeconds: 400);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("iat", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_MissingJtiClaim_ReturnsFailure()
    {
        var validator = CreateValidator();
        var token = AccessToken;
        var proof = CreateProofWithoutJti(token);

        var result = await validator.ValidateAsync(
            new DpopProofValidationContext(proof, token, Method, Uri));

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("jti", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static DpopProofValidator CreateValidator() =>
        new(new InMemoryDpopProofReplayStore());

    private Dictionary<string, string> GetJwkDict()
    {
        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(
            new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)));
        return new Dictionary<string, string>
        {
            ["crv"] = fullJwk.Crv,
            ["kty"] = fullJwk.Kty,
            ["x"] = fullJwk.X,
            ["y"] = fullJwk.Y
        };
    }

    private static string CreateProofWithCustomHeader(
        DpopProofTestFactory f,
        string accessToken,
        Dictionary<string, object> headerClaims)
    {
        // Use the factory's bound key to sign, but override header claims
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingKey = new ECDsaSecurityKey(ecdsa);
        var descriptor = new SecurityTokenDescriptor
        {
            AdditionalHeaderClaims = headerClaims,
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString(),
                ["htm"] = Method,
                ["htu"] = Uri,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["ath"] = DpopProofTestFactory.ComputeAth(accessToken)
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string CreateHmacProof(string accessToken, SymmetricSecurityKey hmacKey)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = "dpop+jwt",
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString(),
                ["htm"] = Method,
                ["htu"] = Uri,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["ath"] = DpopProofTestFactory.ComputeAth(accessToken)
            },
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string CreateProofWithPrivateKeyInJwk(string accessToken, JsonWebKey fullJwk)
    {
        using var signingEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingKey = new ECDsaSecurityKey(signingEcdsa);
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = GatewayAuthConventions.DPoP.ProofTyp,
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["jwk"] = new Dictionary<string, string>
                {
                    ["crv"] = fullJwk.Crv,
                    ["kty"] = fullJwk.Kty,
                    ["x"] = fullJwk.X,
                    ["y"] = fullJwk.Y,
                    ["d"] = fullJwk.D  // private key material — MUST be rejected
                }
            },
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString(),
                ["htm"] = Method,
                ["htu"] = Uri,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["ath"] = DpopProofTestFactory.ComputeAth(accessToken)
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private string CreateProofWithoutJti(string accessToken)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingKey = new ECDsaSecurityKey(ecdsa);
        var fullJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(signingKey);
        var jwkDict = new Dictionary<string, string>
        {
            ["crv"] = fullJwk.Crv,
            ["kty"] = fullJwk.Kty,
            ["x"] = fullJwk.X,
            ["y"] = fullJwk.Y
        };

        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = GatewayAuthConventions.DPoP.ProofTyp,
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwkDict },
            Claims = new Dictionary<string, object>
            {
                // jti intentionally omitted
                ["htm"] = Method,
                ["htu"] = Uri,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["ath"] = DpopProofTestFactory.ComputeAth(accessToken)
            },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
