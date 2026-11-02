using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;
using Xunit;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public sealed class NestedCollectionFilteringTests
{
    [Fact]
    public async Task Expands_nested_invocations_in_ordinary_ef_lambdas_and_respects_updated_captures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(connection)
            .UseRaffinertExpressions()
            .Options;

        await using var db = new OrdersDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Orders.AddRange(
            new Order
            {
                Id = 1,
                Lines =
                [
                    new OrderLine { Id = 101, UnitPriceCents = 12_000, Quantity = 1 },
                    new OrderLine { Id = 102, UnitPriceCents = 30_000, Quantity = 2 },
                    new OrderLine { Id = 103, UnitPriceCents = 40_000, Quantity = 1, IsCancelled = true }
                ]
            },
            new Order
            {
                Id = 2,
                Lines = [new OrderLine { Id = 201, UnitPriceCents = 15_000, Quantity = 1 }]
            },
            new Order
            {
                Id = 3,
                Lines = [new OrderLine { Id = 301, UnitPriceCents = 5_000, Quantity = 1 }]
            },
            new Order { Id = 4 });

        await db.SaveChangesAsync();

        var activeLine = Expr<OrderLine>.Create(line =>
            !line.IsCancelled && line.Quantity > 0);
        var minimumPriceCents = 10_000;
        var matchingLine = activeLine.And(line =>
            line.UnitPriceCents >= minimumPriceCents);
        var lineDto = Expr<OrderLine>.Create(line => new OrderLineDto
        {
            Id = line.Id,
            UnitPriceCents = line.UnitPriceCents
        });

        // Neither the Where nor Select receives an Expr directly; the registered EF adapter
        // must expand the nested Invoke markers before the provider translates this query.
        var query = db.Orders
            .AsNoTracking()
            .Where(order => order.Lines.Any(line => matchingLine.Invoke(line)))
            .OrderBy(order => order.Id)
            .Select(order => new OrderDto
            {
                Id = order.Id,
                Lines = order.Lines
                    .Where(line => matchingLine.Invoke(line))
                    .OrderBy(line => line.Id)
                    .Select(line => lineDto.Invoke(line))
                    .ToList()
            });

        Assert.Contains("SELECT", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);

        var first = await query.ToListAsync();
        Assert.Equal(new[] { 1, 2 }, first.Select(order => order.Id).ToArray());
        Assert.Equal(new[] { 101, 102 }, first[0].Lines.Select(line => line.Id).ToArray());
        Assert.Equal(new[] { 201 }, first[1].Lines.Select(line => line.Id).ToArray());

        // The expression structure is unchanged; EF should use the current captured value.
        minimumPriceCents = 25_000;

        var second = await query.ToListAsync();
        var remaining = Assert.Single(second);
        Assert.Equal(1, remaining.Id);
        var remainingLine = Assert.Single(remaining.Lines);
        Assert.Equal(102, remainingLine.Id);
        Assert.Equal(30_000, remainingLine.UnitPriceCents);
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
        public int UnitPriceCents { get; set; }
        public int Quantity { get; set; }
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
}
