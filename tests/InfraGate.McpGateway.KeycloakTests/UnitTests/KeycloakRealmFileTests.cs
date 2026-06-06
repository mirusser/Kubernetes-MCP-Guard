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

    [Fact]
    public async Task RealmFiles_ControlledServiceClientsRequireDpopBoundAccessTokens()
    {
        var repoRoot = FindRepoRoot();
        var realmPaths = new[]
        {
            Path.Combine(repoRoot, "deploy", "keycloak", "infra-gate-realm.json"),
            Path.Combine(repoRoot, "tests", "TestData", "keycloak", "infra-gate-realm.json")
        };
        var clientIds = new[]
        {
            "infra-gate-observer",
            "infra-gate-planner",
            "infra-gate-executor"
        };

        foreach (string realmPath in realmPaths)
        {
            JsonNode realm = JsonNode.Parse(await File.ReadAllTextAsync(realmPath)) ??
                throw new InvalidOperationException($"Could not parse Keycloak realm file '{realmPath}'.");
            JsonArray clients = realm["clients"]?.AsArray() ??
                throw new InvalidOperationException($"Keycloak realm file '{realmPath}' is missing clients.");

            foreach (string clientId in clientIds)
            {
                JsonNode client = clients.Single(node =>
                    string.Equals(
                        node?["clientId"]?.GetValue<string>(),
                        clientId,
                        StringComparison.Ordinal)) ??
                    throw new InvalidOperationException($"Keycloak realm file '{realmPath}' is missing '{clientId}'.");

                Assert.Equal(
                    "true",
                    client["attributes"]?["dpop.bound.access.tokens"]?.GetValue<string>());
            }
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
