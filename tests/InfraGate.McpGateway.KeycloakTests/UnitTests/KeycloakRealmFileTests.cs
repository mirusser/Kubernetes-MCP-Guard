using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraGate.McpGateway.KeycloakTests.UnitTests;

public sealed class KeycloakRealmFileTests
{
    private static readonly JsonSerializerOptions PrettyPrint = new() { WriteIndented = true };

    [Fact]
    public async Task RealmFiles_DeployAndTestData_AreEquivalentJson()
    {
        var repoRoot = FindRepoRoot();
        var deployRealmPath = Path.Combine(repoRoot, "deploy", "keycloak", "infra-gate-realm.json");
        var testRealmPath = Path.Combine(repoRoot, "tests", "TestData", "keycloak", "infra-gate-realm.json");

        string deployJson = await File.ReadAllTextAsync(deployRealmPath);
        string testJson = await File.ReadAllTextAsync(testRealmPath);

        JsonNode? deployRealm = JsonNode.Parse(deployJson);
        JsonNode? testRealm = JsonNode.Parse(testJson);

        if (!JsonNode.DeepEquals(deployRealm, testRealm))
        {
            string deployPretty = deployRealm!.ToJsonString(PrettyPrint);
            string testPretty = testRealm!.ToJsonString(PrettyPrint);
            Assert.Equal(deployPretty, testPretty);
        }
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
