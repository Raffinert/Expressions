using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class CachePolicyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("snapshot", 25)]
    [InlineData("normal", 1)]
    [InlineData("outer", 1)]
    [InlineData("direct", 1)]
    public async Task ChangingThresholdsMeasureSqlParametersAndCompilations(string mode, int expectedCompilations)
    {
        // A private EF service cache makes the compilation count independent of other tests.
        await using var fixture = await SqliteFixture.CreateAsync(configure: b => b.EnableServiceProviderCaching(false));
        var threshold = 100;
        var snapshot = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        var active = Condition<OrderRow>.Create(x => x.Active);
        var query = mode switch
        {
            "snapshot" => fixture.Db.Orders.Where(x => snapshot.Invoke(x)),
            "outer" => fixture.Db.Orders.Where(x => active.Invoke(x) && x.TotalCents > threshold),
            _ => fixture.Db.Orders.Where(x => x.TotalCents > threshold)
        };
        for (var i = 0; i < 25; i++)
        {
            threshold = 100 + i;
            Assert.Equal(mode == "outer" ? 2 : 4, mode == "direct"
                ? await fixture.Db.Orders.CountAsync(snapshot)
                : await query.CountAsync());
        }
        Assert.Equal(expectedCompilations, fixture.QueryCompilations);
        Assert.Equal(25, fixture.Commands.Executed.Count);
        Assert.Equal(mode == "snapshot" ? 25 : 1, fixture.Commands.Executed.Select(x => x.Sql).Distinct().Count());
        for (var i = 0; i < 25; i++)
        {
            var command = fixture.Commands.Executed[i];
            Assert.Contains("WHERE", command.Sql);
            if (mode == "snapshot") Assert.Empty(command.Values);
            else Assert.Equal(100 + i, Assert.Single(command.Values));
        }
        // Repeating the last value must reuse the compiled shape in both policies.
        Assert.Equal(mode == "outer" ? 2 : 4, mode == "direct"
            ? await fixture.Db.Orders.CountAsync(snapshot)
            : await query.CountAsync());
        Assert.Equal(expectedCompilations, fixture.QueryCompilations);
        output.WriteLine($"{mode}: {fixture.QueryCompilations} compilations for 25 distinct values plus one repeat.");
        output.WriteLine(fixture.Commands.Executed[0].Sql);
        output.WriteLine($"Parameters: [{string.Join(", ", fixture.Commands.Executed[0].Values)}]");
        output.WriteLine(fixture.Commands.Executed[24].Sql);
        output.WriteLine($"Parameters: [{string.Join(", ", fixture.Commands.Executed[24].Values)}]");
    }

    [Fact]
    public async Task DifferentStructuresWithTheSameScalarDoNotShareResults()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var threshold = 1000;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(new[] { 2, 3, 4 }, await query.ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => x.TotalCents < threshold);
        Assert.Equal(new[] { 1 }, await query.ToArrayAsync());
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.NotEqual(fixture.Commands.Executed[0].Sql, fixture.Commands.Executed[1].Sql);
    }
}
