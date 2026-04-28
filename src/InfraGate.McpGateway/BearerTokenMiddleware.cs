using System.Security.Cryptography;
using System.Text;

namespace InfraGate.McpGateway;

public sealed class BearerTokenMiddleware(RequestDelegate next, McpGatewayOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        var expected = $"Bearer {options.BearerToken}";
        var actual = context.Request.Headers.Authorization.ToString();
        if (!ConstantTimeEquals(actual, expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        await next(context);
    }

    private static bool ConstantTimeEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return actualBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
