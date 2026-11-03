using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.IntegrationTests;

public sealed class NestedCollectionFilteringTests
{
    [Fact]
    public async Task Filters_parent_orders_and_projects_only_matching_child_lines()
    {
        await using var fixture = await Fixture.CreateAsync();

        var activeLine = Expr<OrderLine>.Create(line =>
            !line.IsCancelled && line.Quantity > 0);
        var minimumPriceCents = 10_000;
        var matchingLine = activeLine.And(line =>
            line.UnitPriceCents >= minimumPriceCents);

        var hasMatchingLines = Expr<Order>.Create(order =>
            order.Lines.Any(line => matchingLine.Invoke(line)));
        var lineDto = Expr<OrderLine>.Create(line => new OrderLineDto
        {
            Id = line.Id,
            Product = line.Product,
            Quantity = line.Quantity,
            UnitPriceCents = line.UnitPriceCents
        });
        var orderDto = Expr<Order>.Create(order => new OrderDto
        {
            Id = order.Id,
            Lines = order.Lines
                .Where(line => matchingLine.Invoke(line))
                .OrderBy(line => line.Id)
                .Select(line => lineDto.Invoke(line))
                .ToList()
        });

        var query = fixture.Db.Orders
            .AsNoTracking()
            .Where(hasMatchingLines)
            .OrderBy(order => order.Id)
            .Select(orderDto);

        // Translation must succeed; filtering should not fall back to a compiled delegate.
        Assert.Contains("SELECT", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);

        var result = await query.ToListAsync();

        Assert.Collection(result,
            first =>
            {
                Assert.Equal(1, first.Id);
                var line = Assert.Single(first.Lines);
                Assert.Equal(101, line.Id);
                Assert.Equal("Desk", line.Product);
                Assert.Equal(2, line.Quantity);
                Assert.Equal(12_000, line.UnitPriceCents);
            },
            second =>
            {
                Assert.Equal(3, second.Id);
                Assert.Equal(new[] { 301, 302 }, second.Lines.Select(line => line.Id).ToArray());
            });
    }

    [Fact]
    public async Task Keeps_parents_with_no_matching_lines_when_parent_filter_is_omitted()
    {
        await using var fixture = await Fixture.CreateAsync();

        var matchingLine = Expr<OrderLine>.Create(line =>
            !line.IsCancelled && line.Quantity > 0 && line.UnitPriceCents >= 10_000);
        var orderDto = Expr<Order>.Create(order => new OrderDto
        {
            Id = order.Id,
            Lines = order.Lines
                .Where(line => matchingLine.Invoke(line))
                .OrderBy(line => line.Id)
                .Select(line => new OrderLineDto { Id = line.Id })
                .ToList()
        });

        var result = await fixture.Db.Orders
            .AsNoTracking()
            .OrderBy(order => order.Id)
            .Select(orderDto)
            .ToListAsync();

        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Select(order => order.Id).ToArray());
        Assert.Equal(new[] { 101 }, result[0].Lines.Select(line => line.Id).ToArray());
        Assert.Empty(result[1].Lines);
        Assert.Equal(new[] { 301, 302 }, result[2].Lines.Select(line => line.Id).ToArray());
        Assert.Empty(result[3].Lines);
    }

    [Fact]
    public async Task Expands_method_group_invocations_inside_nested_where_and_select()
    {
        await using var fixture = await Fixture.CreateAsync();

        var matchingLine = Expr<OrderLine>.Create(line =>
            !line.IsCancelled && line.Quantity > 0 && line.UnitPriceCents >= 10_000);
        var lineDto = Expr<OrderLine>.Create(line => new OrderLineDto
        {
            Id = line.Id,
            Product = line.Product
        });
        var orderDto = Expr<Order>.Create(order => new OrderDto
        {
            Id = order.Id,
            Lines = order.Lines
                .Where(matchingLine.Invoke)
                .OrderBy(line => line.Id)
                .Select(lineDto.Invoke)
                .ToList()
        });

        var result = await fixture.Db.Orders
            .AsNoTracking()
            .OrderBy(order => order.Id)
            .Select(orderDto)
            .ToListAsync();

        Assert.Equal(new[] { 101 }, result[0].Lines.Select(line => line.Id).ToArray());
        Assert.Empty(result[1].Lines);
        Assert.Equal(new[] { 301, 302 }, result[2].Lines.Select(line => line.Id).ToArray());
        Assert.Equal("Keyboard", result[2].Lines[0].Product);
        Assert.Empty(result[3].Lines);
    }

    public sealed class Order
    {
        public int Id { get; set; }
        public List<OrderLine> Lines { get; set; } = [];
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
        public int UnitPriceCents { get; set; }
        public bool IsCancelled { get; set; }
    }

    public sealed class OrderDto
    {
        public int Id { get; set; }
        public List<OrderLineDto> Lines { get; set; } = [];
    }

    public sealed class OrderLineDto
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
        public int UnitPriceCents { get; set; }
    }

    private sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(order => order.Lines)
                .WithOne()
                .HasForeignKey(line => line.OrderId);
        }
    }

    private sealed class Fixture(SqliteConnection connection, OrdersDbContext db)
        : IAsyncDisposable
    {
        public OrdersDbContext Db => db;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<OrdersDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new OrdersDbContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Orders.AddRange(
                new Order
                {
                    Id = 1,
                    Lines =
                    [
                        new OrderLine { Id = 101, Product = "Desk", Quantity = 2, UnitPriceCents = 12_000 },
                        new OrderLine { Id = 102, Product = "Chair", Quantity = 1, UnitPriceCents = 20_000, IsCancelled = true },
                        new OrderLine { Id = 103, Product = "Lamp", Quantity = 1, UnitPriceCents = 9_999 },
                        new OrderLine { Id = 104, Product = "Monitor", Quantity = 0, UnitPriceCents = 40_000 }
                    ]
                },
                new Order
                {
                    Id = 2,
                    Lines =
                    [
                        new OrderLine { Id = 201, Quantity = 1, UnitPriceCents = 15_000, IsCancelled = true },
                        new OrderLine { Id = 202, Quantity = 1, UnitPriceCents = 5_000 }
                    ]
                },
                new Order
                {
                    Id = 3,
                    Lines =
                    [
                        new OrderLine { Id = 301, Product = "Keyboard", Quantity = 1, UnitPriceCents = 30_000 },
                        new OrderLine { Id = 302, Product = "Mouse", Quantity = 3, UnitPriceCents = 11_000 }
                    ]
                },
                new Order { Id = 4 });

            await db.SaveChangesAsync();
            return new Fixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
