using Microsoft.EntityFrameworkCore;

namespace LinqKitComparison;

public sealed class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ICollection<Purchase> Purchases { get; set; } = [];
}

public sealed class Purchase
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Price { get; set; }
    public required string Description { get; set; }
    public DateTime Date { get; set; }
}

public sealed class Product : IValidFromTo
{
    public int Id { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public bool Discontinued { get; set; }
    public DateTime LastSale { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}

public sealed class PriceList : IValidFromTo
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}

public interface IValidFromTo
{
    DateTime? ValidFrom { get; }
    DateTime? ValidTo { get; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int Amount { get; set; }
    public DateTime OrderDate { get; set; }
}

public sealed record DailyAverage(DateTime OrderDate, double? AverageAmount);

public sealed class ExampleDbContext(DbContextOptions<ExampleDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>()
            .HasMany(customer => customer.Purchases)
            .WithOne(purchase => purchase.Customer)
            .HasForeignKey(purchase => purchase.CustomerId);
    }
}

public static class ExampleData
{
    public static async Task SeedAsync(ExampleDbContext db)
    {
        var alice = new Customer { Name = "Alice" };
        var bob = new Customer { Name = "Bob" };
        var clara = new Customer { Name = "Clara" };

        db.AddRange(
            new Purchase
            {
                Customer = alice,
                Price = 1_500m,
                Description = "Laptop",
                Date = new DateTime(2026, 8, 10)
            },
            new Purchase
            {
                Customer = alice,
                Price = 25m,
                Description = "Mouse",
                Date = new DateTime(2026, 8, 11)
            },
            new Purchase
            {
                Customer = bob,
                Price = 700m,
                Description = "Phone",
                Date = new DateTime(2026, 7, 20)
            },
            new Purchase
            {
                Customer = clara,
                Price = 250m,
                Description = "Premium service plan",
                Date = new DateTime(2026, 8, 15)
            });

        db.Products.AddRange(
            new Product
            {
                Description = "BlackBerry phone",
                Price = 400m,
                LastSale = new DateTime(2026, 8, 20),
                ValidFrom = new DateTime(2025, 1, 1)
            },
            new Product
            {
                Description = "iPhone handset",
                Price = 900m,
                LastSale = new DateTime(2026, 8, 25),
                ValidFrom = new DateTime(2025, 1, 1)
            },
            new Product
            {
                Description = "Nokia classic phone",
                Price = 150m,
                LastSale = new DateTime(2026, 8, 22),
                ValidFrom = new DateTime(2025, 1, 1)
            },
            new Product
            {
                Description = "Ericsson classic phone",
                Price = 200m,
                LastSale = new DateTime(2026, 1, 1),
                Discontinued = true,
                ValidFrom = new DateTime(2025, 1, 1),
                ValidTo = new DateTime(2026, 6, 1)
            },
            new Product
            {
                Description = "foo office chair",
                Price = 300m,
                LastSale = new DateTime(2026, 8, 28),
                ValidFrom = new DateTime(2026, 1, 1)
            },
            new Product
            {
                Description = "far away desk",
                Price = 1_200m,
                LastSale = new DateTime(2026, 8, 28),
                ValidFrom = new DateTime(2026, 1, 1)
            });

        db.PriceLists.AddRange(
            new PriceList
            {
                Name = "Active retail",
                ValidFrom = new DateTime(2026, 1, 1),
                ValidTo = new DateTime(2026, 12, 31)
            },
            new PriceList
            {
                Name = "Archived retail",
                ValidTo = new DateTime(2025, 12, 31)
            },
            new PriceList
            {
                Name = "Business",
                ValidFrom = new DateTime(2026, 1, 1)
            });

        db.Orders.AddRange(
            new Order { Amount = 3, OrderDate = new DateTime(2026, 8, 1) },
            new Order { Amount = 5, OrderDate = new DateTime(2026, 8, 1) },
            new Order { Amount = 7, OrderDate = new DateTime(2026, 8, 2) });

        await db.SaveChangesAsync();
    }
}
