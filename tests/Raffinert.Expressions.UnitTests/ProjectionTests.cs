using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ProjectionTests
{
    [Fact]
    public void InlineSubclassEnumerableAndQueryableProjectionWork()
    {
        var products = new[] { new Product { Name = "Book", Price = 12m } };
        Proj<Product, string> inline = Proj<Product, string>.Create(product => product.Name);
        Proj<Product, decimal> subclass = new PriceProjection();

        Assert.Equal("Book", inline.Invoke(products[0]));
        Assert.Equal(12m, subclass.Invoke(products[0]));
        Assert.Equal("Book", products.Select(inline).Single());
        Assert.Equal("Book", products.AsQueryable().Select(inline).Single());
    }

    [Fact]
    public void NullSafeNestedProjectionUsesConditionalAndDefault()
    {
        var category = Proj<Category, CategoryDto>.Create(value => new CategoryDto { Name = value.Name });
        var product = Proj<Product, ProductDto>.Create(value => new ProductDto
        {
            Name = value.Name,
            Category = category.InvokeOrDefault(value.Category)
        });

        var expression = product.GetExpandedExpression();

        Assert.Contains(FindNodes<ConditionalExpression>(expression), node => node.IfTrue is DefaultExpression);
        Assert.Null(product.Invoke(new Product()).Category);
        Assert.Equal("Tools", product.Invoke(new Product { Category = new Category { Name = "Tools" } }).Category!.Name);
    }

    [Fact]
    public void NullableValueInputMapsSafely()
    {
        var twice = Proj<int?, int>.Create(value => value!.Value * 2);
        var wrapper = Proj<NullableHolder, int>.Create(value => twice.InvokeOrDefault(value.Value));

        Assert.Equal(0, wrapper.Invoke(new NullableHolder()));
        Assert.Equal(8, wrapper.Invoke(new NullableHolder { Value = 4 }));
    }

    [Fact]
    public void MergeBindingsHonorsAllConflictPolicies()
    {
        var first = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Id = product.Id,
            Name = "first"
        });
        var second = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Name = "second",
            IsExpensive = product.Price > 10m
        });
        var product = new Product { Id = 3, Price = 20m };

        var useLast = first.MergeBindings(second).Invoke(product);
        var useFirst = first.MergeBindings(second, BindingConflictBehavior.UseFirst).Invoke(product);

        Assert.Equal(3, useLast.Id);
        Assert.Equal("second", useLast.Name);
        Assert.True(useLast.IsExpensive);
        Assert.Equal("first", useFirst.Name);
        Assert.Throws<InvalidOperationException>(() =>
            first.MergeBindings(second, BindingConflictBehavior.Throw));
    }

    [Fact]
    public void ConditionalMergeMatchesBranchBindingsByMemberNotPosition()
    {
        var conditional = Proj<ConditionalSource, MergeDto>.Create(source => source.Flag
            ? new MergeDto { A = source.A, B = source.B }
            : new MergeDto { B = source.B + 1, A = source.A + 1 });
        var overlay = Proj<ConditionalSource, MergeDto>.Create(source => new MergeDto { C = 9 });

        var trueResult = conditional.MergeBindings(overlay).Invoke(new ConditionalSource { Flag = true, A = 2, B = 3 });
        var falseResult = conditional.MergeBindings(overlay).Invoke(new ConditionalSource { Flag = false, A = 2, B = 3 });

        Assert.Equal((2, 3, 9), (trueResult.A, trueResult.B, trueResult.C));
        Assert.Equal((3, 4, 9), (falseResult.A, falseResult.B, falseResult.C));
    }

    [Fact]
    public void MapToExistingUpdatesSimpleAndPresentNestedObjects()
    {
        var nested = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Id = product.Id,
            Category = product.Category == null
                ? null
                : new CategoryDto { Name = product.Category.Name }
        });
        ProductDto? destination = new()
        {
            Id = 1,
            Category = new CategoryDto { Name = "Old" }
        };

        nested.MapToExisting(
            new Product { Id = 2, Category = new Category { Name = "New" } },
            ref destination);

        Assert.Equal(2, destination!.Id);
        Assert.Equal("New", destination.Category!.Name);
    }

    [Fact]
    public void MapToExistingCreatesRootButRejectsMissingNestedDestination()
    {
        var nestedCategory = Proj<Category, CategoryDto>.Create(category => new CategoryDto { Name = category.Name });
        var projection = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Category = nestedCategory.Invoke(product.Category!)
        });
        ProductDto? absentRoot = null;
        projection.MapToExisting(
            new Product { Category = new Category { Name = "New" } },
            ref absentRoot);
        Assert.Equal("New", absentRoot!.Category!.Name);

        ProductDto? absentNested = new();
        var exception = Assert.Throws<InvalidOperationException>(() => projection.MapToExisting(
            new Product { Category = new Category { Name = "New" } },
            ref absentNested));
        Assert.Contains("current value is null", exception.Message);
    }

    [Fact]
    public void MapToExistingConditionalNullClearsNestedDestination()
    {
        var projection = Proj<Product, ProductDto>.Create(product => new ProductDto
        {
            Category = product.Category == null
                ? null
                : new CategoryDto { Name = product.Category.Name }
        });
        ProductDto? destination = new() { Category = new CategoryDto { Name = "Old" } };

        projection.MapToExisting(new Product(), ref destination);

        Assert.Null(destination!.Category);
    }

    [Fact]
    public void UnsupportedMapToExistingShapeIsDescriptive()
    {
        var projection = Proj<Product, string>.Create(product => product.Name);

        var exception = Assert.Throws<NotSupportedException>(projection.GetMapToExistingExpression);

        Assert.Contains("member initializer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static List<TNode> FindNodes<TNode>(Expression expression) where TNode : Expression
    {
        var visitor = new NodeVisitor<TNode>();
        visitor.Visit(expression);
        return visitor.Nodes;
    }

    private sealed class NodeVisitor<TNode> : ExpressionVisitor where TNode : Expression
    {
        public List<TNode> Nodes { get; } = [];

        public override Expression? Visit(Expression? node)
        {
            if (node is TNode typed) Nodes.Add(typed);
            return base.Visit(node);
        }
    }

    private sealed class PriceProjection : Proj<Product, decimal>
    {
        public override Expression<Func<Product, decimal>> GetExpression() => product => product.Price;
    }
}

public sealed class NullableHolder
{
    public int? Value { get; set; }
}

public sealed class ConditionalSource
{
    public bool Flag { get; set; }
    public int A { get; set; }
    public int B { get; set; }
}

public sealed class MergeDto
{
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
}
