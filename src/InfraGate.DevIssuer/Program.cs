using InfraGate.DevIssuer;

var options = DevIssuerOptions.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration[DevIssuerConventions.ConfigurationKeys.Urls]) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DevIssuerConventions.EnvironmentVariables.AspNetCoreUrls)))
{
    builder.WebHost.UseUrls(DevIssuerOptions.DefaultUrl);
}

builder.Services.AddDevIssuer(options);

var app = builder.Build();
app.MapDevIssuer();

await app.RunAsync();
