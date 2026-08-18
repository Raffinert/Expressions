using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ConditionTests
{
    [Fact]
    public void BooleanConstantsAreSingletonsAndExecutable()
    {
        var product = new Product();

        Assert.Equal("value => true", Condition<Product>.True.GetExpandedExpression().ToReadableString());
        Assert.Equal("value => false", Condition<Product>.False.GetExpandedExpression().ToReadableString());
        Assert.True(Condition<Product>.True.Invoke(product));
        Assert.False(Condition<Product>.False.Invoke(product));
        Assert.Same(Condition<Product>.True, Condition<Product>.True);
        Assert.Same(Condition<Product>.False, Condition<Product>.False);
    }

    [Fact]
    public void BooleanCompositionProducesReadableExpandedConditions()
    {
        Condition<Product> named = Condition<Product>.Create(product => product.Name == "Apple");
        Condition<Product> cheap = new CheapProductCondition(10m);
        var product = new Product { Name = "Apple", Price = 5m };
        var namedAndCheap = named.And(cheap);
        var namedAndFive = named.And(value => value.Price == 5m);
        var namedOrFalse = named.Or(Condition<Product>.False);
        var notNamed = !named;

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
        Assert.True((named & cheap).Invoke(product));
        Assert.True((named | Condition<Product>.False).Invoke(product));
        Assert.False(notNamed.Invoke(product));
        Assert.True((named && cheap).Invoke(product));
        Assert.True((named || Condition<Product>.False).Invoke(product));
    }

    [Fact]
    public void EnumerableAndQueryableWhereUseCondition()
    {
        var source = new[]
        {
            new Product { Name = "A", Price = 1m },
            new Product { Name = "B", Price = 20m }
        };
        var condition = Condition<Product>.Create(product => product.Price > 10m);

        var enumerable = source.Where(condition).Single();
        var queryable = source.AsQueryable().Where(condition).Single();

        Assert.Equal("B", enumerable.Name);
        Assert.Equal("B", queryable.Name);
    }

    private sealed class CheapProductCondition(decimal threshold) : Condition<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price < threshold;
    }
}
