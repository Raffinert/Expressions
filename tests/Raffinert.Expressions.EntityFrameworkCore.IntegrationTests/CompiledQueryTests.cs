using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class CompiledQueryTests
{
    [Fact]
    public async Task CompiledQueryWithStableClosedWrapperAndScalarParameterExecutesSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var compiled = EF.CompileQuery((OrdersContext db, int minimum) => db.Orders
            .Where(x => condition.Invoke(x) && x.TotalCents > minimum).OrderBy(x => x.Id).Select(x => x.Id));
        Assert.Equal(new[] { 2 }, compiled(fixture.Db, 1000));
        Assert.Equal(new[] { 1, 2 }, compiled(fixture.Db, 100));
        await using var second = new OrdersContext(fixture.Options);
        Assert.Equal(new[] { 2 }, compiled(second, 1000));
        Assert.Equal(3, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, x => Assert.Contains("WHERE", x.Sql));
    }

    [Fact]
    public async Task CompiledAsyncQueryWithStableWrapperUsesCurrentScalarParameters()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var compiled = EF.CompileAsyncQuery((OrdersContext db, int minimum) => db.Orders
            .Where(x => condition.Invoke(x) && x.TotalCents > minimum).OrderBy(x => x.Id).Select(x => x.Id));
        Assert.Equal(new[] { 2 }, await CollectAsync(compiled(fixture.Db, 1000)));
        Assert.Equal(new[] { 1, 2 }, await CollectAsync(compiled(fixture.Db, 100)));
        Assert.Equal(2, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, x => Assert.Contains("WHERE", x.Sql));
    }

    [Fact]
    public async Task WrapperParametersOfCompiledQueriesAreExplicitlyUnsupported()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var compiled = EF.CompileQuery((OrdersContext db, Condition<OrderRow> predicate) =>
            db.Orders.Count(x => predicate.Invoke(x)));
        var error = Assert.Throws<InvalidOperationException>(() => compiled(fixture.Db, Condition<OrderRow>.True));
        Assert.Contains("Unable to resolve expression instance", error.Message);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task CompiledQueriesFixClosedWrappersAtCompilationAsDocumented()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Id == 2);
        var compiled = EF.CompileQuery((OrdersContext db) => db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id));
        Assert.Equal(new[] { 2 }, compiled(fixture.Db));
        condition = Condition<OrderRow>.Create(x => x.Id == 4);
        // EF's compiled delegate fixes its captured expression at compilation. Use ordinary LINQ for wrapper changes.
        Assert.Equal(new[] { 2 }, compiled(fixture.Db));
        Assert.Equal(new[] { 4 }, await fixture.Db.Orders.Where(x => condition.Invoke(x)).Select(x => x.Id).ToArrayAsync());
    }

    private static async Task<List<int>> CollectAsync(IAsyncEnumerable<int> query)
    {
        var rows = new List<int>();
        await foreach (var value in query) rows.Add(value);
        return rows;
    }
}
