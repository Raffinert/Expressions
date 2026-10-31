using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class PredicateBuilderIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectSqliteWhereSupportsBalancedAndUnbalancedBuilder(bool balanced)
    {
        await using var fixture = await SqliteFixture.CreateAsync(intercept: false);
        var builder = PredicateBuilder<OrderRow>.Start()
            .And(row => row.Active)
            .And(row => row.TotalCents > 1000)
            .And(row => row.Name.Length > 0)
            .Or(row => row.Id == 4);

        var predicate = builder.Build(balanced);
        var ids = await fixture.Db.Orders.Where(predicate)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        Assert.Equal([2, 4], ids);
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
        Assert.DoesNotContain("Invoke", fixture.Commands.Executed[0].Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NestedInvokeAndCapturedScalarsAreTranslatedUsingNativeEfParameterization()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var threshold = 1000;
        var predicate = PredicateBuilder<OrderRow>.Start()
            .And(row => row.Active)
            .And(row => row.TotalCents > threshold)
            .And(row => row.Name != "")
            .Build();

        var nested = Expr<OrderRow>.Create(row => predicate.Invoke(row));
        var query = fixture.Db.Orders.Where(row => nested.Invoke(row))
            .OrderBy(row => row.Id).Select(row => row.Id);

        Assert.Equal([2], await query.ToArrayAsync());
        threshold = 30000;
        Assert.Empty(await query.ToArrayAsync());
        Assert.Contains(1000, fixture.Commands.Executed[0].Values);
        Assert.Contains(30000, fixture.Commands.Executed[1].Values);
        Assert.All(fixture.Commands.Executed,
            command => Assert.DoesNotContain("Invoke", command.Sql, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContravariantAndExternalPredicateLeavesExecuteInSqlite()
    {
        await using var fixture = await SqliteFixture.CreateAsync(intercept: false);
        IExpr<OrderRowBase, bool> sharedBase = Expr<OrderRowBase>.Create(row => row.Active);
        IExpr<OrderRow, bool> shared = sharedBase;
        IExpr<OrderRow, bool> external = new ExternalPredicate();

        var predicate = PredicateBuilder<OrderRow>.Start()
            .And(shared)
            .And(external)
            .Build();
        var ids = await fixture.Db.Orders.Where(predicate)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

        Assert.Equal([2], ids);
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    private sealed class ExternalPredicate : IExpr<OrderRow, bool>
    {
        public bool Invoke(OrderRow row) => row.TotalCents >= 1000;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<OrderRow, bool>>)(row => row.TotalCents >= 1000);
    }
}
