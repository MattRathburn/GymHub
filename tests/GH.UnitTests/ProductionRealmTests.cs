using System.Text.Json.Nodes;

namespace GH.UnitTests;

/// <summary>
/// The production realm (baked into the Keycloak image) has no test users and production settings, and keeps the same
/// roles, clients and token mappers as the test realm the app is developed against.
/// </summary>
public class ProductionRealmTests
{
    private static readonly string AppHost = Path.Combine(RepoRoot(), "src", "GymHub.AppHost");

    private static readonly JsonObject Production = Load(Path.Combine(AppHost, "Keycloak", "realm", "GymHub-realm.json"));
    private static readonly JsonObject Test = Load(Path.Combine(AppHost, "Realms", "GH-Test-Realm-realm.json"));

    private static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GymHub.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("GymHub.slnx not found above the test output.");
    }

    private static JsonObject Client(JsonObject realm, string clientId) =>
        realm["clients"]!.AsArray().Single(c => (string?)c!["clientId"] == clientId)!.AsObject();

    [Fact]
    public void Only_the_API_service_account_is_seeded_and_nobody_has_a_password()
    {
        var users = Production["users"]!.AsArray();

        var user = Assert.Single(users)!;
        Assert.Equal("gymhub-api-admin", (string?)user["serviceAccountClientId"]);
        Assert.Null(user["credentials"]);
    }

    [Fact]
    public void Production_settings_are_on()
    {
        Assert.Equal("GymHub", (string?)Production["realm"]);
        Assert.Equal("external", (string?)Production["sslRequired"]);
        Assert.True((bool?)Production["verifyEmail"]);
        Assert.True((bool?)Production["bruteForceProtected"]);
        Assert.False((bool?)Production["duplicateEmailsAllowed"]);
        Assert.Contains("length(", (string?)Production["passwordPolicy"]);

        var smtp = Production["smtpServer"]!;
        Assert.Equal("true", (string?)smtp["auth"]);
        Assert.Equal("true", (string?)smtp["starttls"]);
    }

    [Fact]
    public void Sign_in_only_returns_to_the_public_origin()
    {
        var bff = Client(Production, "gymhub-bff");

        Assert.Equal(["${GYMHUB_PUBLIC_ORIGIN}/*"], bff["redirectUris"]!.AsArray().Select(u => (string?)u));
        Assert.Equal("${GYMHUB_PUBLIC_ORIGIN}/*", (string?)bff["attributes"]!["post.logout.redirect.uris"]);
        Assert.DoesNotContain("localhost", Production.ToJsonString());
    }

    [Fact]
    public void Secrets_come_from_the_environment_not_the_file()
    {
        Assert.All(Production["clients"]!.AsArray(), client => Assert.StartsWith("${", (string?)client!["secret"]));
        Assert.StartsWith("${", (string?)Production["smtpServer"]!["password"]);
    }

    [Fact]
    public void Roles_clients_and_token_mappers_match_the_test_realm()
    {
        static IEnumerable<string?> RoleNames(JsonObject realm) =>
            realm["roles"]!["realm"]!.AsArray().Select(r => (string?)r!["name"]).Order();

        static IEnumerable<string?> ClientIds(JsonObject realm) =>
            realm["clients"]!.AsArray().Select(c => (string?)c!["clientId"]).Order();

        Assert.Equal(RoleNames(Test), RoleNames(Production));
        Assert.Equal(ClientIds(Test), ClientIds(Production));

        foreach (var clientId in ClientIds(Test))
        {
            var test = Client(Test, clientId!);
            var production = Client(Production, clientId!);

            Assert.True(JsonNode.DeepEquals(test["protocolMappers"], production["protocolMappers"]), $"{clientId}'s mappers differ");

            foreach (var flag in new[] { "publicClient", "standardFlowEnabled", "directAccessGrantsEnabled", "serviceAccountsEnabled" })
            {
                Assert.True(JsonNode.DeepEquals(test[flag], production[flag]), $"{clientId}.{flag} differs");
            }
        }

        var testAccount = Test["users"]!.AsArray().Single(u => u!["serviceAccountClientId"] is not null)!;
        Assert.True(JsonNode.DeepEquals(testAccount["clientRoles"], Production["users"]![0]!["clientRoles"]));
    }
}
