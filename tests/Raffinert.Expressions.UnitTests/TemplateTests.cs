namespace Raffinert.Expressions.UnitTests;

public class TemplateTests
{
    [Fact]
    public void AnonymousShapeAdaptsToProperties()
    {
        var template = ExpressionTemplate<Product>.Create(
            product => new { product.Name, product.Price },
            shape => shape.Price > 10m && shape.Name != null);

        var adapted = template.AdaptSpecification<InventoryItem>();

        Assert.True(adapted.Invoke(new InventoryItem { Name = "Desk", Price = 20m }));
    }

    [Fact]
    public void ExplicitShapeAdaptsPropertiesToFields()
    {
        var template = SpecificationTemplate<Product>.Create(
            product => new ProductShape { Name = product.Name, Price = product.Price },
            shape => shape.Price > 10m && shape.Name != null);

        var adapted = template.Adapt<FieldInventoryItem>();

        Assert.True(adapted.Invoke(new FieldInventoryItem { Name = "Desk", Price = 20m }));
    }

    [Fact]
    public void MissingAndWrongMembersAreDetected()
    {
        var template = ExpressionTemplate<Product>.Create(
            product => new { product.Name, product.Price },
            shape => shape.Price > 10m);

        var missing = Assert.Throws<InvalidOperationException>(() => template.AdaptSpecification<MissingPriceItem>());
        var wrong = Assert.Throws<InvalidOperationException>(() => template.AdaptSpecification<WrongPriceItem>());

        Assert.Contains("missing", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incompatible", wrong.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedShapeFailsAtCreation()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ExpressionTemplate<Product>.Create(
                product => new { Calculated = product.Price + 1m },
                shape => shape.Calculated > 10m));

        Assert.Contains("directly readable", exception.Message);
    }
}

public sealed class ProductShape
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public sealed class InventoryItem
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public sealed class FieldInventoryItem
{
    public string Name = string.Empty;
    public decimal Price;
}

public sealed class MissingPriceItem
{
    public string Name { get; set; } = string.Empty;
}

public sealed class WrongPriceItem
{
    public string Name { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
}
