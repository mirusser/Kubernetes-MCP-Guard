namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class RunProfileDocumentTests
{
    [Fact]
    public void FindProfile_KnownName_ReturnsProfile()
    {
        var profile = MakeProfile("my-profile");
        var doc = new RunProfileDocument([profile]);

        var found = doc.FindProfile("my-profile");

        Assert.Same(profile, found);
    }

    [Fact]
    public void FindProfile_UnknownName_ThrowsInvalidOperationException()
    {
        var doc = new RunProfileDocument([MakeProfile("my-profile")]);

        Assert.Throws<InvalidOperationException>(() => doc.FindProfile("other-profile"));
    }

    [Fact]
    public void FindProfileWithDefaults_NullDefaults_ReturnsOriginalProfile()
    {
        var profile = MakeProfile("my-profile");
        var doc = new RunProfileDocument([profile]);

        var found = doc.FindProfileWithDefaults("my-profile", null);

        Assert.Equal(profile, found);
    }

    [Fact]
    public void FindProfileWithDefaults_GatewayDefaults_MergesUrlsWhenProfileHasNone()
    {
        var profile = MakeProfile("my-profile") with
        {
            Gateway = new GatewayProfile(null, null, null)
        };
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("http://localhost:3001", merged.Gateway?.AspnetcoreUrls);
    }

    [Fact]
    public void FindProfileWithDefaults_GatewayDefaults_ProfileValueTakesPrecedence()
    {
        var profile = MakeProfile("my-profile") with
        {
            Gateway = new GatewayProfile("http://localhost:4000", null, null)
        };
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("http://localhost:4000", merged.Gateway?.AspnetcoreUrls);
    }

    [Fact]
    public void FindProfileWithDefaults_NullGatewayProfile_DefaultsDoNotMaterialize()
    {
        var profile = MakeProfile("my-profile");
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Null(merged.Gateway);
    }

    [Fact]
    public void Profiles_ReflectsConstructorInput()
    {
        var profiles = new[]
        {
            MakeProfile("alpha"),
            MakeProfile("beta"),
        };
        var doc = new RunProfileDocument(profiles);

        Assert.Equal(2, doc.Profiles.Count);
        Assert.Equal("alpha", doc.Profiles[0].Name);
        Assert.Equal("beta", doc.Profiles[1].Name);
    }

    [Fact]
    public void Defaults_InitializerSetsDefaults()
    {
        var defaults = new ProfileDefaults(null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([MakeProfile("x")]) { Defaults = defaults };

        Assert.Same(defaults, doc.Defaults);
    }

    private static RunProfile MakeProfile(string name) =>
        new(name, "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null);
}
