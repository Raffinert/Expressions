using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class InterceptorTests
{
    [Fact]
    public async Task InterfaceWrapperReassignmentUpdatesTheSameQuery()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        IComposableExpression<OrderRow, bool> condition = Condition<OrderRow>.Create(x => x.Active);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
        var original = query.Expression;
        Assert.Equal(new[] { 1, 2 }, await query.ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => !x.Active);
        Assert.Equal(new[] { 3, 4 }, await query.ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(new[] { 4 }, await query.ToArrayAsync());
        Assert.Same(original, query.Expression);
        Assert.Equal(3, fixture.Commands.Executed.Count);
        Assert.All(fixture.Commands.Executed, command => Assert.Contains("WHERE", command.Sql));
    }

    [Fact]
    public async Task NestedInterfaceWrappersExpandOnTheServer()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        IComposableExpression<OrderRow, int> amount = Projection<OrderRow>.Create(x => x.TotalCents);
        IComposableExpression<OrderRow, bool> condition = Condition<OrderRow>.Create(x => amount.Invoke(x) > 1000);
        var composed = Condition<OrderRow>.Create(x => condition.Invoke(x) && x.Active);
        Assert.Equal(new[] { 2 }, await fixture.Db.Orders.Where(x => composed.Invoke(x)).Select(x => x.Id).ToArrayAsync());
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task CapturedPredicateExecutesSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > 1000);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id);
        var rows = await query.ToListAsync();

        Assert.Equal([2, 3, 4], rows.Select(x => x.Id));
        Assert.Contains("WHERE", query.ToQueryString());
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task UnregisteredPredicateFailsTranslation()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > 1000);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToListAsync());
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task ReassigningWrapperInTheSameClosureAndQueryDoesNotLeakCachedCondition()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id);
        var original = query.Expression;

        Assert.Equal(new[] { 1, 2 }, await query.Select(x => x.Id).ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => !x.Active);
        Assert.Equal(new[] { 3, 4 }, await query.Select(x => x.Id).ToArrayAsync());
        condition = Condition<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(new[] { 4 }, await query.Select(x => x.Id).ToArrayAsync());
        Assert.Same(original, query.Expression);
        Assert.Equal(3, fixture.Commands.Executed.Count);
    }

    [Fact]
    public async Task StableWrapperUsesCurrentCapturedScalarOnEveryExecution()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var threshold = 1000;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x)).OrderBy(x => x.Id);

        Assert.Equal(new[] { 2, 3, 4 }, await query.Select(x => x.Id).ToArrayAsync());
        threshold = 10000;
        Assert.Equal(new[] { 2 }, await query.Select(x => x.Id).ToArrayAsync());
        threshold = 1000;
        Assert.Equal(new[] { 2, 3, 4 }, await query.Select(x => x.Id).ToArrayAsync());
        Assert.DoesNotContain("10000", fixture.Commands.Executed[1].Sql);
        Assert.Contains(10000, fixture.Commands.Executed[1].Values);
    }

    [Fact]
    public async Task OuterCapturedScalarRemainsAnEfParameter()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var threshold = 1000;
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x) && x.TotalCents > threshold);
        Assert.Equal(new[] { 2 }, await query.Select(x => x.Id).ToArrayAsync());
        threshold = 100;
        Assert.Equal(new[] { 1, 2 }, await query.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Contains(1000, fixture.Commands.Executed[0].Values);
        Assert.Contains(100, fixture.Commands.Executed[1].Values);
    }
}
