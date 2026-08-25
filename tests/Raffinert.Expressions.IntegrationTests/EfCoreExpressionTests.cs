using AgileObjects.ReadableExpressions;
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
    public async Task NestedConditionAndProjectionTranslate()
    {
        var expensive = Condition<DbProduct>.Create(product => product.PriceCents > 1000);
        var named = Condition<DbProduct>.Create(product => expensive.Invoke(product) && product.Name != "Hidden");
        var category = Projection<DbCategory>.Create(value => new CategoryRow { Name = value.Name });
        var product = Projection<DbProduct>.Create(value => new ProductRow
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

        Assert.Equal(
            "product => (product.PriceCents > 1000) && (product.Name != \"Hidden\")",
            named.GetExpandedExpression().ToReadableString());
        Assert.Equal("""
                     value => new ProductRow
                     {
                         Id = value.Id,
                         Name = value.Name,
                         Category = (value.Category == null) ? null : new CategoryRow
                         {
                             Name = value.Category.Name
                         }
                     }
                     """, product.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("""
                     SELECT "p"."Id", "p"."Name", "c"."Id" IS NULL, "c"."Name"
                     FROM "Products" AS "p"
                     LEFT JOIN "Categories" AS "c" ON "p"."CategoryId" = "c"."Id"
                     WHERE "p"."PriceCents" > 1000 AND "p"."Name" <> 'Hidden'
                     ORDER BY "p"."Name"
                     """, sql, ignoreLineEndingDifferences: true);
        Assert.Equal(["Desk", "Uncategorized"], rows.Select(row => row.Name));
        Assert.Equal("Office", rows[0].Category!.Name);
        Assert.Null(rows[1].Category);
    }

    [Fact]
    public async Task ConditionInsideProjectionAndProjectionInsideConditionTranslate()
    {
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var expensive = Condition<DbProduct>.Create(product => price.Invoke(product) > 1000);
        var row = Projection<DbProduct>.Create(product => new ProductRow
        {
            Id = product.Id,
            Name = product.Name,
            IsExpensive = expensive.Invoke(product)
        });

        var query = _db.Products.Where(expensive).Select(row).OrderBy(value => value.Id);
        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Equal(
            "product => product.PriceCents > 1000",
            expensive.GetExpandedExpression().ToReadableString());
        Assert.Equal("""
                     product => new ProductRow
                     {
                         Id = product.Id,
                         Name = product.Name,
                         IsExpensive = product.PriceCents > 1000
                     }
                     """, row.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal("""
                     SELECT "p"."Id", "p"."Name", "p"."PriceCents" > 1000 AS "IsExpensive"
                     FROM "Products" AS "p"
                     WHERE "p"."PriceCents" > 1000
                     ORDER BY "p"."Id"
                     """, sql, ignoreLineEndingDifferences: true);

        Assert.All(rows, value => Assert.True(value.IsExpensive));
        Assert.Equal(3, rows.Length);
    }

    [Fact]
    public async Task ThenAndDeepMixedCompositionTranslate()
    {
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var doubled = Projection<int>.Create(value => value * 2);
        var threshold = Condition<int>.Create(value => value >= 3000);
        var composedScalar = price.Then(doubled);
        var composedCondition = composedScalar.Then(threshold);
        var mixed = Projection<DbProduct>.Create(product => composedCondition.Invoke(product));

        var values = await _db.Products
            .Where(composedCondition)
            .Select(mixed)
            .ToArrayAsync();

        Assert.Equal(
            "product => (product.PriceCents * 2) >= 3000",
            composedCondition.GetExpandedExpression().ToReadableString());
        Assert.Equal(3, values.Length);
        Assert.All(values, Assert.True);
    }

    [Fact]
    public async Task ThenAndDeepMixedCompositionTranslateProjectionAsConditionAndConditionAsProjection()
    {
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var doubled = Projection<int>.Create(value => value * 2);
        var threshold = Condition<int>.Create(value => value >= 3000);
        var composedScalar = price.Then(doubled);
        var composedCondition = composedScalar.Then(threshold);
        var mixed = Projection<DbProduct>.Create(product => composedCondition.Invoke(product));

        var values = await _db.Products
            .Where(mixed)
            .Select(composedCondition)
            .ToArrayAsync();

        Assert.Equal(
            "product => (product.PriceCents * 2) >= 3000",
            mixed.GetExpandedExpression().ToReadableString());
        Assert.Equal(3, values.Length);
        Assert.All(values, Assert.True);
    }

    [Fact]
    public async Task MergedProjectionTranslatesWithOverlaySemantics()
    {
        var basis = Projection<DbProduct>.Create(product => new ProductRow
        {
            Id = product.Id,
            Name = product.Name
        });
        var overlay = Projection<DbProduct>.Create(product => new ProductRow
        {
            Name = product.Name + "!",
            IsExpensive = product.PriceCents > 1000
        });
        var merged = basis.MergeBindings(overlay);

        var query = _db.Products.Select(merged).OrderBy(row => row.Name);
        var rows = await query.ToArrayAsync();

        Assert.Equal("""
                     product => new ProductRow
                     {
                         Id = product.Id,
                         Name = product.Name + "!",
                         IsExpensive = product.PriceCents > 1000
                     }
                     """, merged.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.False(rows.Single(row => row.Name == "Pencil!").IsExpensive);
        Assert.True(rows.Single(row => row.Name == "Desk!").IsExpensive);
    }

    [Fact]
    public async Task StructuralAdaptationTranslates()
    {
        var condition = Condition<StructuralDbProduct>.Create(product =>
            product.PriceCents > 1000 && product.Name != "Hidden");
        var projection = Projection<StructuralDbProduct>.Create(product =>
            new StructuralDbProductRow
            {
                Id = product.Id,
                Name = product.Name,
                IsExpensive = product.PriceCents > 1000
            });
        var adaptedCondition = condition.AdaptSource<DbProduct>();
        var adaptedProjection = projection.Adapt<DbProduct, ProductRow>();

        var query = _db.Products
            .Where(adaptedCondition)
            .Select(adaptedProjection)
            .OrderBy(product => product.Name);

        var rows = await query.ToListAsync();

        Assert.Equal(
            "product => (product.PriceCents > 1000) && (product.Name != \"Hidden\")",
            adaptedCondition.GetExpandedExpression().ToReadableString());
        Assert.Equal("""
                     product => new ProductRow
                     {
                         Id = product.Id,
                         Name = product.Name,
                         IsExpensive = product.PriceCents > 1000
                     }
                     """, adaptedProjection.GetExpandedExpression().ToReadableString(), ignoreLineEndingDifferences: true);
        Assert.Equal(["Desk", "Uncategorized"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.True(row.IsExpensive));
    }

    [Fact]
    public async Task ComposableOrderingSelectorsExpandAndTranslate()
    {
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var adjustedPrice = Projection<DbProduct>.Create(product => price.Invoke(product) + 1);
        var name = Projection<DbProduct>.Create(product => product.Name);

        var query = _db.Products
            .OrderByDescending(adjustedPrice)
            .ThenBy(name);

        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Contains("ORDER BY \"p\".\"PriceCents\" + 1 DESC, \"p\".\"Name\"", sql);
        Assert.Equal(["Desk", "Hidden", "Uncategorized", "Pencil"], rows.Select(product => product.Name));
    }

    [Fact]
    public void ComposableConditionConsumersExpandAndTranslate()
    {
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var expensive = Condition<DbProduct>.Create(product => price.Invoke(product) > 1000);

        Assert.True(_db.Products.Any(expensive));
        Assert.False(_db.Products.All(expensive));
        Assert.Equal(3, _db.Products.Count(expensive));
        Assert.Equal(3L, _db.Products.LongCount(expensive));
        Assert.True(_db.Products.OrderBy(product => product.Id).First(expensive).PriceCents > 1000);
        Assert.Null(_db.Products.FirstOrDefault(Condition<DbProduct>.False));
    }

    [Fact]
    public async Task ComposableGroupingSelectorsExpandAndTranslate()
    {
        var categoryId = Projection<DbProduct>.Create(product => product.CategoryId);
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var adjustedPrice = Projection<DbProduct>.Create(product => price.Invoke(product) + 1);

        var query = _db.Products
            .GroupBy(categoryId, adjustedPrice)
            .Select(group => new { group.Key, Total = group.Sum() })
            .OrderBy(row => row.Key);

        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Contains("GROUP BY \"p\".\"CategoryId\"", sql);
        Assert.Contains("SUM(\"p\".\"PriceCents\" + 1)", sql);
        Assert.Collection(
            rows,
            row =>
            {
                Assert.Null(row.Key);
                Assert.Equal(10502, row.Total);
            },
            row =>
            {
                Assert.NotNull(row.Key);
                Assert.Equal(20202, row.Total);
            });
    }

    [Fact]
    public async Task ComposableSelectManySelectorExpandsAndTranslates()
    {
        var products = Projection<DbCategory>.Create(category => category.Products.AsEnumerable());
        var name = Projection<DbProduct>.Create(product => product.Name);

        var query = _db.Categories
            .SelectMany(products)
            .OrderBy(name);

        var sql = query.ToQueryString();
        var rows = await query.ToArrayAsync();

        Assert.Contains("INNER JOIN \"Products\"", sql);
        Assert.Equal(["Desk", "Pencil"], rows.Select(product => product.Name));
    }

    [Fact]
    public async Task QuerySyntaxFacadeExpandsMarkersAndPreservesAsyncProviderExecution()
    {
        var source = _db.Products;
        var price = Projection<DbProduct>.Create(product => product.PriceCents);
        var expensive = Condition<DbProduct>.Create(product => price.Invoke(product) > 1000);
        var name = Projection<DbProduct>.Create(product => product.Name);
        var row = Projection<DbProduct>.Create(product => new ProductRow
        {
            Id = product.Id,
            Name = name.Invoke(product),
            IsExpensive = expensive.Invoke(product)
        });

        var query =
            from product in source.AsRaffinertQuery()
            where expensive.Invoke(product)
            orderby price.Invoke(product) descending, name.Invoke(product)
            select row.Invoke(product);

        var sql = query.ToQueryString();
        var rows = await query.ToListAsync();

        Assert.Same(((IQueryable<DbProduct>)source).Provider, query.Provider);
        Assert.Same(query.UnderlyingQuery.Provider, query.Provider);
        Assert.DoesNotContain(nameof(ComposableExpression<,>.Invoke), query.Expression.ToString());
        Assert.Contains("WHERE \"p\".\"PriceCents\" > 1000", sql);
        Assert.Contains("ORDER BY \"p\".\"PriceCents\" DESC, \"p\".\"Name\"", sql);
        Assert.Equal(["Desk", "Hidden", "Uncategorized"], rows.Select(product => product.Name));
        Assert.All(rows, product => Assert.True(product.IsExpensive));
    }

    [Fact]
    public async Task QuerySyntaxFacadeExpandsMultipleFromJoinAndGrouping()
    {
        var productName = Projection<DbProduct>.Create(product => product.Name);
        var productCategoryId = Projection<DbProduct>.Create(product => product.CategoryId);
        var categoryId = Projection<DbCategory>.Create(category => (int?)category.Id);
        var categoryProducts = Projection<DbCategory>.Create(category => category.Products.AsEnumerable());

        var flattened =
            from category in _db.Categories.AsRaffinertQuery()
            from product in categoryProducts.Invoke(category)
            orderby productName.Invoke(product)
            select productName.Invoke(product);

        var joined =
            from product in _db.Products.AsRaffinertQuery()
            join category in _db.Categories
                on productCategoryId.Invoke(product) equals categoryId.Invoke(category)
            group productName.Invoke(product) by category.Name into namesByCategory
            select new { namesByCategory.Key, Count = namesByCategory.Count() };

        var flattenedSql = flattened.ToQueryString();
        var joinedSql = joined.ToQueryString();
        var flattenedNames = await flattened.ToArrayAsync();
        var groups = await joined.ToListAsync();

        Assert.DoesNotContain(nameof(ComposableExpression<,>.Invoke), flattened.Expression.ToString());
        Assert.DoesNotContain(nameof(ComposableExpression<,>.Invoke), joined.Expression.ToString());
        Assert.Contains("INNER JOIN \"Products\"", flattenedSql);
        Assert.Contains("INNER JOIN \"Categories\"", joinedSql);
        Assert.Contains("GROUP BY \"c\".\"Name\"", joinedSql);
        Assert.Equal(["Desk", "Pencil"], flattenedNames);
        var group = Assert.Single(groups);
        Assert.Equal("Office", group.Key);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public async Task MapToExistingPreservesTrackedCollectionNavigation()
    {
        var category = await _db.Categories
            .Include(value => value.Products)
            .SingleAsync(value => value.Name == "Office");
        var retainedProduct = category.Products.Single(product => product.Name == "Desk");
        var removedProduct = category.Products.Single(product => product.Name == "Pencil");
        var originalCollection = category.Products;
        var projection = Projection<CategoryCollectionUpdate, DbCategory>.Create(update => new DbCategory
        {
            Products = { update.RetainedProduct }
        });
        DbCategory? destination = category;

        projection.MapToExisting(
            new CategoryCollectionUpdate { RetainedProduct = retainedProduct },
            ref destination);
        _db.ChangeTracker.DetectChanges();

        Assert.Same(category, destination);
        Assert.Same(originalCollection, category.Products);
        Assert.Same(retainedProduct, Assert.Single(category.Products));
        Assert.Null(removedProduct.Category);
        Assert.Null(removedProduct.CategoryId);
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

public sealed class CategoryCollectionUpdate
{
    public required DbProduct RetainedProduct { get; set; }
}

public sealed class StructuralDbProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
}

public sealed class StructuralDbProductRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsExpensive { get; set; }
}
