using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class CompositionTests
{

    [Fact]
    public async Task ContravariantInterfaceExpressionsTranslateInSqlite()
    {
        await using var fixture = await SqliteFixture.CreateAsync();

        IExpr<OrderRowBase, bool> baseActive = Expr<OrderRowBase>.Create(row => row.Active);
        IExpr<OrderRow, bool> active = baseActive;
        IExpr<OrderRowBase, string> baseName = Expr<OrderRowBase>.Create(row => row.Name);
        IExpr<OrderRow, string> name = baseName;

        var filtered = await fixture.Db.Orders.Where(active)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        Assert.Equal([1, 2], filtered);

        var names = await fixture.Db.Orders.OrderBy(row => row.Id)
            .Select(name).ToArrayAsync();
        Assert.Equal(["Pencil", "Desk", "Uncategorized", "Hidden"], names);

        var combined = active.And(Expr<OrderRow>.Create(row => row.TotalCents > 1000));
        var combinedIds = await fixture.Db.Orders.Where(combined)
            .Select(row => row.Id).ToArrayAsync();
        Assert.Equal([2], combinedIds);

        // Whole-query expansion must also support a contravariant invocation marker.
        var invoked = await fixture.Db.Orders.Where(row => active.Invoke(row))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        Assert.Equal([1, 2], invoked);
        Assert.All(fixture.Commands.Executed,
            command => Assert.DoesNotContain("Invoke", command.Sql, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EfAdapterExpandsPlainQuerySyntaxWithoutAsRaffinertQuery()
    {
        await using var fixture = await SqliteFixture.CreateAsync(intercept: true);
        var active = Expr<OrderRow>.Create(x => x.Active);
        var name = Expr<OrderRow>.Create(x => x.Name);

        // Intentionally use ordinary C# query syntax without AsRaffinertQuery().
        var query =
            from order in fixture.Db.Orders
            where active.Invoke(order)
            orderby name.Invoke(order)
            select new
            {
                order.Id,
                Name = name.Invoke(order)
            };

        var rows = await query.ToArrayAsync();

        Assert.Equal(new[] { 2, 1 }, rows.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { "Desk", "Pencil" }, rows.Select(x => x.Name).ToArray());

        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("WHERE", command.Sql);
        Assert.Contains("ORDER BY", command.Sql);
        Assert.DoesNotContain("Invoke", command.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlainQuerySyntaxWithInvokeRequiresRegisteredEfAdapter()
    {
        await using var fixture = await SqliteFixture.CreateAsync(intercept: false);
        var active = Expr<OrderRow>.Create(x => x.Active);

        var query =
            from order in fixture.Db.Orders
            where active.Invoke(order)
            select order.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());

        Assert.Empty(fixture.Commands.Executed);
    }

    [Fact]
    public async Task AsyncTerminalWithEmbeddedMarkerExecutesSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Expr<OrderRow>.Create(x => x.TotalCents > 1000);
        Assert.True(await fixture.Db.Orders.AnyAsync(x => condition.Invoke(x)));
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task NestedProjectionAndCrossCompositionTranslateSelection()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var amount = Expr<OrderRow>.Create(x => x.TotalCents);
        var expensive = Expr<OrderRow>.Create(x => amount.Invoke(x) > 1000);
        var label = Expr<OrderRow>.Create(x => expensive.Invoke(x) ? x.Name : "cheap");
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new ResultRow
        {
            Name = label.Invoke(x),
            Valid = expensive.Invoke(x)
        });
        var rows = await query.ToListAsync();

        Assert.Equal(["cheap", "Desk", "Uncategorized", "Hidden"], rows.Select(x => x.Name));
        Assert.Equal([false, true, true, true], rows.Select(x => x.Valid));
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("CASE", command.Sql);
        Assert.Contains("TotalCents", query.ToQueryString());
    }

    [Fact]
    public async Task ChainedOperatorsExpandEveryMarkerAndRetainOrdering()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var amount = Expr<OrderRow>.Create(x => x.TotalCents);
        var condition = Expr<OrderRow>.Create(x => x.TotalCents > 1000);
        var name = Expr<OrderRow>.Create(x => x.Name);
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
        var line = Expr<LineRow>.Create(x => x.AmountCents >= 150);
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
        var positive = Expr<LineRow>.Create(x => x.AmountCents >= 150);
        Func<LineRow, bool> predicate = positive.Invoke;
        var query = fixture.Db.Orders.Where(x => x.Lines.Any(positive.Invoke) && x.Lines.Any(predicate));
        Assert.Equal(new[] { 2 }, await query.Select(x => x.Id).ToArrayAsync());
        Assert.Contains("EXISTS", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task NullableNavigationReturnsCorrectNullAndValueTypeDefaults()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var name = Expr<CustomerRow>.Create(x => x.Name);
        var length = Expr<CustomerRow>.Create(x => x.Name.Length);
        var active = Expr<CustomerRow>.Create(x => x.Active);
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new
        {
            Name = name.InvokeOrDefault(x.Customer),
            Length = length.InvokeOrDefault(x.Customer),
            Active = active.InvokeOrDefault(x.Customer)
        });
        var rows = await query.ToArrayAsync();
        Assert.Equal(["Ada", "Ada", null, "Bob"], rows.Select(x => x.Name));
        Assert.Equal([3, 3, 0, 3], rows.Select(x => x.Length));
        Assert.Equal([true, true, false, false], rows.Select(x => x.Active));
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("LEFT JOIN", command.Sql);
        Assert.Contains("CASE", command.Sql);
    }

    [Fact]
    public async Task NullableValueAndNonNullableValueInvocationRetainDefaultSemantics()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var nullable = Expr<int?, int>.Create(x => x!.Value + 1);
        var number = Expr<int, int>.Create(x => x + 1);
        var query = fixture.Db.Orders.OrderBy(x => x.Id).Select(x => new
        {
            CustomerNumber = nullable.InvokeOrDefault(x.CustomerId),
            Total = number.InvokeOrDefault(x.TotalCents)
        });
        var rows = await query.ToArrayAsync();
        Assert.Equal([2, 2, 0, 3], rows.Select(x => x.CustomerNumber));
        Assert.Equal([201, 20001, 1501, 9001], rows.Select(x => x.Total));
        Assert.Contains("CASE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task RepeatedExecutionAndSharedOptionsDoNotMutateOrLeakWrapperState()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var condition = Expr<OrderRow>.Create(x => x.Id == 2);
        var query = fixture.Db.Orders.Where(x => condition.Invoke(x));
        var original = query.Expression;
        for (var i = 0; i < 3; i++) Assert.Equal(2, (await query.SingleAsync()).Id);
        Assert.Same(original, query.Expression);

        await using var second = new OrdersContext(fixture.Options);
        condition = Expr<OrderRow>.Create(x => x.Id == 4);
        Assert.Equal(4, (await second.Orders.Where(x => condition.Invoke(x)).SingleAsync()).Id);
        condition = Expr<OrderRow>.Create(x => x.Id == 1);
        Assert.Equal(1, (await query.SingleAsync()).Id);
        Assert.Equal(5, fixture.Commands.Executed.Count);
    }

    [Fact]
    public async Task InterceptorCancellationReachesEf()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var condition = Expr<OrderRow>.Create(x => x.Active);
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
        var condition = Expr<OrderRow>.Create(x => x.Active);
        var name = Expr<OrderRow>.Create(x => x.Name);
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
        var condition = Expr<OrderRow>.Create(x => x.Id == 2);
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

    [Fact]
    public async Task InlinedProviderDateTimeFunctionRetainsServerTranslation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var timestamp = Expr<OrderRow>.Create(x => DateTime.UtcNow);
        var rows = await fixture.Db.Orders.Select(x => timestamp.Invoke(x)).ToArrayAsync();
        Assert.Equal(4, rows.Length);
        Assert.All(rows, x => Assert.True(x.Year >= 2000));
        Assert.Contains("'now'", Assert.Single(fixture.Commands.Executed).Sql);
    }

    private static Expr<OrderRow, bool> GetCondition(OrderRow row) => Expr<OrderRow>.Create(x => x.Id == row.Id);

    private sealed class ResultRow
    {
        public string Name { get; set; } = "";
        public bool Valid { get; set; }
    }

    private sealed class LinkedCondition : Expr<OrderRow, bool>
    {
        public Expr<OrderRow, bool> Next { get; set; } = null!;
        public override Expression<Func<OrderRow, bool>> GetExpression() => x => Next.Invoke(x);
    }

    private sealed class Unrelated
    {
        public bool Invoke(OrderRow row) => row.Active;
    }
}
