using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class SpecificationTests
{
    [Fact]
    public void InlineSubclassConstantsAndBooleanCompositionWork()
    {
        Specification<Product> named = Specification<Product>.Create(product => product.Name == "Apple");
        Specification<Product> cheap = new CheapProductSpecification(10m);
        var product = new Product { Name = "Apple", Price = 5m };

        Assert.True(Specification<Product>.True.Invoke(product));
        Assert.False(Specification<Product>.False.Invoke(product));
        Assert.True(named.And(cheap).Invoke(product));
        Assert.True(named.And(p => p.Price == 5m).Invoke(product));
        Assert.True(named.Or(Specification<Product>.False).Invoke(product));
        Assert.True((named & cheap).Invoke(product));
        Assert.True((named | Specification<Product>.False).Invoke(product));
        Assert.False((!named).Invoke(product));
        Assert.True((named && cheap).Invoke(product));
        Assert.True((named || Specification<Product>.False).Invoke(product));
        Assert.Same(Specification<Product>.True, Specification<Product>.True);
        Assert.Same(Specification<Product>.False, Specification<Product>.False);
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
