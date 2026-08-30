using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

namespace LinqKitComparison;

/// <summary>
/// Demonstrates higher-level Raffinert APIs which LINQKit does not provide. Equivalent behavior would require
/// application-specific expression-tree or object-mapping code rather than another LINQKit call.
/// </summary>
public static class RaffinertSpecificExamples
{
    public static async Task RunAsync(ExampleDbContext db, bool showSql)
    {
        Console.WriteLine();
        Console.WriteLine("=== Raffinert-specific APIs (no direct LINQKit equivalent) ===");

        await StructuralAdaptationAsync(db, showSql);
        await TypedForwardCompositionAsync(db, showSql);
        await MergeProjectionBindingsAsync(db, showSql);
        NullSafeProjection();
        await MapToExistingAsync(db);
    }

    private static async Task StructuralAdaptationAsync(ExampleDbContext db, bool showSql)
    {
        var templateCondition = Condition<ProductTemplate>.Create(product =>
            product.Price >= 300m && product.Description.Contains("phone"));
        var templateProjection = Projection<ProductTemplate, ProductCardTemplate>.Create(product =>
            new ProductCardTemplate
            {
                Id = product.Id,
                Description = product.Description,
                Price = product.Price,
                IsPremium = product.Price >= 800m
            });

        // Raffinert rebinds compatible public members by name, including construction of a different result type.
        // LINQKit expands expression invocation, but has no source/result structural-adaptation API.
        var condition = templateCondition.AdaptSource<Product>();
        var projection = templateProjection.Adapt<Product, ProductCard>();
        var query = db.Products
            .Where(condition)
            .OrderBy(product => product.Description)
            .Select(projection);

        WriteSqlIfRequested("Structural adaptation", query, showSql);
        var rows = await query.ToArrayAsync();

        Console.WriteLine();
        Console.WriteLine("Structural adaptation:");
        Console.WriteLine($"  {Format(rows)}");
    }

    private static async Task TypedForwardCompositionAsync(ExampleDbContext db, bool showSql)
    {
        var price = Projection<Product, decimal>.Create(product => product.Price);
        var withTax = Projection<decimal, decimal>.Create(value => value * 1.25m);
        var premium = Condition<decimal>.Create(value => value >= 1_000m);

        // Then preserves the intermediate types and returns a Condition<Product>. LINQKit can reproduce the
        // final tree with Invoke/Expand, but does not provide a typed projection-to-projection/condition API.
        var premiumAfterTax = price.Then(withTax).Then(premium);
        var query = db.Products
            .Where(premiumAfterTax)
            .OrderBy(product => product.Description)
            .Select(product => product.Description);

        WriteSqlIfRequested("Typed forward composition", query, showSql);
        var descriptions = await query.ToArrayAsync();

        Console.WriteLine();
        Console.WriteLine("Typed forward composition with Then:");
        Console.WriteLine($"  {Format(descriptions)}");
    }

    private static async Task MergeProjectionBindingsAsync(ExampleDbContext db, bool showSql)
    {
        var identity = Projection<Product, ProductCard>.Create(product => new ProductCard
        {
            Id = product.Id,
            Description = product.Description
        });
        var commercial = Projection<Product, ProductCard>.Create(product => new ProductCard
        {
            Price = product.Price,
            IsPremium = product.Price >= 800m
        });

        // MergeBindings combines member initializers and has explicit duplicate-member conflict policies.
        // LINQKit has no projection-binding merge operation.
        var merged = identity.MergeBindings(commercial);
        var query = db.Products
            .OrderBy(product => product.Description)
            .Select(merged);

        WriteSqlIfRequested("Merged projection bindings", query, showSql);
        var rows = await query.ToArrayAsync();

        Console.WriteLine();
        Console.WriteLine("Merged projection bindings:");
        Console.WriteLine($"  {Format(rows)}");
    }

    private static void NullSafeProjection()
    {
        var card = Projection<Product, ProductCard>.Create(product => new ProductCard
        {
            Id = product.Id,
            Description = product.Description,
            Price = product.Price,
            IsPremium = product.Price >= 800m
        });
        var optionalCard = Projection<OptionalProduct, ProductCard?>.Create(value =>
            card.InvokeOrDefault(value.Product));

        // InvokeOrDefault expands to a conditional and returns default when the source is null.
        // A LINQKit expression must spell out that null conditional itself.
        var missing = optionalCard.Invoke(new OptionalProduct());
        var present = optionalCard.Invoke(new OptionalProduct
        {
            Product = new Product { Description = "Portable screen", Price = 850m }
        });

        Console.WriteLine();
        Console.WriteLine("Null-safe nested projection with InvokeOrDefault:");
        Console.WriteLine($"  missing = {missing?.ToString() ?? "<null>"}; present = {present}");
    }

    private static async Task MapToExistingAsync(ExampleDbContext db)
    {
        var map = Projection<Product, ProductCard>.Create(product => new ProductCard
        {
            Id = product.Id,
            Description = product.Description,
            Price = product.Price,
            IsPremium = product.Price >= 800m
        });
        var product = await db.Products
            .AsNoTracking()
            .OrderByDescending(value => value.Price)
            .FirstAsync();
        ProductCard? destination = new()
        {
            Id = -1,
            Description = "Existing instance",
            Price = -1m
        };
        var original = destination;

        // MapToExisting compiles an update action from the member initializer and preserves the root instance.
        // LINQKit is an expression-expansion library and has no object-update/mapping API.
        map.MapToExisting(product, ref destination);

        if (!ReferenceEquals(original, destination))
            throw new InvalidOperationException("MapToExisting unexpectedly replaced the destination instance.");

        Console.WriteLine();
        Console.WriteLine("Map projection to an existing object:");
        Console.WriteLine($"  same instance = {ReferenceEquals(original, destination)}; value = {destination}");
    }

    private static void WriteSqlIfRequested<T>(string title, IQueryable<T> query, bool showSql)
    {
        if (!showSql) return;

        Console.WriteLine();
        Console.WriteLine($"-- Raffinert.Expressions: {title}");
        Console.WriteLine(query.ToQueryString());
    }

    private static string Format<T>(IEnumerable<T> values) => string.Join(", ", values);
}

public sealed class ProductTemplate
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public sealed class ProductCardTemplate
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsPremium { get; set; }
}

public sealed class ProductCard
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsPremium { get; set; }

    public override string ToString() => $"{Description} ({Price:C}, premium: {IsPremium})";
}

public sealed class OptionalProduct
{
    public Product? Product { get; set; }
}
