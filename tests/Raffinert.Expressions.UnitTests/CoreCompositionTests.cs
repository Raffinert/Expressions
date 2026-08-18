using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class CoreCompositionTests
{
    [Fact]
    public void SpecificationInsideProjectionIsInlinedAndExecutes()
    {
        var expensive = Specification<Product>.Create(product => product.Price > 100m);
        var projection = Projection<Product, ProductDto>.Create(product => new ProductDto
        {
            Name = product.Name,
            IsExpensive = expensive.Invoke(product)
        });

        var expanded = projection.GetExpandedExpression();

        Assert.Equal("""
                     product => new ProductDto
                     {
                         Name = product.Name,
                         IsExpensive = expensive.Invoke(product)
                     }
                     """, projection.GetExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("""
                     product => new ProductDto
                     {
                         Name = product.Name,
                         IsExpensive = product.Price > 100m
                     }
                     """, expanded.ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.False(ContainsInvocation(expanded));
        Assert.True(projection.Invoke(new Product { Name = "Desk", Price = 200m }).IsExpensive);
    }

    [Fact]
    public void ProjectionInsideSpecificationIsInlinedAndExecutes()
    {
        var total = Projection<Order, decimal>.Create(order =>
            order.Lines.Sum(line => line.Price * line.Quantity));
        var expensive = Specification<Order>.Create(order => total.Invoke(order) > 1000m);

        var expanded = expensive.GetExpandedExpression();

        Assert.Equal(
            "order => order.Lines.Sum(line => line.Price * line.Quantity) > 1000m",
            expanded.ToReadableString());
        Assert.True(expensive.Invoke(new Order
        {
            Lines = { new OrderLine { Price = 600m, Quantity = 2 } }
        }));
    }

    [Fact]
    public void DeepMixedCompositionExpandsRecursively()
    {
        var price = Projection<Product, decimal>.Create(product => product.Price);
        var rounded = Projection<Product, decimal>.Create(product => decimal.Round(price.Invoke(product)));
        var high = Specification<Product>.Create(product => rounded.Invoke(product) >= 10m);
        var result = Projection<Product, bool>.Create(product => high.Invoke(product));

        var expanded = result.GetExpandedExpression();

        Assert.Equal(
            "product => decimal.Round(product.Price) >= 10m",
            expanded.ToReadableString());
        Assert.True(result.Invoke(new Product { Price = 10.4m }));
    }

    [Fact]
    public void ThenComposesProjectionAndProjectionWithoutInvocationNodes()
    {
        var customer = Projection<Order, Customer>.Create(order => order.Customer);
        var name = Projection<Customer, string>.Create(value => value.Name);

        var composed = customer.Then(name);
        var expanded = composed.GetExpandedExpression();

        Assert.Equal("order => order.Customer.Name", expanded.ToReadableString());
        Assert.Equal("Ada", composed.Invoke(new Order { Customer = new Customer { Name = "Ada" } }));
        Assert.False(ContainsInvocation(expanded));
    }

    [Fact]
    public void ThenComposesProjectionAndSpecification()
    {
        var customer = Projection<Order, Customer>.Create(order => order.Customer);
        var active = Specification<Customer>.Create(value => value.IsActive);

        var composed = customer.Then(active);

        Assert.Equal("order => order.Customer.IsActive", composed.GetExpandedExpression().ToReadableString());
        Assert.True(composed.Invoke(new Order { Customer = new Customer { IsActive = true } }));
    }

    [Fact]
    public void DirectNewSubclassInvocationIsExpanded()
    {
        var outer = Specification<Product>.Create(product => new MinimumPriceSpecification(12m).Invoke(product));

        Assert.Equal("product => product.Price >= <minimum>P", outer.GetExpandedExpression().ToReadableString());
        Assert.True(outer.Invoke(new Product { Price = 15m }));
    }

    [Fact]
    public void ClosureMemberChainsAndReadonlyFieldsAreResolved()
    {
        var holder = new SpecificationHolder(new MinimumPriceSpecification(12m));
        var outer = Specification<Product>.Create(product => holder.Nested.Minimum.Invoke(product));

        Assert.Equal("product => product.Price >= <minimum>P", outer.GetExpandedExpression().ToReadableString());
        Assert.True(outer.Invoke(new Product { Price = 15m }));
    }

    [Fact]
    public void StaticTargetsResolveAndNestedExpandedExpressionsAreCached()
    {
        var counting = new CountingSpecification();
        var first = Specification<Product>.Create(product => counting.Invoke(product));
        var second = Specification<Product>.Create(product => counting.Invoke(product) && StaticSpecifications.Minimum.Invoke(product));

        first.GetExpandedExpression();
        var expanded = second.GetExpandedExpression();

        Assert.Equal(
            "product => (product.Price > 0m) && (product.Price >= <minimum>P)",
            expanded.ToReadableString());
        Assert.Equal(1, counting.ExpressionRequests);
        Assert.True(second.Invoke(new Product { Price = 20m }));
    }

    [Fact]
    public void CanonicalInvocationMethodsAreExpanded()
    {
        var positive = Specification<Product>.Create(product => product.Price > 0m);
        var name = Projection<Product, string>.Create(product => product.Name);
        var outer = Projection<Product, ProductDto>.Create(product => new ProductDto
        {
            IsExpensive = positive.Invoke(product),
            Name = name.Invoke(product)
        });

        Assert.Equal("""
                     product => new ProductDto
                     {
                         IsExpensive = product.Price > 0m,
                         Name = product.Name
                     }
                     """, outer.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("Book", outer.Invoke(new Product { Name = "Book", Price = 1m }).Name);
    }

    [Fact]
    public void InvokeOrDefaultIsSharedBySpecificationsAndProjections()
    {
        var active = Specification<Customer>.Create(customer => customer.IsActive);
        var nameLength = Projection<Customer, int>.Create(customer => customer.Name.Length);
        var specification = Specification<NullableCustomerHolder>.Create(holder => active.InvokeOrDefault(holder.Customer));
        var projection = Projection<NullableCustomerHolder, int>.Create(
            holder => nameLength.InvokeOrDefault(holder.Customer));

        Assert.Equal(
            "holder => (holder.Customer == null) ? default(bool) : holder.Customer.IsActive",
            specification.GetExpandedExpression().ToReadableString());
        Assert.Equal(
            "holder => (holder.Customer == null) ? default(int) : holder.Customer.Name.Length",
            projection.GetExpandedExpression().ToReadableString());
        Assert.False(specification.Invoke(new NullableCustomerHolder()));
        Assert.Equal(0, projection.Invoke(new NullableCustomerHolder()));
        Assert.True(specification.Invoke(new NullableCustomerHolder { Customer = new Customer { IsActive = true } }));
        Assert.Equal(3, projection.Invoke(new NullableCustomerHolder { Customer = new Customer { Name = "Ada" } }));
    }

    [Fact]
    public void MethodGroupsInAnyAndSelectAreExpanded()
    {
        var positive = Specification<Product>.Create(product => product.Price > 0m);
        var name = Projection<Product, string>.Create(product => product.Name);
        var groupProjection = Projection<ProductGroup, GroupDto>.Create(group => new GroupDto
        {
            HasPositive = group.Products.Any(positive.Invoke),
            Names = group.Products.Select(name.Invoke).ToArray()
        });

        var expanded = groupProjection.GetExpandedExpression();
        var mapped = groupProjection.Invoke(new ProductGroup
        {
            Products = { new Product { Name = "A", Price = 2m } }
        });

        Assert.Equal("""
                     group => new GroupDto
                     {
                         HasPositive = group.Products.Any(product => product.Price > 0m),
                         Names = group.Products.Select(product => product.Name).ToArray()
                     }
                     """, expanded.ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.True(mapped.HasPositive);
        Assert.Equal(["A"], mapped.Names);
    }

    [Fact]
    public void ParameterSubstitutionPreservesAReallyShadowedNestedLambda()
    {
        var shared = Expression.Parameter(typeof(int), "number");
        var range = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Range),
            Type.EmptyTypes,
            Expression.Constant(0),
            Expression.Constant(1));
        var selector = Expression.Lambda<Func<int, int>>(shared, shared);
        var selected = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Select),
            [typeof(int), typeof(int)],
            range,
            selector);
        var sum = Expression.Call(typeof(Enumerable), nameof(Enumerable.Sum), Type.EmptyTypes, selected);
        var unusual = Projection<int, int>.Create(
            Expression.Lambda<Func<int, int>>(Expression.Add(sum, shared), shared));
        var input = Projection<NumberHolder, int>.Create(holder => holder.Value);

        var composed = input.Then(unusual);

        Assert.Equal("""
                     holder => Enumerable
                         .Range(0, 1)
                         .Select(number => number)
                         .Sum() + holder.Value
                     """, composed.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal(7, composed.Invoke(new NumberHolder { Value = 7 }));
    }

    [Fact]
    public void CompositionCycleFailsDeterministically()
    {
        var cyclic = new CyclicSpecification();

        var exception = Assert.Throws<InvalidOperationException>(cyclic.GetExpandedExpression);

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsInvocation(Expression expression)
    {
        var visitor = new InvocationVisitor();
        visitor.Visit(expression);
        return visitor.FoundInvocation;
    }

    private sealed class InvocationVisitor : ExpressionVisitor
    {
        public bool FoundInvocation { get; private set; }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            FoundInvocation = true;
            return base.VisitInvocation(node);
        }
    }

    private sealed class MinimumPriceSpecification(decimal minimum) : Specification<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price >= minimum;
    }

    private sealed class CyclicSpecification : Specification<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => Invoke(product);
    }

    private sealed class CountingSpecification : Specification<Product>
    {
        public int ExpressionRequests { get; private set; }

        public override Expression<Func<Product, bool>> GetExpression()
        {
            ExpressionRequests++;
            return product => product.Price > 0m;
        }
    }

    private static class StaticSpecifications
    {
        public static readonly Specification<Product> Minimum = new MinimumPriceSpecification(10m);
    }

    private sealed class SpecificationHolder(Specification<Product> minimum)
    {
        public readonly NestedSpecificationHolder Nested = new(minimum);
    }

    private sealed class NestedSpecificationHolder(Specification<Product> minimum)
    {
        public readonly Specification<Product> Minimum = minimum;
    }
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public Category? Category { get; set; }
}

public sealed class Category
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsExpensive { get; set; }
    public CategoryDto? Category { get; set; }
}

public sealed class CategoryDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ProductGroup
{
    public List<Product> Products { get; } = [];
}

public sealed class GroupDto
{
    public bool HasPositive { get; set; }
    public string[] Names { get; set; } = [];
}

public sealed class NumberHolder
{
    public int Value { get; set; }
}

public sealed class NullableCustomerHolder
{
    public Customer? Customer { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = new();
    public List<OrderLine> Lines { get; } = [];
}

public sealed class Customer
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class OrderLine
{
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
