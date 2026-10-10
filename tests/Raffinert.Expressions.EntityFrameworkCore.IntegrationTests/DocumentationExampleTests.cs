using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

// Deliberately outside the library namespace: verify exactly the documented imports.
namespace ExampleApplication;

public class DocumentationExampleTests
{
    [Fact]
    public async Task CompleteSqliteDocumentationExampleCompilesAndExecutes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OrdersContext>()
            .UseSqlite(connection)
            .UseRaffinertExpressions()
            .Options;
        await using var db = new OrdersContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Orders.AddRange(
            new OrderRow { Id = 1, TotalCents = 200, Active = true },
            new OrderRow { Id = 2, TotalCents = 20_000, Active = true });
        await db.SaveChangesAsync();

        var expensive = Condition<OrderRow>.Create(x => x.TotalCents >= 10_000);
        bool hasExpensive = await db.Orders.AnyAsync(expensive);
        var rows = await db.Orders
            .Where(x => expensive.Invoke(x))
            .Select(x => new { x.Id, IsExpensive = expensive.Invoke(x) })
            .ToListAsync();

        Assert.True(hasExpensive);
        Assert.True(await db.Orders.AnyAsync(x => x.Active));
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Id);
        Assert.True(row.IsExpensive);
    }
}

public sealed class OrdersContext : DbContext
{
    public OrdersContext(DbContextOptions<OrdersContext> options) : base(options) { }
    public DbSet<OrderRow> Orders => Set<OrderRow>();
}

public sealed class OrderRow
{
    public int Id { get; set; }
    public int TotalCents { get; set; }
    public bool Active { get; set; }
}
