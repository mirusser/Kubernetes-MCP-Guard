using System.Globalization;

namespace InfraGate.McpGateway;

/// <summary>
/// Bounds for <see cref="DownstreamProcessSupervisor"/>'s capped-exponential-backoff-with-jitter
/// restart loop. Read from the same <c>InfraGate:Gateway:KubernetesMcpServer</c> section as
/// <see cref="KubernetesMcpServerProcessOptions"/> so operators configure the secondary downstream
/// in one place.
/// </summary>
internal sealed record class DownstreamProcessSupervisorOptions(
    TimeSpan MinBackoff,
    TimeSpan MaxBackoff,
    int MaxAttempts)
{
    internal static readonly TimeSpan DefaultMinBackoff = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30);
    internal const int DefaultMaxAttempts = 5;

    internal static DownstreamProcessSupervisorOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section =
            configuration.GetSection(McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection);

        TimeSpan minBackoff = ParseMilliseconds(
            section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSupervisorMinBackoffMillisecondsKey],
            DefaultMinBackoff);
        TimeSpan maxBackoff = ParseMilliseconds(
            section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSupervisorMaxBackoffMillisecondsKey],
            DefaultMaxBackoff);

        int maxAttempts =
            int.TryParse(
                section[McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSupervisorMaxAttemptsKey],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedAttempts) && parsedAttempts > 0
                ? parsedAttempts
                : DefaultMaxAttempts;

        if (maxBackoff < minBackoff)
        {
            throw new InvalidOperationException(
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:" +
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSupervisorMaxBackoffMillisecondsKey} " +
                "must be greater than or equal to " +
                $"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSupervisorMinBackoffMillisecondsKey}.");
        }

        return new DownstreamProcessSupervisorOptions(minBackoff, maxBackoff, maxAttempts);
    }

    private static TimeSpan ParseMilliseconds(string? raw, TimeSpan fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMs) && parsedMs > 0
            ? TimeSpan.FromMilliseconds(parsedMs)
            : fallback;
}
