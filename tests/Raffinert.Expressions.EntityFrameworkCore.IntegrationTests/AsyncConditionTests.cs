using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class AsyncConditionTests
{
    public static TheoryData<string> Operators =>
    [
        "Any", "All", "Count", "LongCount", "First", "FirstOrDefault",
        "Single", "SingleOrDefault", "Last", "LastOrDefault"
    ];

    [Fact]
    public async Task BooleanAndCountOperatorsExecutePredicatesWithoutInterceptor()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var active = Condition<OrderRow>.Create(x => x.Active);
        var empty = Condition<OrderRow>.Create(x => x.Id < 0);
        IComposableExpression<OrderRow, bool> interfaceCondition = active;

        Assert.True(await fixture.Db.Orders.AnyAsync(interfaceCondition));
        Assert.False(await fixture.Db.Orders.AnyAsync(empty));
        Assert.False(await fixture.Db.Orders.AllAsync(active));
        Assert.True(await fixture.Db.Orders.AllAsync(Condition<OrderRow>.True));
        Assert.True(await fixture.Db.Orders.Where(x => x.Id < 0).AllAsync(active));
        Assert.Equal(2, await fixture.Db.Orders.CountAsync(active));
        Assert.Equal(2L, await fixture.Db.Orders.LongCountAsync(active));
        Assert.Equal(0, await fixture.Db.Orders.CountAsync(empty));
        Assert.Equal(0L, await fixture.Db.Orders.LongCountAsync(empty));
        // EF can simplify All(True) to SELECT 1; nonconstant conditions must filter in SQL.
        Assert.Equal(9, fixture.Commands.Executed.Count);
        Assert.Contains("SELECT", fixture.Commands.Executed[3].Sql);
        Assert.All(fixture.Commands.Executed.Where((_, index) => index != 3), x => Assert.Contains("WHERE", x.Sql));
        Assert.Contains(fixture.Commands.Executed, x => x.Sql.Contains("EXISTS"));
        Assert.Contains(fixture.Commands.Executed, x => x.Sql.Contains("COUNT"));
    }

    [Theory]
    [InlineData("First", 1)]
    [InlineData("FirstOrDefault", 1)]
    [InlineData("Last", 2)]
    [InlineData("LastOrDefault", 2)]
    public async Task OrderedOperatorsRetainEfSemantics(string operation, int expectedId)
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var query = fixture.Db.Orders.OrderBy(x => x.Id);
        var active = Condition<OrderRow>.Create(x => x.Active);
        var result = Assert.IsType<OrderRow>(await ExecuteAsync(operation, query, active));

        Assert.Equal(expectedId, result.Id);
        var command = Assert.Single(fixture.Commands.Executed);
        Assert.Contains("WHERE", command.Sql);
        Assert.Contains("ORDER BY", command.Sql);
        Assert.Contains("LIMIT", command.Sql);

        var empty = Condition<OrderRow>.Create(x => x.Id < 0);
        if (operation.EndsWith("OrDefault", StringComparison.Ordinal))
            Assert.Null(await ExecuteAsync(operation, query, empty));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation, query, empty));
    }

    [Theory]
    [InlineData("Single")]
    [InlineData("SingleOrDefault")]
    public async Task SingleOperatorsRetainEfSemantics(string operation)
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var one = Condition<OrderRow>.Create(x => x.Id == 2);
        Assert.Equal(2, Assert.IsType<OrderRow>(await ExecuteAsync(operation, fixture.Db.Orders, one)).Id);
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);

        var empty = Condition<OrderRow>.Create(x => x.Id < 0);
        if (operation.EndsWith("OrDefault", StringComparison.Ordinal))
            Assert.Null(await ExecuteAsync(operation, fixture.Db.Orders, empty));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation, fixture.Db.Orders, empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(operation, fixture.Db.Orders, Condition<OrderRow>.True));
    }

    [Fact]
    public async Task NestedProjectionConditionAndMethodGroupExecuteAsSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var amount = Projection<OrderRow>.Create(x => x.TotalCents);
        var positiveLine = Condition<LineRow>.Create(x => x.AmountCents > 150);
        var nested = Condition<OrderRow>.Create(x => x.Lines.Any(positiveLine.Invoke));
        var condition = Condition<OrderRow>.Create(x => amount.Invoke(x) > 1000 && nested.Invoke(x));
        Assert.Equal(1, await fixture.Db.Orders.CountAsync(condition));
        Assert.True(await fixture.Db.Orders.AnyAsync(condition));
        Assert.All(fixture.Commands.Executed, x =>
        {
            Assert.Contains("WHERE", x.Sql);
            Assert.Contains("AmountCents", x.Sql);
        });
    }

    [Fact]
    public async Task ExpandedPredicatesUseCurrentCapturedScalarsWithNormalEfParameterization()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var threshold = 1000;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        Assert.Equal(3, await fixture.Db.Orders.CountAsync(condition));
        threshold = 10000;
        Assert.Equal(1, await fixture.Db.Orders.CountAsync(condition));
        Assert.Contains(1000, fixture.Commands.Executed[0].Values);
        Assert.Contains(10000, fixture.Commands.Executed[1].Values);
    }

    [Fact]
    public async Task NormalEfAndComposableOverloadsResolveWithDocumentedImports()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var condition = Condition<OrderRow>.Create(x => x.Active);
        Assert.True(await fixture.Db.Orders.AnyAsync(condition));
        Assert.True(await fixture.Db.Orders.AnyAsync(x => x.Active));
        Assert.True(await fixture.Db.Orders.AnyAsync());
        Assert.Equal(2, await fixture.Db.Orders.CountAsync(x => x.Active));
        var error = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Db.Orders.AnyAsync((Expression<Func<OrderRow, bool>>)null!));
        Assert.Equal("predicate", error.ParamName);
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public async Task NullArgumentsAreRejectedExplicitly(string operation)
    {
        var source = Array.Empty<OrderRow>().AsQueryable();
        var sourceError = await Assert.ThrowsAsync<ArgumentNullException>(() => ExecuteAsync(operation, null!, Condition<OrderRow>.True));
        Assert.Equal("source", sourceError.ParamName);
        var conditionError = await Assert.ThrowsAsync<ArgumentNullException>(() => ExecuteAsync(operation, source, null!));
        Assert.Equal("condition", conditionError.ParamName);
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public async Task CancellationTokenReachesEfAndTheProvider(string operation)
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        using var cancellation = new CancellationTokenSource();
        var condition = Condition<OrderRow>.Create(x => x.Id == 2);
        var query = fixture.Db.Orders.OrderBy(x => x.Id);
        await ExecuteAsync(operation, query, condition, cancellation.Token);
        Assert.Equal(cancellation.Token, Assert.Single(fixture.Commands.Executed).CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecuteAsync(operation, query, condition, cancellation.Token));
    }

    [Fact]
    public async Task ProviderErrorsAndUntranslatablePredicatesAreNotHidden()
    {
        await using var fixture = await SqliteFixture.CreateAsync(false);
        var unsupported = Condition<OrderRow>.Create(x => CustomPredicate(x.Name));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.Orders.AnyAsync(unsupported));
        Assert.Empty(fixture.Commands.Executed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Array.Empty<OrderRow>().AsQueryable().AnyAsync(Condition<OrderRow>.True));
    }

    private static bool CustomPredicate(string value) => value.Length > 1;

    private static async Task<object?> ExecuteAsync(string operation, IQueryable<OrderRow> query,
        IComposableExpression<OrderRow, bool> condition, CancellationToken cancellationToken = default) => operation switch
        {
            "Any" => await query.AnyAsync(condition, cancellationToken),
            "All" => await query.AllAsync(condition, cancellationToken),
            "Count" => await query.CountAsync(condition, cancellationToken),
            "LongCount" => await query.LongCountAsync(condition, cancellationToken),
            "First" => await query.FirstAsync(condition, cancellationToken),
            "FirstOrDefault" => await query.FirstOrDefaultAsync(condition, cancellationToken),
            "Single" => await query.SingleAsync(condition, cancellationToken),
            "SingleOrDefault" => await query.SingleOrDefaultAsync(condition, cancellationToken),
            "Last" => await query.LastAsync(condition, cancellationToken),
            "LastOrDefault" => await query.LastOrDefaultAsync(condition, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
}
