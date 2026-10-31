using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ConditionTests
{
    [Fact]
    public void BooleanConstantsAreOrdinaryReusableExpressions()
    {
        var product = new Product();
        var yes = Expr<Product>.Create(value => true);
        var no = Expr<Product>.Create(value => false);

        Assert.True(yes.Invoke(product));
        Assert.False(no.Invoke(product));
        Assert.Equal("value => true", yes.GetExpandedExpression().ToReadableString());
        Assert.Equal("value => false", no.GetExpandedExpression().ToReadableString());
        Assert.NotSame(yes, Expr<Product>.Create(value => true));
    }

    [Fact]
    public void EmptyBuilderDefaultsToFalseOrConfiguredTrue()
    {
        var product = new Product();
        Assert.False(PredicateBuilder<Product>.Start().Build().Invoke(product));
        Assert.True(PredicateBuilder<Product>.Start(defaultWhenEmpty: true).Build().Invoke(product));
    }

    [Fact]
    public void BuilderDropsSeedOnFirstAndAndOrAndSnapshotsAreIndependent()
    {
        var named = Expr<Product>.Create(product => product.Name == "Apple");
        var cheap = Expr<Product>.Create(product => product.Price < 10m);
        var builder = PredicateBuilder<Product>.Start();
        var before = builder.Build();
        var first = builder.Or(named).Build();
        var mixed = builder.And(cheap).Build();

        Assert.False(before.Invoke(new Product { Name = "Apple", Price = 5m }));
        Assert.True(first.Invoke(new Product { Name = "Apple", Price = 50m }));
        Assert.True(mixed.Invoke(new Product { Name = "Apple", Price = 5m }));
        Assert.False(mixed.Invoke(new Product { Name = "Apple", Price = 50m }));
        Assert.True(first.Invoke(new Product { Name = "Apple", Price = 50m }));
        Assert.True(PredicateBuilder<Product>.Start().And(named).Build()
            .Invoke(new Product { Name = "Apple" }));
    }

    [Fact]
    public void BooleanCompositionProducesReadableExpandedConditions()
    {
        Expr<Product, bool> named = Expr<Product>.Create(product => product.Name == "Apple");
        Expr<Product, bool> cheap = new CheapProductCondition(10m);
        var product = new Product { Name = "Apple", Price = 5m };
        var namedAndCheap = named.And(cheap);
        var namedAndFive = named.And(value => value.Price == 5m);
        var namedOrFalse = named.Or(Expr<Product>.Create(value => false));
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
        Assert.True(named.Or(Expr<Product>.Create(value => false)).Invoke(product));
        Assert.False(notNamed.Invoke(product));
        Assert.True(named.And(cheap).Invoke(product));
        Assert.True(named.Or(Expr<Product>.Create(value => false)).Invoke(product));
    }

    [Fact]
    public void EnumerableAndQueryableWhereUseCondition()
    {
        var source = new[]
        {
            new Product { Name = "A", Price = 1m },
            new Product { Name = "B", Price = 20m }
        };
        var condition = Expr<Product>.Create(product => product.Price > 10m);

        var enumerable = source.Where(condition).Single();
        var queryable = source.AsQueryable().Where(condition).Single();

        Assert.Equal("B", enumerable.Name);
        Assert.Equal("B", queryable.Name);
    }

    private sealed class CheapProductCondition(decimal threshold) : Expr<Product, bool>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price < threshold;
    }
}
