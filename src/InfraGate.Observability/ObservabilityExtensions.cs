using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

namespace InfraGate.Observability;

public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddInfraGateObservability(
        this IHostApplicationBuilder builder,
        Action<ObservabilityOptions> configure)
    {
        var options = new ObservabilityOptions();
        configure(options);

        builder.Logging.ClearProviders();

        var loggerConfig = new LoggerConfiguration();

        if (options.WriteToConsole)
        {
            if (options.ConsoleToStandardError)
            {
                loggerConfig.WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose);
            }
            else
            {
                loggerConfig.WriteTo.Console();
            }
        }

        if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            loggerConfig.WriteTo.File(new CompactJsonFormatter(), options.FilePath);
        }

        loggerConfig.Enrich.With<TraceContextEnricher>();

        var logger = loggerConfig.CreateLogger();
        builder.Services.AddSerilog(logger, dispose: true);

        return builder;
    }
}
