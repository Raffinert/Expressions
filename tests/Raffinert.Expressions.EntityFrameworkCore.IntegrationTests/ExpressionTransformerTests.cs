using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ExpressionTransformerTests
{
    [Fact]
    public async Task ExternalOptimizerRunsAfterRaffinertExpansionBeforeEfTranslation()
    {
        var calls = 0;
        var sawExpandedPredicate = false;
        await using var fixture = await SqliteFixture.CreateAsync(configure: builder =>
            builder.UseRaffinertExpressions(options => options.Transform(query =>
            {
                calls++;
                var printed = query.ToString();
                sawExpandedPredicate |= printed.Contains("TotalCents") &&
                                        !printed.Contains(".Invoke(");
                // Real external F# expression optimizer; no Raffinert dependency on it.
                return ExpressionOptimizer.visit(query);
            })));

        var threshold = 1000;
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
        var ids = await fixture.Db.Orders
            .Where(x => condition.Invoke(x) && !(!x.Active))
            .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();

        Assert.Equal(new[] { 2 }, ids);
        Assert.True(calls > 0);
        Assert.True(sawExpandedPredicate);
        Assert.Contains("WHERE", Assert.Single(fixture.Commands.Executed).Sql);
    }

    [Fact]
    public async Task TransformersRunInRegistrationOrderAndAreScopedToOptions()
    {
        var order = new List<int>();
        await using var transformed = await SqliteFixture.CreateAsync(configure: builder =>
            builder.UseRaffinertExpressions(options =>
            {
                options.Transform(query => { order.Add(1); return query; });
                options.Transform(query => { order.Add(2); return query; });
            }));
        order.Clear(); // discard any queries during database setup
        Assert.Equal(4, await transformed.Db.Orders.CountAsync());
        Assert.Equal(new[] { 1, 2 }, order);

        await using var ordinary = await SqliteFixture.CreateAsync();
        order.Clear();
        Assert.Equal(4, await ordinary.Db.Orders.CountAsync());
        Assert.Empty(order);
    }

    [Fact]
    public async Task NullTransformationResultFailsExplicitly()
    {
        await using var fixture = await SqliteFixture.CreateAsync(configure: builder =>
            builder.UseRaffinertExpressions(options => options.Transform(_ => null!)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Db.Orders.CountAsync());
        Assert.Contains("returned null", exception.Message);
    }
}
