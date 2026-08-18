using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class SpecificationTests
{
    [Fact]
    public void BooleanConstantsAreSingletonsAndExecutable()
    {
        var product = new Product();

        Assert.Equal("value => true", Specification<Product>.True.GetExpandedExpression().ToReadableString());
        Assert.Equal("value => false", Specification<Product>.False.GetExpandedExpression().ToReadableString());
        Assert.True(Specification<Product>.True.Invoke(product));
        Assert.False(Specification<Product>.False.Invoke(product));
        Assert.Same(Specification<Product>.True, Specification<Product>.True);
        Assert.Same(Specification<Product>.False, Specification<Product>.False);
    }

    [Fact]
    public void BooleanCompositionProducesReadableExpandedPredicates()
    {
        Specification<Product> named = Specification<Product>.Create(product => product.Name == "Apple");
        Specification<Product> cheap = new CheapProductSpecification(10m);
        var product = new Product { Name = "Apple", Price = 5m };
        var namedAndCheap = named.And(cheap);
        var namedAndFive = named.And(value => value.Price == 5m);
        var namedOrFalse = named.Or(Specification<Product>.False);
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
        Assert.True((named | Specification<Product>.False).Invoke(product));
        Assert.False(notNamed.Invoke(product));
        Assert.True((named && cheap).Invoke(product));
        Assert.True((named || Specification<Product>.False).Invoke(product));
    }

    [Fact]
    public void EnumerableAndQueryableWhereUseSpecification()
    {
        var source = new[]
        {
            new Product { Name = "A", Price = 1m },
            new Product { Name = "B", Price = 20m }
        };
        var specification = Specification<Product>.Create(product => product.Price > 10m);

        var enumerable = source.Where(specification).Single();
        var queryable = source.AsQueryable().Where(specification).Single();

        Assert.Equal("B", enumerable.Name);
        Assert.Equal("B", queryable.Name);
    }

    private sealed class CheapProductSpecification(decimal threshold) : Specification<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price < threshold;
    }
}
