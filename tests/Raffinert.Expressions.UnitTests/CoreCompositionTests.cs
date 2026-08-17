using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class CoreCompositionTests
{
    [Fact]
    public void SpecInsideProjectionIsInlinedAndExecutes()
    {
        var expensive = Spec<Product>.Create(product => product.Price > 100m);
        var projection = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Name = product.Name,
            IsExpensive = expensive.Invoke(product)
        });

        var expanded = projection.GetExpandedExpression();

        Assert.False(ContainsMarker(expanded));
        Assert.False(ContainsInvocation(expanded));
        Assert.True(projection.Invoke(new Product { Name = "Desk", Price = 200m }).IsExpensive);
    }

    [Fact]
    public void ProjectionInsideSpecIsInlinedAndExecutes()
    {
        var total = Proj<Order, decimal>.Create(order =>
            order.Lines.Sum(line => line.Price * line.Quantity));
        var expensive = Spec<Order>.Create(order => total.Invoke(order) > 1000m);

        var expanded = expensive.GetExpandedExpression();

        Assert.False(ContainsMarker(expanded));
        Assert.True(expensive.Invoke(new Order
        {
            Lines = { new OrderLine { Price = 600m, Quantity = 2 } }
        }));
    }

    [Fact]
    public void DeepMixedCompositionExpandsRecursively()
    {
        var price = Proj<Product, decimal>.Create(product => product.Price);
        var rounded = Proj<Product, decimal>.Create(product => decimal.Round(price.Invoke(product)));
        var high = Spec<Product>.Create(product => rounded.Invoke(product) >= 10m);
        var result = Proj<Product, bool>.Create(product => high.Invoke(product));

        var expanded = result.GetExpandedExpression();

        Assert.False(ContainsMarker(expanded));
        Assert.True(result.Invoke(new Product { Price = 10.4m }));
    }

    [Fact]
    public void ThenComposesProjectionAndProjectionWithoutInvocationNodes()
    {
        var customer = Proj<Order, Customer>.Create(order => order.Customer);
        var name = Proj<Customer, string>.Create(value => value.Name);

        var composed = customer.Then(name);

        Assert.Equal("Ada", composed.Invoke(new Order { Customer = new Customer { Name = "Ada" } }));
        Assert.False(ContainsMarker(composed.GetExpandedExpression()));
        Assert.False(ContainsInvocation(composed.GetExpandedExpression()));
    }

    [Fact]
    public void ThenComposesProjectionAndSpec()
    {
        var customer = Proj<Order, Customer>.Create(order => order.Customer);
        var active = Spec<Customer>.Create(value => value.IsActive);

        var composed = customer.Then(active);

        Assert.True(composed.Invoke(new Order { Customer = new Customer { IsActive = true } }));
        Assert.False(ContainsMarker(composed.GetExpandedExpression()));
    }

    [Fact]
    public void DirectNewSubclassInvocationIsExpanded()
    {
        var outer = Spec<Product>.Create(product => new MinimumPriceSpec(12m).Invoke(product));

        Assert.True(outer.Invoke(new Product { Price = 15m }));
        Assert.False(ContainsMarker(outer.GetExpandedExpression()));
    }

    [Fact]
    public void ClosureMemberChainsAndReadonlyFieldsAreResolved()
    {
        var holder = new SpecHolder(new MinimumPriceSpec(12m));
        var outer = Spec<Product>.Create(product => holder.Nested.Minimum.Invoke(product));

        Assert.True(outer.Invoke(new Product { Price = 15m }));
        Assert.False(ContainsMarker(outer.GetExpandedExpression()));
    }

    [Fact]
    public void StaticTargetsResolveAndNestedExpandedExpressionsAreCached()
    {
        var counting = new CountingSpec();
        var first = Spec<Product>.Create(product => counting.Invoke(product));
        var second = Spec<Product>.Create(product => counting.Invoke(product) && StaticSpecs.Minimum.Invoke(product));

        first.GetExpandedExpression();
        second.GetExpandedExpression();

        Assert.Equal(1, counting.ExpressionRequests);
        Assert.True(second.Invoke(new Product { Price = 20m }));
    }

    [Fact]
    public void CanonicalInvocationMethodsAreExpanded()
    {
        var positive = Spec<Product>.Create(product => product.Price > 0m);
        var name = Proj<Product, string>.Create(product => product.Name);
        var outer = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            IsExpensive = positive.Invoke(product),
            Name = name.Invoke(product)
        });

        Assert.False(ContainsMarker(outer.GetExpandedExpression()));
        Assert.Equal("Book", outer.Invoke(new Product { Name = "Book", Price = 1m }).Name);
    }

    [Fact]
    public void InvokeOrDefaultIsSharedBySpecificationsAndProjections()
    {
        var active = Spec<Customer>.Create(customer => customer.IsActive);
        var nameLength = Proj<Customer, int>.Create(customer => customer.Name.Length);
        var spec = Spec<NullableCustomerHolder>.Create(holder => active.InvokeOrDefault(holder.Customer));
        var projection = Proj<NullableCustomerHolder, int>.Create(
            holder => nameLength.InvokeOrDefault(holder.Customer));

        Assert.False(spec.Invoke(new NullableCustomerHolder()));
        Assert.Equal(0, projection.Invoke(new NullableCustomerHolder()));
        Assert.True(spec.Invoke(new NullableCustomerHolder { Customer = new Customer { IsActive = true } }));
        Assert.Equal(3, projection.Invoke(new NullableCustomerHolder { Customer = new Customer { Name = "Ada" } }));
        Assert.False(ContainsMarker(spec.GetExpandedExpression()));
        Assert.False(ContainsMarker(projection.GetExpandedExpression()));
    }

    [Fact]
    public void MethodGroupsInAnyAndSelectAreExpanded()
    {
        var positive = Spec<Product>.Create(product => product.Price > 0m);
        var name = Proj<Product, string>.Create(product => product.Name);
        var groupProjection = Proj<ProductGroup, GroupDto>.Create(group => new GroupDto
        {
            HasPositive = group.Products.Any(positive.Invoke),
            Names = group.Products.Select(name.Invoke).ToArray()
        });

        var expanded = groupProjection.GetExpandedExpression();
        var mapped = groupProjection.Invoke(new ProductGroup
        {
            Products = { new Product { Name = "A", Price = 2m } }
        });

        Assert.False(ContainsMarker(expanded));
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
        var unusual = Proj<int, int>.Create(
            Expression.Lambda<Func<int, int>>(Expression.Add(sum, shared), shared));
        var input = Proj<NumberHolder, int>.Create(holder => holder.Value);

        var composed = input.Then(unusual);

        Assert.Equal(7, composed.Invoke(new NumberHolder { Value = 7 }));
    }

    [Fact]
    public void CompositionCycleFailsDeterministically()
    {
        var cyclic = new CyclicSpec();

        var exception = Assert.Throws<InvalidOperationException>(() => cyclic.GetExpandedExpression());

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsMarker(Expression expression)
    {
        var visitor = new MarkerVisitor();
        visitor.Visit(expression);
        return visitor.FoundMarker;
    }

    private static bool ContainsInvocation(Expression expression)
    {
        var visitor = new InvocationVisitor();
        visitor.Visit(expression);
        return visitor.FoundInvocation;
    }

    private sealed class MarkerVisitor : ExpressionVisitor
    {
        public bool FoundMarker { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name is "Invoke" or "InvokeOrDefault" &&
                node.Object != null && IsExprType(node.Object.Type))
            {
                FoundMarker = true;
            }

            return base.VisitMethodCall(node);
        }

        private static bool IsExprType(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Expr<,>)) return true;
            }

            return false;
        }
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

    private sealed class MinimumPriceSpec(decimal minimum) : Spec<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => product.Price >= minimum;
    }

    private sealed class CyclicSpec : Spec<Product>
    {
        public override Expression<Func<Product, bool>> GetExpression() => product => Invoke(product);
    }

    private sealed class CountingSpec : Spec<Product>
    {
        public int ExpressionRequests { get; private set; }

        public override Expression<Func<Product, bool>> GetExpression()
        {
            ExpressionRequests++;
            return product => product.Price > 0m;
        }
    }

    private static class StaticSpecs
    {
        public static readonly Spec<Product> Minimum = new MinimumPriceSpec(10m);
    }

    private sealed class SpecHolder(Spec<Product> minimum)
    {
        public readonly NestedSpecHolder Nested = new(minimum);
    }

    private sealed class NestedSpecHolder(Spec<Product> minimum)
    {
        public readonly Spec<Product> Minimum = minimum;
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
