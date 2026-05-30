using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace InfraGate.Observability;

public static class TelemetryExtensions
{
    public static IHostApplicationBuilder AddInfraGateTelemetry(
        this IHostApplicationBuilder builder,
        Action<TelemetryOptions> configure)
    {
        var options = new TelemetryOptions();
        configure(options);

        var otlpEndpoint = options.OtlpEndpoint
            ?? Environment.GetEnvironmentVariable(TelemetryConventions.OtlpEndpointEnvVar);

        var resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion,
                autoGenerateServiceInstanceId: true);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(TelemetryConventions.AgentsAiSourceName)
                    .AddSource(TelemetryConventions.ExtensionsAiSourceName)
                    .AddSource(TelemetryConventions.WorkflowsSourceName)
                    .AddHttpClientInstrumentation();

                tracing.AddProcessor(sp =>
                    new SerilogSpanProcessor(sp.GetRequiredService<ILogger<SerilogSpanProcessor>>()));

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddMeter(TelemetryConventions.AgentsAiSourceName)
                    .AddMeter(TelemetryConventions.ExtensionsAiSourceName)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                foreach (var meterName in options.MeterNames)
                {
                    metrics.AddMeter(meterName);
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }
}
