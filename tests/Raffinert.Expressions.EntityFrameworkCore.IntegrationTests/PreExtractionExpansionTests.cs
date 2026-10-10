using Microsoft.EntityFrameworkCore;
#if SQLSERVER_TESTS
using Fixture = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.LocalDbFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.SqlOrderRow;
namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;
#else
using Fixture = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.SqliteFixture;
using Row = Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.OrderRow;
namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;
#endif

public class PreExtractionExpansionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComputedDirectiveMatchesNativeAcrossChanges(bool constant)
    {
        await using var fixture = await Fixture.CreateAsync();
        var threshold = 1000;
        var condition = constant
            ? Condition<Row>.Create(x => x.TotalCents > EF.Constant(threshold + 100))
            : Condition<Row>.Create(x => x.TotalCents > EF.Parameter(threshold + 100));
        var embedded = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var native = constant
            ? fixture.Db.Orders.Where(x => x.TotalCents > EF.Constant(threshold + 100)).OrderBy(x => x.Id).Select(x => x.Id)
            : fixture.Db.Orders.Where(x => x.TotalCents > EF.Parameter(threshold + 100)).OrderBy(x => x.Id).Select(x => x.Id);
        foreach (var value in new[] { 1000, 10000, 1000 })
        {
            threshold = value;
            var expected = await native.ToArrayAsync();
            Assert.Equal(value == 1000 ? new[] { 2, 3, 4 } : new[] { 2 }, expected);
            Assert.Equal(expected, await embedded.ToArrayAsync());
            var command = fixture.Commands.Executed.Last();
            if (constant)
            {
                Assert.Empty(command.Values);
                Assert.Matches(@">\s*" + (value + 100) + @"\b", command.Sql);
            }
            else
            {
                Assert.Equal(value + 100, Assert.Single(command.Values));
                Assert.DoesNotContain((value + 100).ToString(), command.Sql);
            }
        }
    }
}
