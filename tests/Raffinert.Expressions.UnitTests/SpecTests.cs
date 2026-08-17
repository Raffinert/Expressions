using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class SpecTests
{
    [Fact]
    public void InlineSubclassConstantsAndBooleanCompositionWork()
    {
        Spec<Product> named = Spec<Product>.Create(product => product.Name == "Apple");
        Spec<Product> cheap = new CheapProductSpec(10m);
        var product = new Product { Name = "Apple", Price = 5m };

        Assert.True(Spec<Product>.True.Invoke(product));
        Assert.False(Spec<Product>.False.Invoke(product));
        Assert.True(named.And(cheap).Invoke(product));
        Assert.True(named.And(p => p.Price == 5m).Invoke(product));
        Assert.True(named.Or(Spec<Product>.False).Invoke(product));
        Assert.True((named & cheap).Invoke(product));
        Assert.True((named | Spec<Product>.False).Invoke(product));
        Assert.False((!named).Invoke(product));
        Assert.True((named && cheap).Invoke(product));
        Assert.True((named || Spec<Product>.False).Invoke(product));
        Assert.Same(Spec<Product>.True, Spec<Product>.True);
        Assert.Same(Spec<Product>.False, Spec<Product>.False);
    }

    [Fact]
    public void EnumerableAndQueryableWhereUseSpecification()
    {
        var source = new[]
        {
            new Product { Name = "A", Price = 1m },
            new Product { Name = "B", Price = 20m }
        };
        var spec = Spec<Product>.Create(product => product.Price > 10m);

        var enumerable = source.Where(spec).Single();
        var queryable = source.AsQueryable().Where(spec).Single();

        Assert.Equal("B", enumerable.Name);
        Assert.Equal("B", queryable.Name);
    }

    private sealed class CheapProductSpec(decimal threshold) : Spec<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price < threshold;
    }
}
