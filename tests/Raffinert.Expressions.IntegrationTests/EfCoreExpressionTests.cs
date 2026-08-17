using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.IntegrationTests;

public sealed class EfCoreExpressionTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _db;

    public EfCoreExpressionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;
        _db = new TestDbContext(options);
    }

    [Fact]
    public async Task NestedSpecificationAndProjectionTranslate()
    {
        var expensive = Specification<DbProduct>.Create(product => product.PriceCents > 1000);
        var named = Specification<DbProduct>.Create(product => expensive.Invoke(product) && product.Name != "Hidden");
        var category = Projection<DbCategory, CategoryRow>.Create(value => new CategoryRow { Name = value.Name });
        var product = Projection<DbProduct, ProductRow>.Create(value => new ProductRow
        {
            Id = value.Id,
            Name = value.Name,
            Category = category.InvokeOrDefault(value.Category)
        });

        var query = _db.Products
            .Where(named)
            .Select(product)
            .OrderBy(row => row.Name);

        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Equal("""
                     SELECT "p"."Id", "p"."Name", "c"."Id" IS NULL, "c"."Name"
                     FROM "Products" AS "p"
                     LEFT JOIN "Categories" AS "c" ON "p"."CategoryId" = "c"."Id"
                     WHERE "p"."PriceCents" > 1000 AND "p"."Name" <> 'Hidden'
                     ORDER BY "p"."Name"
                     """, sql);
        Assert.Equal(["Desk", "Uncategorized"], rows.Select(row => row.Name));
        Assert.Equal("Office", rows[0].Category!.Name);
        Assert.Null(rows[1].Category);
    }

    [Fact]
    public async Task SpecificationInsideProjectionAndProjectionInsideSpecificationTranslate()
    {
        var price = Projection<DbProduct, int>.Create(product => product.PriceCents);
        var expensive = Specification<DbProduct>.Create(product => price.Invoke(product) > 1000);
        var row = Projection<DbProduct, ProductRow>.Create(product => new ProductRow
        {
            Id = product.Id,
            Name = product.Name,
            IsExpensive = expensive.Invoke(product)
        });

        var query = _db.Products.Where(expensive).Select(row).OrderBy(value => value.Id);
        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Equal("""
                     SELECT "p"."Id", "p"."Name", "p"."PriceCents" > 1000 AS "IsExpensive"
                     FROM "Products" AS "p"
                     WHERE "p"."PriceCents" > 1000
                     ORDER BY "p"."Id"
                     """, sql);

        Assert.All(rows, value => Assert.True(value.IsExpensive));
        Assert.Equal(3, rows.Length);
    }

    [Fact]
    public async Task ThenAndDeepMixedCompositionTranslate()
    {
        var price = Projection<DbProduct, int>.Create(product => product.PriceCents);
        var doubled = Projection<int, int>.Create(value => value * 2);
        var threshold = Specification<int>.Create(value => value >= 3000);
        var composedScalar = price.Then(doubled);
        var composedSpecification = composedScalar.Then(threshold);
        var mixed = Projection<DbProduct, bool>.Create(product => composedSpecification.Invoke(product));

        var values = await _db.Products
            .Where(composedSpecification)
            .Select(mixed)
            .ToArrayAsync();

        Assert.Equal(3, values.Length);
        Assert.All(values, Assert.True);
    }

    [Fact]
    public async Task ThenAndDeepMixedCompositionTranslateProjectionAsConditionAndSpecificationAsProjection()
    {
        var price = Projection<DbProduct, int>.Create(product => product.PriceCents);
        var doubled = Projection<int, int>.Create(value => value * 2);
        var threshold = Specification<int>.Create(value => value >= 3000);
        var composedScalar = price.Then(doubled);
        var composedSpecification = composedScalar.Then(threshold);
        var mixed = Projection<DbProduct, bool>.Create(product => composedSpecification.Invoke(product));

        var values = await _db.Products
            .Where(mixed)
            .Select(composedSpecification)
            .ToArrayAsync();

        Assert.Equal(3, values.Length);
        Assert.All(values, Assert.True);
    }

    [Fact]
    public async Task MergedProjectionTranslatesWithOverlaySemantics()
    {
        var basis = Projection<DbProduct, ProductRow>.Create(product => new ProductRow
        {
            Id = product.Id,
            Name = product.Name
        });
        var overlay = Projection<DbProduct, ProductRow>.Create(product => new ProductRow
        {
            Name = product.Name + "!",
            IsExpensive = product.PriceCents > 1000
        });

        var query = _db.Products.Select(basis.MergeBindings(overlay)).OrderBy(row => row.Name);
        var rows = await query.ToArrayAsync();

        Assert.False(rows.Single(row => row.Name == "Pencil!").IsExpensive);
        Assert.True(rows.Single(row => row.Name == "Desk!").IsExpensive);
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await _db.Database.EnsureCreatedAsync();
        Seed();
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void Seed()
    {
        var office = new DbCategory { Name = "Office" };
        _db.Products.AddRange(
            new DbProduct { Name = "Pencil", PriceCents = 200, Category = office },
            new DbProduct { Name = "Desk", PriceCents = 20000, Category = office },
            new DbProduct { Name = "Uncategorized", PriceCents = 1500 },
            new DbProduct { Name = "Hidden", PriceCents = 9000 });
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<DbProduct> Products => Set<DbProduct>();
    public DbSet<DbCategory> Categories => Set<DbCategory>();
}

public sealed class DbProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int? CategoryId { get; set; }
    public DbCategory? Category { get; set; }
}

public sealed class DbCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<DbProduct> Products { get; } = [];
}

public sealed class ProductRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsExpensive { get; set; }
    public CategoryRow? Category { get; set; }
}

public sealed class CategoryRow
{
    public string Name { get; set; } = string.Empty;
}
