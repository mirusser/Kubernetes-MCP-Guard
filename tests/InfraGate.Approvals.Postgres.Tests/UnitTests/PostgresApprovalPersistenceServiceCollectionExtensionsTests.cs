using InfraGate.Approvals.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.Approvals.Postgres.Tests.UnitTests;

public sealed class PostgresApprovalPersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPostgresApprovalPersistence_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddPostgresApprovalPersistence("Host=localhost;Database=test"));
    }

    [Fact]
    public void AddPostgresApprovalPersistence_NullConnectionString_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddPostgresApprovalPersistence(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPostgresApprovalPersistence_WhitespaceConnectionString_Throws(string connectionString)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddPostgresApprovalPersistence(connectionString));
    }
}
