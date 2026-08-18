using AgileObjects.ReadableExpressions;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ProjectionTests
{
    [Fact]
    public void InlineSubclassEnumerableAndQueryableProjectionWork()
    {
        var products = new[] { new Product { Name = "Book", Price = 12m } };
        Projection<Product, string> inline = Projection<Product, string>.Create(product => product.Name);
        Projection<Product, decimal> subclass = new PriceProjection();

        Assert.Equal("Book", inline.Invoke(products[0]));
        Assert.Equal(12m, subclass.Invoke(products[0]));
        Assert.Equal("Book", products.Select(inline).Single());
        Assert.Equal("Book", products.AsQueryable().Select(inline).Single());
    }

    [Fact]
    public void NullSafeNestedProjectionUsesConditionalAndDefault()
    {
        var category = Projection<Category, CategoryDto>.Create(value => new CategoryDto { Name = value.Name });
        var product = Projection<Product, ProductDto>.Create(value => new ProductDto
        {
            Name = value.Name,
            Category = category.InvokeOrDefault(value.Category)
        });

        var expression = product.GetExpandedExpression();

        Assert.Equal("""
                     value => new ProductDto
                     {
                         Name = value.Name,
                         Category = (value.Category == null) ? null : new CategoryDto
                         {
                             Name = value.Category.Name
                         }
                     }
                     """, expression.ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Null(product.Invoke(new Product()).Category);
        Assert.Equal("Tools", product.Invoke(new Product { Category = new Category { Name = "Tools" } }).Category!.Name);
    }

    [Fact]
    public void NullableValueInputMapsSafely()
    {
        var twice = Projection<int?, int>.Create(value => value!.Value * 2);
        var wrapper = Projection<NullableHolder, int>.Create(value => twice.InvokeOrDefault(value.Value));

        Assert.Equal(
            "value => (value.Value == null) ? default(int) : value.Value.Value * 2",
            wrapper.GetExpandedExpression().ToReadableString());
        Assert.Equal(0, wrapper.Invoke(new NullableHolder()));
        Assert.Equal(8, wrapper.Invoke(new NullableHolder { Value = 4 }));
    }

    [Fact]
    public void MergeBindingsHonorsAllConflictPolicies()
    {
        var first = Projection<Product, ProductDto>.Create(product => new ProductDto
        {
            Id = product.Id,
            Name = "first"
        });
        var second = Projection<Product, ProductDto>.Create(product => new ProductDto
        {
            Name = "second",
            IsExpensive = product.Price > 10m
        });
        var product = new Product { Id = 3, Price = 20m };

        var useLastProjection = first.MergeBindings(second);
        var useFirstProjection = first.MergeBindings(second, BindingConflictBehavior.UseFirst);
        var useLast = useLastProjection.Invoke(product);
        var useFirst = useFirstProjection.Invoke(product);

        Assert.Equal("""
                     product => new ProductDto
                     {
                         Id = product.Id,
                         Name = "second",
                         IsExpensive = product.Price > 10m
                     }
                     """, useLastProjection.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("""
                     product => new ProductDto
                     {
                         Id = product.Id,
                         Name = "first",
                         IsExpensive = product.Price > 10m
                     }
                     """, useFirstProjection.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
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
        var conditional = Projection<ConditionalSource, MergeDto>.Create(source => source.Flag
            ? new MergeDto { A = source.A, B = source.B }
            : new MergeDto { B = source.B + 1, A = source.A + 1 });
        var overlay = Projection<ConditionalSource, MergeDto>.Create(source => new MergeDto { C = 9 });

        var merged = conditional.MergeBindings(overlay);
        var trueResult = merged.Invoke(new ConditionalSource { Flag = true, A = 2, B = 3 });
        var falseResult = merged.Invoke(new ConditionalSource { Flag = false, A = 2, B = 3 });

        Assert.Equal("""
                     source => new MergeDto
                     {
                         A = source.Flag ? source.A : source.A + 1,
                         B = source.Flag ? source.B : source.B + 1,
                         C = 9
                     }
                     """, merged.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal((2, 3, 9), (trueResult.A, trueResult.B, trueResult.C));
        Assert.Equal((3, 4, 9), (falseResult.A, falseResult.B, falseResult.C));
    }

    [Fact]
    public void MapToExistingUpdatesSimpleAndPresentNestedObjects()
    {
        var nested = Projection<Product, ProductDto>.Create(product => new ProductDto
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
        var originalCategory = destination.Category;

        nested.MapToExisting(
            new Product { Id = 2, Category = new Category { Name = "New" } },
            ref destination);

        Assert.Equal("""
                     (product, existing) =>
                     {
                         existing.Id = product.Id;

                         if (product.Category == null)
                         {
                             existing.Category = null;
                         }
                         else if (existing.Category == null)
                         {
                             existing.Category = new CategoryDto
                             {
                                 Name = product.Category.Name
                             };
                         }
                         else
                         {
                             existing.Category.Name = product.Category.Name;
                         }
                     }
                     """, nested.GetMapToExistingExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal(2, destination!.Id);
        Assert.Same(originalCategory, destination.Category);
        Assert.Equal("New", destination.Category!.Name);
    }

    [Fact]
    public void MapToExistingCreatesRootAndMissingNestedDestination()
    {
        var nestedCategory = Projection<Category, CategoryDto>.Create(category => new CategoryDto { Name = category.Name });
        var projection = Projection<Product, ProductDto>.Create(product => new ProductDto
        {
            Category = nestedCategory.Invoke(product.Category!)
        });
        ProductDto? absentRoot = null;
        projection.MapToExisting(
            new Product { Category = new Category { Name = "New" } },
            ref absentRoot);
        Assert.Equal("New", absentRoot!.Category!.Name);

        ProductDto? absentNested = new();
        projection.MapToExisting(
            new Product { Category = new Category { Name = "New" } },
            ref absentNested);
        Assert.Equal("New", absentNested!.Category!.Name);
    }

    [Fact]
    public void MapToExistingConditionalNullClearsNestedDestination()
    {
        var projection = Projection<Product, ProductDto>.Create(product => new ProductDto
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
    public void MapToExistingPreservesAndRefillsMutableCollections()
    {
        var projection = Projection<CollectionSource, CollectionDestination>.Create(source => new CollectionDestination
        {
            Values = source.Values == null ? null : source.Values.Select(value => value * 10).ToList(),
            SetValues = source.Values == null ? null : source.Values.ToList(),
            EnumerableValues = source.Values == null ? null : source.Values.Select(value => value + 1).ToList(),
            ArrayValues = source.Values == null ? null : source.Values.Select(value => value + 2).ToArray(),
            ReadOnlyValues = { source.Values![0], source.Values[1] }
        });
        CollectionDestination? destination = new()
        {
            Values = [99],
            SetValues = new HashSet<int> { 99 },
            EnumerableValues = new List<int> { 99 },
            ArrayValues = [99],
            ReadOnlyValues = { 99 }
        };
        var originalValues = destination.Values;
        var originalSet = destination.SetValues;
        var originalEnumerable = destination.EnumerableValues;
        var originalArray = destination.ArrayValues;
        var originalReadOnly = destination.ReadOnlyValues;

        projection.MapToExisting(new CollectionSource { Values = [2, 3] }, ref destination);

        Assert.Same(originalValues, destination!.Values);
        Assert.Equal([20, 30], destination.Values);
        Assert.Same(originalSet, destination.SetValues);
        Assert.Equal([2, 3], destination.SetValues!.OrderBy(value => value));
        Assert.NotSame(originalEnumerable, destination.EnumerableValues);
        Assert.Equal([3, 4], destination.EnumerableValues);
        Assert.NotSame(originalArray, destination.ArrayValues);
        Assert.Equal([4, 5], destination.ArrayValues!);
        Assert.Same(originalReadOnly, destination.ReadOnlyValues);
        Assert.Equal([2, 3], destination.ReadOnlyValues);
    }

    [Fact]
    public void MapToExistingClearsMutableCollectionForNullAndCreatesMissingCollection()
    {
        var projection = Projection<CollectionSource, CollectionDestination>.Create(source => new CollectionDestination
        {
            Values = source.Values == null ? null : source.Values.ToList()
        });
        CollectionDestination? destination = new() { Values = [99] };
        var originalValues = destination.Values;

        projection.MapToExisting(new CollectionSource(), ref destination);

        Assert.Same(originalValues, destination!.Values);
        Assert.Empty(destination.Values!);

        destination!.Values = null;
        projection.MapToExisting(new CollectionSource { Values = [4, 5] }, ref destination);

        Assert.Equal([4, 5], destination!.Values);
    }

    [Fact]
    public void MapToExistingHandlesCollectionAliasedBySourceAndDestination()
    {
        var projection = Projection<CollectionSource, CollectionDestination>.Create(source => new CollectionDestination
        {
            Values = source.Values
        });
        var shared = new List<int> { 2, 3 };
        CollectionDestination? destination = new() { Values = shared };

        projection.MapToExisting(new CollectionSource { Values = shared }, ref destination);

        Assert.Same(shared, destination!.Values);
        Assert.Equal([2, 3], destination.Values);
    }

    [Fact]
    public void UnsupportedMapToExistingShapeIsDescriptive()
    {
        var projection = Projection<Product, string>.Create(product => product.Name);

        var exception = Assert.Throws<NotSupportedException>(projection.GetMapToExistingExpression);

        Assert.Contains("member initializer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PriceProjection : Projection<Product, decimal>
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

public sealed class CollectionSource
{
    public List<int>? Values { get; set; }
}

public sealed class CollectionDestination
{
    public List<int>? Values { get; set; } = [];
    public ICollection<int>? SetValues { get; set; } = new HashSet<int>();
    public IEnumerable<int>? EnumerableValues { get; set; } = [];
    public int[]? ArrayValues { get; set; } = [];
    public List<int> ReadOnlyValues { get; } = [];
}
