namespace InfraGate.RunProfiles;

internal sealed record class HostProfile(
    string? BindAddress,
    string? BindPort,
    string? GatewayImage,
    string? KubeconfigHostPath,
    string? ApprovalHostPath,
    string? GuardAuditHostPath,
    string? DataProtectionHostPath);
