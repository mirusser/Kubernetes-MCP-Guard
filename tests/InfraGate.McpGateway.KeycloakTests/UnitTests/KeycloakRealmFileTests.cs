namespace InfraGate.McpGateway.KeycloakTests.UnitTests;

public sealed class KeycloakRealmFileTests
{
    [Fact]
    public async Task RealmFiles_DeployAndTestData_AreIdentical()
    {
        var repoRoot = FindRepoRoot();
        var deployRealmPath = Path.Combine(repoRoot, "deploy", "keycloak", "infra-gate-realm.json");
        var testRealmPath = Path.Combine(repoRoot, "tests", "TestData", "keycloak", "infra-gate-realm.json");

        var deployRealm = await File.ReadAllTextAsync(deployRealmPath);
        var testRealm = await File.ReadAllTextAsync(testRealmPath);

        Assert.Equal(deployRealm, testRealm);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "InfraGate.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
