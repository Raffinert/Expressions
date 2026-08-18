using AgileObjects.ReadableExpressions;

namespace Raffinert.Expressions.UnitTests;

public class StructuralAdaptationTests
{
    [Fact]
    public void ConditionAdaptsSourcePropertiesFieldsAndNestedPaths()
    {
        var condition = Condition<StructuralProduct>.Create(product =>
            product.Price > 10m &&
            product.Category != null &&
            product.Category.Name == "Tools");

        var adapted = condition.AdaptSource<StructuralInventoryItem>();

        Assert.Equal(
            "product => ((product.Price > 10m) && (product.Category != null)) && (product.Category.Name == \"Tools\")",
            adapted.GetExpandedExpression().ToReadableString());
        Assert.True(adapted.Invoke(new StructuralInventoryItem
        {
            Price = 20m,
            Category = new StructuralInventoryCategory { Name = "Tools" }
        }));
        Assert.False(adapted.Invoke(new StructuralInventoryItem
        {
            Price = 20m,
            Category = new StructuralInventoryCategory { Name = "Other" }
        }));
    }

    [Fact]
    public void ProjectionAdaptsSourceAndNestedResultMembers()
    {
        var projection = Projection<StructuralProduct, StructuralProductDto>.Create(product =>
            new StructuralProductDto
            {
                Id = product.Id,
                Name = product.Name,
                IsExpensive = product.Price > 10m,
                Category = product.Category == null
                    ? null
                    : new StructuralCategoryDto { Name = product.Category.Name }
            });

        var adapted = projection.Adapt<StructuralInventoryItem, StructuralInventoryDto>();
        var result = adapted.Invoke(new StructuralInventoryItem
        {
            Id = 7,
            Name = "Desk",
            Price = 20m,
            Category = new StructuralInventoryCategory { Name = "Tools" }
        });

        Assert.Equal("""
                     product => new StructuralInventoryDto
                     {
                         Id = product.Id,
                         Name = product.Name,
                         IsExpensive = product.Price > 10m,
                         Category = (product.Category == null)
                             ? null
                             : new StructuralInventoryCategoryDto
                             {
                                 Name = product.Category.Name
                             }
                     }
                     """, adapted.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal(7, result.Id);
        Assert.Equal("Desk", result.Name);
        Assert.True(result.IsExpensive);
        Assert.Equal("Tools", result.Category!.Name);
    }

    [Fact]
    public void ProjectionCanAdaptOnlyItsSourceOrResult()
    {
        var name = Projection<StructuralProduct, string>.Create(product => product.Name);
        var dto = Projection<StructuralProduct, StructuralProductDto>.Create(product =>
            new StructuralProductDto { Id = product.Id, Name = product.Name });

        var adaptedName = name.AdaptSource<StructuralInventoryItem>();
        var adaptedDto = dto.AdaptResult<StructuralInventoryDto>();
        var result = adaptedDto.Invoke(new StructuralProduct { Id = 3, Name = "Chair" });

        Assert.Equal("product => product.Name", adaptedName.GetExpandedExpression().ToReadableString());
        Assert.Equal("""
                     product => new StructuralInventoryDto
                     {
                         Id = product.Id,
                         Name = product.Name
                     }
                     """, adaptedDto.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("Desk", adaptedName.Invoke(new StructuralInventoryItem { Name = "Desk" }));
        Assert.Equal((3, "Chair"), (result.Id, result.Name));
    }

    [Fact]
    public void SourceAdaptationRejectsMissingIncompatibleAndDirectSourceUsage()
    {
        var condition = Condition<StructuralProduct>.Create(product => product.Price > 10m);
        var identity = Projection<StructuralProduct, StructuralProduct>.Create(product => product);

        var missing = Assert.Throws<InvalidOperationException>(
            condition.AdaptSource<StructuralMissingPriceItem>);
        var incompatible = Assert.Throws<InvalidOperationException>(
            condition.AdaptSource<StructuralWrongPriceItem>);
        var direct = Assert.Throws<NotSupportedException>(
            identity.AdaptSource<StructuralInventoryItem>);

        Assert.Contains("missing", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incompatible", incompatible.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("used directly", direct.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultAdaptationRejectsMissingMembersAndConstructorProjections()
    {
        var memberInit = Projection<StructuralProduct, StructuralProductDto>.Create(product =>
            new StructuralProductDto { Id = product.Id, Name = product.Name });
        var constructor = Projection<StructuralProduct, StructuralConstructorDto>.Create(product =>
            new StructuralConstructorDto(product.Name));

        var missing = Assert.Throws<InvalidOperationException>(
            memberInit.AdaptResult<StructuralMissingNameDto>);
        var unsupported = Assert.Throws<NotSupportedException>(
            constructor.AdaptResult<StructuralInventoryDto>);

        Assert.Contains("missing", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("member initializer", unsupported.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StructuralProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public StructuralCategory? Category { get; set; }
}

public sealed class StructuralCategory
{
    public string Name { get; set; } = string.Empty;
}

public sealed class StructuralInventoryItem
{
    public int Id { get; set; }
    public string Name = string.Empty;
    public decimal Price { get; set; }
    public StructuralInventoryCategory? Category { get; set; }
}

public sealed class StructuralInventoryCategory
{
    public string Name = string.Empty;
}

public sealed class StructuralProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsExpensive { get; set; }
    public StructuralCategoryDto? Category { get; set; }
}

public sealed class StructuralCategoryDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class StructuralInventoryDto
{
    public int Id;
    public string Name { get; set; } = string.Empty;
    public bool IsExpensive { get; set; }
    public StructuralInventoryCategoryDto? Category { get; set; }
}

public sealed class StructuralInventoryCategoryDto
{
    public string Name = string.Empty;
}

public sealed class StructuralMissingPriceItem
{
    public string Name { get; set; } = string.Empty;
}

public sealed class StructuralWrongPriceItem
{
    public string Price { get; set; } = string.Empty;
}

public sealed class StructuralMissingNameDto
{
    public int Id { get; set; }
}

public sealed class StructuralConstructorDto(string name)
{
    public string Name { get; } = name;
}
