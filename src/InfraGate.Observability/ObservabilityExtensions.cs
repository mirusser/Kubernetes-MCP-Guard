using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;

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
            loggerConfig.WriteTo.Console(
                theme: AnsiConsoleTheme.Code,
                standardErrorFromLevel: options.ConsoleToStandardError
                    ? Serilog.Events.LogEventLevel.Verbose
                    : null);
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
