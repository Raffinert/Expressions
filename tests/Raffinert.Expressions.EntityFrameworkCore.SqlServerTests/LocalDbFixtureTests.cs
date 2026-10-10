namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;

public class LocalDbFixtureTests
{
    [Theory]
    [InlineData("Server=production.invalid;Integrated Security=True")]
    [InlineData(@"Server=(localdb)\.\Shared;Integrated Security=True")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;User ID=synthetic;Password=synthetic")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;AttachDbFilename=C:\synthetic.mdf")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Password=synthetic")]
    [InlineData("invalid connection text")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Failover Partner=production.invalid")]
    public void UnsafeConnectionsAreRejectedWithoutEchoingConfiguration(string connection)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LocalDbFixture.ValidateMasterConnection(connection));
        Assert.DoesNotContain("synthetic", error.ToString());
        Assert.DoesNotContain("production.invalid", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void SuppliedCatalogCannotBecomeTheCleanupTarget()
    {
        var builder = LocalDbFixture.ValidateMasterConnection(@"Server=(localdb)\Alternate;Database=ExistingDatabase;Integrated Security=True;Encrypt=False");
        Assert.Equal("master", builder.InitialCatalog);
        Assert.Equal(@"(localdb)\Alternate", builder.DataSource);
    }
}
