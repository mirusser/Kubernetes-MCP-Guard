namespace InfraGate.Remediation.E2E.Tests;

[CollectionDefinition(Name)]
public sealed class RemediationE2ECollection : ICollectionFixture<RemediationE2EFixture>
{
    public const string Name = "RemediationE2E";
}
