using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions.NetFrameworkCompatibility;

internal static class Program
{
    public static async Task Main()
    {
        var options = new DbContextOptionsBuilder<CompatibilityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var context = new CompatibilityDbContext(options))
        {
            context.Products.AddRange(
                new CompatibilityProduct { Name = "Pencil", Price = 2 },
                new CompatibilityProduct { Name = "Desk", Price = 20 });
            await context.SaveChangesAsync().ConfigureAwait(false);

            var expensive = Condition<CompatibilityProduct>.Create(product => product.Price > 10);
            var name = Projection<CompatibilityProduct>.Create(product => product.Name);

            var query = context.Products
                .Where(expensive)
                .Select(name);

            var rows = await query.ToListAsync().ConfigureAwait(false);

            if (rows.Count != 1 || rows[0] != "Desk")
                throw new InvalidOperationException("The .NET Framework async compatibility query returned unexpected results.");
        }
    }
}

internal sealed class CompatibilityDbContext(DbContextOptions<CompatibilityDbContext> options)
    : DbContext(options)
{
    public DbSet<CompatibilityProduct> Products => Set<CompatibilityProduct>();
}

internal sealed class CompatibilityProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
}
