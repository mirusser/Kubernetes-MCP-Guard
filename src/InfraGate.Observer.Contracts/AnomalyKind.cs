namespace InfraGate.Observer.Contracts;

public enum AnomalyKind
{
    PodUnhealthy,
    DeploymentUnavailable,
    ServiceNoEndpoints,
    WarningEvent
}
