namespace Raffinert.Expressions.QuerySyntax.UnitTests;

public sealed class QuerySyntaxTests
{
    private static readonly Product[] Products =
    [
        new() { Name = "Desk", Category = "Office", Price = 20, Tags = ["large", "wood"] },
        new() { Name = "Pen", Category = "Office", Price = 2, Tags = ["small"] },
        new() { Name = "Apple", Category = "Food", Price = 3, Tags = ["fresh"] }
    ];

    private static readonly Category[] Categories =
    [
        new() { Name = "Office", Label = "Work" },
        new() { Name = "Food", Label = "Meal" }
    ];

    [Fact]
    public void QuerySyntaxExpandsWhereOrderingAndSelectWithoutReplacingProvider()
    {
        var source = Products.AsQueryable();
        var minimumPrice = Condition<Product>.Create(product => product.Price >= 3);
        var price = Projection<Product>.Create(product => product.Price);
        var name = Projection<Product>.Create(product => product.Name);

        var query =
            from product in source.AsRaffinertQuery()
            where minimumPrice.Invoke(product)
            orderby price.Invoke(product) descending, name.Invoke(product)
            select name.Invoke(product);

        Assert.Same(query.UnderlyingQuery.Provider, query.Provider);
        Assert.DoesNotContain("Invoke", query.Expression.ToString());
        Assert.Equal(["Desk", "Apple"], (IEnumerable<string>)query);
    }

    [Fact]
    public void QuerySyntaxExpandsLetMultipleFromAndContinuation()
    {
        var source = Products.AsQueryable();
        var name = Projection<Product>.Create(product => product.Name);
        var tags = Projection<Product>.Create(product => product.Tags.AsEnumerable());

        var query =
            from product in source.AsRaffinertQuery()
            let productName = name.Invoke(product)
            from tag in tags.Invoke(product)
            group productName by tag into taggedProducts
            orderby taggedProducts.Key
            select new { Tag = taggedProducts.Key, Count = taggedProducts.Count() };

        Assert.Same(query.UnderlyingQuery.Provider, query.Provider);
        Assert.DoesNotContain("Invoke", query.Expression.ToString());
        Assert.Equal(
            ["fresh", "large", "small", "wood"],
            (IEnumerable<string>)query.Select(row => row.Tag));
        Assert.All(Enumerable.ToArray(query), row => Assert.Equal(1, row.Count));
    }

    [Fact]
    public void QuerySyntaxExpandsJoinAndGroupJoinSelectors()
    {
        var source = Products.AsQueryable();
        var categories = Categories.AsQueryable();
        var productCategory = Projection<Product>.Create(product => product.Category);
        var categoryName = Projection<Category>.Create(category => category.Name);
        var categoryLabel = Projection<Category>.Create(category => category.Label);

        var joined =
            from product in source.AsRaffinertQuery()
            join category in categories
                on productCategory.Invoke(product) equals categoryName.Invoke(category)
            orderby product.Name
            select categoryLabel.Invoke(category) + ":" + product.Name;

        var groupJoined =
            from category in categories.AsRaffinertQuery()
            join product in source
                on categoryName.Invoke(category) equals productCategory.Invoke(product) into products
            orderby category.Name
            select new { category.Name, Count = products.Count() };

        Assert.Same(joined.UnderlyingQuery.Provider, joined.Provider);
        Assert.DoesNotContain("Invoke", joined.Expression.ToString());
        Assert.Equal(["Meal:Apple", "Work:Desk", "Work:Pen"], (IEnumerable<string>)joined);
        Assert.DoesNotContain("Invoke", groupJoined.Expression.ToString());
        Assert.Equal([1, 2], (IEnumerable<int>)groupJoined.Select(row => row.Count));
    }

    [Fact]
    public void ExplicitRangeTypeUsesFacadeCast()
    {
        IQueryable<object> source = Products.Cast<object>().AsQueryable();
        var name = Projection<Product>.Create(product => product.Name);

        var query =
            from Product product in source.AsRaffinertQuery()
            orderby product.Name
            select name.Invoke(product);

        Assert.DoesNotContain("Invoke", query.Expression.ToString());
        Assert.Equal(["Apple", "Desk", "Pen"], (IEnumerable<string>)query);
    }

    [Fact]
    public void AsyncEnumerationFailsDescriptivelyWhenUnderlyingQueryIsSynchronous()
    {
        var query = Products.AsQueryable().AsRaffinertQuery();

        var exception = Assert.Throws<InvalidOperationException>(() => query.GetAsyncEnumerator());

        Assert.Contains("does not support asynchronous enumeration", exception.Message);
    }

    private sealed class Product
    {
        public required string Name { get; init; }
        public required string Category { get; init; }
        public required int Price { get; init; }
        public string[] Tags { get; init; } = [];
    }

    private sealed class Category
    {
        public required string Name { get; init; }
        public required string Label { get; init; }
    }
}
