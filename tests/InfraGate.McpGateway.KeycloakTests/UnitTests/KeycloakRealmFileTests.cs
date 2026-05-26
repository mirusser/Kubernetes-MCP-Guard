using System.Text.Json.Nodes;

namespace InfraGate.McpGateway.KeycloakTests.UnitTests;

public sealed class KeycloakRealmFileTests
{
    [Fact]
    public async Task RealmFiles_DeployAndTestData_AreEquivalentJson()
    {
        var repoRoot = FindRepoRoot();
        var deployRealmPath = Path.Combine(repoRoot, "deploy", "keycloak", "infra-gate-realm.json");
        var testRealmPath = Path.Combine(repoRoot, "tests", "TestData", "keycloak", "infra-gate-realm.json");

        JsonNode? deployRealm = JsonNode.Parse(await File.ReadAllTextAsync(deployRealmPath));
        JsonNode? testRealm = JsonNode.Parse(await File.ReadAllTextAsync(testRealmPath));

        Assert.True(
            JsonNode.DeepEquals(deployRealm, testRealm),
            "Deploy and test Keycloak realm JSON must remain semantically equivalent.");
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
