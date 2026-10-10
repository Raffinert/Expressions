using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class CompositionTests
{
    [Fact]
    public async Task AsyncTerminalWithEmbeddedMarkerExecutesSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > 1000);
        Assert.True(await fixture.Db.Orders.AnyAsync(x => condition.Invoke(x)));
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task NestedProjectionAndCrossCompositionTranslateSelection()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var amount = Projection<OrderRow>.Create(x => x.TotalCents);
        var expensive = Condition<OrderRow>.Create(x => amount.Invoke(x) > 1000);
        var label = Projection<OrderRow>.Create(x => expensive.Invoke(x) ? x.Name : "cheap");
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new ResultRow
        {
            Name = label.Invoke(x),
            Valid = expensive.Invoke(x)
        });
        var rows = await query.ToListAsync();

        Assert.Equal(new[] { "cheap", "Desk", "Uncategorized", "Hidden" }, rows.Select(x => x.Name));
        Assert.Equal(new[] { false, true, true, true }, rows.Select(x => x.Valid));
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("CASE", command.Sql);
        Assert.Contains("TotalCents", query.ToQueryString());
    }

    [Fact]
    public async Task ChainedOperatorsExpandEveryMarkerAndRetainOrdering()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var amount = Projection<OrderRow>.Create(x => x.TotalCents);
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > 1000);
        var name = Projection<OrderRow>.Create(x => x.Name);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x))
            .OrderByDescending(x => amount.Invoke(x)).Select(x => name.Invoke(x));
        Assert.Equal(new[] { "Desk", "Hidden", "Uncategorized" }, await query.ToArrayAsync());
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("WHERE", command.Sql);
        Assert.Contains("ORDER BY", command.Sql);
    }

    [Fact]
    public async Task NestedAnyAndCountExecuteCorrelatedSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var line = Condition<LineRow>.Create(x => x.AmountCents >= 150);
        var query = fixture.Db.Orders
            .Where(x => x.Lines.Any(y => line.Invoke(y)))
            .Select(x => new { x.Id, Count = x.Lines.Count(y => line.Invoke(y)) });
        var row = Assert.Single(await query.ToListAsync());
        Assert.Equal(2, row.Id);
        Assert.Equal(2, row.Count);
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("EXISTS", command.Sql);
        Assert.Contains("COUNT", command.Sql);
    }

    [Fact]
    public async Task NestedMethodGroupsAndCapturedDelegatesExpandBeforeTranslation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var positive = Condition<LineRow>.Create(x => x.AmountCents >= 150);
        Func<LineRow, bool> predicate = positive.Invoke;
        var query = fixture.Db.Orders.Where(x => x.Lines.Any(positive.Invoke) && x.Lines.Any(predicate));
        Assert.Equal(new[] { 2 }, await query.Select(x => x.Id).ToArrayAsync());
        Assert.Contains("EXISTS", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task NullableNavigationReturnsCorrectNullAndValueTypeDefaults()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var name = Projection<CustomerRow>.Create(x => x.Name);
        var length = Projection<CustomerRow>.Create(x => x.Name.Length);
        var active = Condition<CustomerRow>.Create(x => x.Active);
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new
        {
            Name = name.InvokeOrDefault(x.Customer),
            Length = length.InvokeOrDefault(x.Customer),
            Active = active.InvokeOrDefault(x.Customer)
        });
        var rows = await query.ToArrayAsync();
        Assert.Equal(new string?[] { "Ada", "Ada", null, "Bob" }, rows.Select(x => x.Name));
        Assert.Equal(new[] { 3, 3, 0, 3 }, rows.Select(x => x.Length));
        Assert.Equal(new[] { true, true, false, false }, rows.Select(x => x.Active));
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("LEFT JOIN", command.Sql);
        Assert.Contains("CASE", command.Sql);
    }

    [Fact]
    public async Task NullableValueAndNonNullableValueInvocationRetainDefaultSemantics()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var nullable = Projection<int?, int>.Create(x => x!.Value + 1);
        var number = Projection<int, int>.Create(x => x + 1);
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new
        {
            CustomerNumber = nullable.InvokeOrDefault(x.CustomerId),
            Total = number.InvokeOrDefault(x.TotalCents)
        });
        var rows = await query.ToArrayAsync();
        Assert.Equal(new[] { 2, 2, 0, 3 }, rows.Select(x => x.CustomerNumber));
        Assert.Equal(new[] { 201, 20001, 1501, 9001 }, rows.Select(x => x.Total));
        Assert.Contains("CASE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task RepeatedExecutionAndSharedOptionsDoNotMutateOrLeakWrapperState()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Condition<OrderRow>.Create(x => x.Id == 2);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        var original = query.Expression;
        for (var i = 0; i < 3; i++) Assert.Equal(2, (await query.SingleAsync()).Id);
        Assert.Same(original, query.Expression);

        await using var second = new OrdersContext(fixture.Options);
        condition = Condition<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(4, (await second.Orders.Where(x => condition.Invoke(x)).SingleAsync()).Id);
        condition = Condition<OrderRow>.Create(x => x.Id == 1);
        Assert.Equal(1, (await query.SingleAsync()).Id);
        Assert.Equal(5, fixture.Commands.Executed.Count);
    }

    [Fact]
    public async Task InterceptorCancellationReachesEf()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var condition = Condition<OrderRow>.Create(x => x.Active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Db.Orders.AnyAsync(x => condition.Invoke(x), cancellation.Token));
    }

    [Fact]
    public async Task RowDependentWrapperTargetFailsBeforeExecutingSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => GetCondition(x).Invoke(x)).ToListAsync());
        Assert.Contains("Unable to resolve expression instance", error.Message);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CyclesFailWithoutStackOverflowOrSql(bool indirect)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = new LinkedCondition();
        condition.Next = indirect ? new LinkedCondition { Next = condition } : condition;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => condition.Invoke(x)).ToListAsync());
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task UnrelatedInvokeRetainsNormalEfTranslationFailure()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var unrelated = new Unrelated();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Db.Orders.Where(x => unrelated.Invoke(x)).ToListAsync());
        Assert.DoesNotContain("invocation marker", error.Message);
        Assert.Empty(fixture.Commands.Executed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuerySyntaxRemainsIndependentAndPreservesProvider(bool intercept)
    {
        await using var fixture = await SqliteFixture.CreateAsync(intercept);
        var condition = Condition<OrderRow>.Create(x => x.Active);
        var name = Projection<OrderRow>.Create(x => x.Name);
        var query = from row in fixture.Db.Orders.AsRaffinertQuery()
                    where condition.Invoke(row)
                    orderby row.Id
                    select name.Invoke(row);
        Assert.Equal(new[] { "Pencil", "Desk" }, await query.ToArrayAsync());
        Assert.Same(((IQueryable<OrderRow>)fixture.Db.Orders).Provider, query.Provider);
        Assert.DoesNotContain("Invoke", query.Expression.ToString());
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task RegistrationIsLocalIdempotentAndKeepsTypedOptions()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var builder = new DbContextOptionsBuilder<OrdersContext>(fixture.Options);
        Assert.Same(builder, builder.UseRaffinertExpressions().UseRaffinertExpressions());
        await using var context = new OrdersContext(builder.Options);
        var condition = Condition<OrderRow>.Create(x => x.Id == 2);
        Assert.Equal(2, (await context.Orders.Where(x => condition.Invoke(x)).SingleAsync()).Id);

        await using var unregistered = await SqliteFixture.CreateAsync(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unregistered.Db.Orders.Where(x => condition.Invoke(x)).ToArrayAsync());
        Assert.Empty(unregistered.Commands.Executed);
    }

    [Fact]
    public void NullOptionsBuildersAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseRaffinertExpressions());
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder<OrdersContext>)null!).UseRaffinertExpressions());
    }

    [Fact]
    public async Task NormalQueriesRetainEfSemanticsAndSqlParameters()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var threshold = 1000;
        var query = fixture.Db.Orders.Where(x => x.TotalCents > threshold).OrderBy(x => x.Id);
        Assert.Equal(new[] { 2, 3, 4 }, await query.Select(x => x.Id).ToArrayAsync());
        threshold = 10000;
        Assert.Equal(new[] { 2 }, await query.Select(x => x.Id).ToArrayAsync());
        Assert.Contains(1000, fixture.Commands.Executed[0].Values);
        Assert.Contains(10000, fixture.Commands.Executed[1].Values);
    }

    private static Condition<OrderRow> GetCondition(OrderRow row) => Condition<OrderRow>.Create(x => x.Id == row.Id);

    private sealed class ResultRow
    {
        public string Name { get; set; } = "";
        public bool Valid { get; set; }
    }

    private sealed class LinkedCondition : Condition<OrderRow>
    {
        public Condition<OrderRow> Next { get; set; } = null!;
        public override Expression<Func<OrderRow, bool>> GetExpression() => x => Next.Invoke(x);
    }

    private sealed class Unrelated
    {
        public bool Invoke(OrderRow row) => row.Active;
    }
}
