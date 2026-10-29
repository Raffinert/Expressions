using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ConditionTests
{
    [Fact]
    public void BooleanConstantsAreSingletonsAndExecutable()
    {
        var product = new Product();

        Assert.Equal("value => true", ComposableExpression<Product>.True.GetExpandedExpression().ToReadableString());
        Assert.Equal("value => false", ComposableExpression<Product>.False.GetExpandedExpression().ToReadableString());
        Assert.True(ComposableExpression<Product>.True.Invoke(product));
        Assert.False(ComposableExpression<Product>.False.Invoke(product));
        Assert.Same(ComposableExpression<Product>.True, ComposableExpression<Product>.True);
        Assert.Same(ComposableExpression<Product>.False, ComposableExpression<Product>.False);
    }

    [Fact]
    public void StartDefaultsToFalseWhenEmpty()
    {
        var product = new Product();
        var start = ComposableExpression<Product>.Start();

        Assert.False(start.Invoke(product));
        Assert.Equal("value => false", start.GetExpandedExpression().ToReadableString());
    }

    [Fact]
    public void StartCanReturnTrueWhenEmpty()
    {
        var product = new Product();
        var start = ComposableExpression<Product>.Start(defaultWhenEmpty: true);
        var named = ComposableExpression<Product>.Create(value => value.Name == "Apple");

        Assert.True(start.Invoke(product));
        Assert.Equal("value => true", start.GetExpandedExpression().ToReadableString());
        Assert.Same(named, start.And(named));
    }

    [Fact]
    public void StartDropsTheSeedForEitherFirstOperatorAndRemainsImmutable()
    {
        var start = ComposableExpression<Product>.Start();
        var named = ComposableExpression<Product>.Create(product => product.Name == "Apple");
        var cheap = ComposableExpression<Product>.Create(product => product.Price < 10m);

        Assert.Same(named, start.And(named));
        Assert.Same(named, start.Or(named));
        Assert.Equal("product => product.Name == \"Apple\"",
            start.And(product => product.Name == "Apple").GetExpandedExpression().ToReadableString());
        Assert.Equal("product => product.Name == \"Apple\"",
            start.Or(product => product.Name == "Apple").GetExpandedExpression().ToReadableString());

        var mixed = start.Or(named).And(cheap);
        Assert.Equal("product => (product.Name == \"Apple\") && (product.Price < 10m)",
            mixed.GetExpandedExpression().ToReadableString());
        Assert.False(start.Invoke(new Product { Name = "Apple", Price = 5m }));
        Assert.True(mixed.Invoke(new Product { Name = "Apple", Price = 5m }));
    }

    [Fact]
    public void BooleanCompositionProducesReadableExpandedConditions()
    {
        ComposableExpression<Product, bool> named = ComposableExpression<Product>.Create(product => product.Name == "Apple");
        ComposableExpression<Product, bool> cheap = new CheapProductCondition(10m);
        var product = new Product { Name = "Apple", Price = 5m };
        var namedAndCheap = named.And(cheap);
        var namedAndFive = named.And(value => value.Price == 5m);
        var namedOrFalse = named.Or(ComposableExpression<Product>.False);
        var notNamed = named.Not();

        Assert.Equal(
            "product => (product.Name == \"Apple\") && (product.Price < <threshold>P)",
            namedAndCheap.GetExpandedExpression().ToReadableString());
        Assert.Equal(
            "product => (product.Name == \"Apple\") && (product.Price == 5m)",
            namedAndFive.GetExpandedExpression().ToReadableString());
        Assert.Equal(
            "product => (product.Name == \"Apple\") || false",
            namedOrFalse.GetExpandedExpression().ToReadableString());
        Assert.Equal(
            "product => !(product.Name == \"Apple\")",
            notNamed.GetExpandedExpression().ToReadableString());
        Assert.True(namedAndCheap.Invoke(product));
        Assert.True(namedAndFive.Invoke(product));
        Assert.True(namedOrFalse.Invoke(product));
        Assert.True(named.And(cheap).Invoke(product));
        Assert.True(named.Or(ComposableExpression<Product>.False).Invoke(product));
        Assert.False(notNamed.Invoke(product));
        Assert.True(named.And(cheap).Invoke(product));
        Assert.True(named.Or(ComposableExpression<Product>.False).Invoke(product));
    }

    [Fact]
    public void EnumerableAndQueryableWhereUseCondition()
    {
        var source = new[]
        {
            new Product { Name = "A", Price = 1m },
            new Product { Name = "B", Price = 20m }
        };
        var condition = ComposableExpression<Product>.Create(product => product.Price > 10m);

        var enumerable = source.Where(condition).Single();
        var queryable = source.AsQueryable().Where(condition).Single();

        Assert.Equal("B", enumerable.Name);
        Assert.Equal("B", queryable.Name);
    }

    private sealed class CheapProductCondition(decimal threshold) : ComposableExpression<Product, bool>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price < threshold;
    }
}
