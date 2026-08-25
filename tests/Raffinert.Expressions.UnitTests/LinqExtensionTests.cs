namespace Raffinert.Expressions.UnitTests;

public sealed class LinqExtensionTests
{
    private static readonly Product[] Products =
    [
        new() { Name = "Desk", Category = "Office", Price = 20, Tags = ["furniture"] },
        new() { Name = "Pen", Category = "Office", Price = 2, Tags = ["writing"] },
        new() { Name = "Apple", Category = "Food", Price = 2, Tags = ["fruit"] }
    ];

    [Fact]
    public void EnumerableConditionConsumersUseComposableConditions()
    {
        IEnumerable<Product> source = Products;
        var costsTwo = Condition<Product>.Create(product => product.Price == 2);
        var expensive = Condition<Product>.Create(product => product.Price > 10);
        var affordable = Condition<Product>.Create(product => product.Price < 25);
        var missing = Condition<Product>.Create(product => product.Price < 0);
        var ordered = source.OrderBy(product => product.Price).ThenBy(product => product.Name);

        Assert.True(source.Any(expensive));
        Assert.True(source.All(affordable));
        Assert.Equal(2, source.Count(costsTwo));
        Assert.Equal(2L, source.LongCount(costsTwo));
        Assert.Equal("Desk", source.First(expensive).Name);
        Assert.Null(source.FirstOrDefault(missing));
        Assert.Equal("Apple", source.Last(costsTwo).Name);
        Assert.Null(source.LastOrDefault(missing));
        Assert.Equal("Desk", source.Single(expensive).Name);
        Assert.Null(source.SingleOrDefault(missing));
        Assert.Equal(["Desk"], ordered.SkipWhile(costsTwo).Select(product => product.Name));
        Assert.Equal(["Apple", "Pen"], ordered.TakeWhile(costsTwo).Select(product => product.Name));
    }

    [Fact]
    public void QueryableConditionConsumersUseComposableConditions()
    {
        IQueryable<Product> source = Products.AsQueryable();
        var costsTwo = Condition<Product>.Create(product => product.Price == 2);
        var expensive = Condition<Product>.Create(product => product.Price > 10);
        var affordable = Condition<Product>.Create(product => product.Price < 25);
        var missing = Condition<Product>.Create(product => product.Price < 0);
        var ordered = source.OrderBy(product => product.Price).ThenBy(product => product.Name);

        Assert.True(source.Any(expensive));
        Assert.True(source.All(affordable));
        Assert.Equal(2, source.Count(costsTwo));
        Assert.Equal(2L, source.LongCount(costsTwo));
        Assert.Equal("Desk", source.First(expensive).Name);
        Assert.Null(source.FirstOrDefault(missing));
        Assert.Equal("Apple", source.Last(costsTwo).Name);
        Assert.Null(source.LastOrDefault(missing));
        Assert.Equal("Desk", source.Single(expensive).Name);
        Assert.Null(source.SingleOrDefault(missing));
        Assert.Equal(["Desk"], ordered.SkipWhile(costsTwo).Select(product => product.Name));
        Assert.Equal(["Apple", "Pen"], ordered.TakeWhile(costsTwo).Select(product => product.Name));
    }

    [Fact]
    public void EnumerableProjectionConsumersUseComposableSelectors()
    {
        IEnumerable<Product> source = Products;
        var name = Projection<Product>.Create(product => product.Name);
        var price = Projection<Product>.Create(product => product.Price);
        var category = Projection<Product>.Create(product => product.Category);
        var tags = Projection<Product>.Create(product => product.Tags);
        var tagArray = Projection<Product>.Create(product => product.Tags.ToArray());

        Assert.Equal(["Desk", "Apple", "Pen"], source.OrderByDescending(price).ThenBy(name).Select(name));
        Assert.Equal(["Desk", "Pen", "Apple"], source.OrderByDescending(price).ThenByDescending(name).Select(name));
        Assert.Equal(["Apple", "Pen", "Desk"], source.OrderBy(price).ThenBy(name).Select(name));
        Assert.Equal(["Pen", "Apple", "Desk"], source.OrderBy(price).ThenByDescending(name).Select(name));
        Assert.Equal(["Food", "Office"], source.GroupBy(category).Select(group => group.Key).OrderBy(value => value));
        Assert.Equal(3, source.GroupBy(category, name).SelectMany(group => group).Count());
        Assert.Equal(["furniture", "writing", "fruit"], source.SelectMany(tags));
        Assert.Equal(["furniture", "writing", "fruit"], source.SelectMany(tagArray));
    }

    [Fact]
    public void QueryableProjectionConsumersUseComposableSelectors()
    {
        IQueryable<Product> source = Products.AsQueryable();
        var name = Projection<Product>.Create(product => product.Name);
        var price = Projection<Product>.Create(product => product.Price);
        var category = Projection<Product>.Create(product => product.Category);
        var tags = Projection<Product>.Create(product => product.Tags);
        var tagArray = Projection<Product>.Create(product => product.Tags.ToArray());

        Assert.Equal(["Desk", "Apple", "Pen"], source.OrderByDescending(price).ThenBy(name).Select(name));
        Assert.Equal(["Desk", "Pen", "Apple"], source.OrderByDescending(price).ThenByDescending(name).Select(name));
        Assert.Equal(["Apple", "Pen", "Desk"], source.OrderBy(price).ThenBy(name).Select(name));
        Assert.Equal(["Pen", "Apple", "Desk"], source.OrderBy(price).ThenByDescending(name).Select(name));
        Assert.Equal(["Food", "Office"], source.GroupBy(category).Select(group => group.Key).OrderBy(value => value));
        Assert.Equal(3, source.GroupBy(category, name).SelectMany(group => group).Count());
        Assert.Equal(["furniture", "writing", "fruit"], source.SelectMany(tags));
        Assert.Equal(["furniture", "writing", "fruit"], source.SelectMany(tagArray));
    }

    private sealed class Product
    {
        public required string Name { get; init; }
        public required string Category { get; init; }
        public required int Price { get; init; }
        public List<string> Tags { get; init; } = [];
    }
}
