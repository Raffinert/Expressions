using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public CommandRecorder Commands { get; } = new();
    public DbContextOptions<OrdersContext> Options { get; private set; } = null!;
    public OrdersContext Db { get; private set; } = null!;

    public static async Task<SqliteFixture> CreateAsync(bool intercept = true)
    {
        var fixture = new SqliteFixture();
        try
        {
            await fixture._connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<OrdersContext>()
                .UseSqlite(fixture._connection)
                .AddInterceptors(fixture.Commands);
            if (intercept) builder.UseRaffinertExpressions();
            fixture.Options = builder.Options;
            fixture.Db = new OrdersContext(fixture.Options);
            await fixture.Db.Database.EnsureCreatedAsync();
            var ada = new CustomerRow { Id = 1, Name = "Ada", Active = true };
            var bob = new CustomerRow { Id = 2, Name = "Bob", Active = false };
            fixture.Db.Orders.AddRange(
                new OrderRow
                {
                    Id = 1,
                    TotalCents = 200,
                    Active = true,
                    Name = "Pencil",
                    Customer = ada,
                    Lines = { new LineRow { AmountCents = 100 } }
                },
                new OrderRow
                {
                    Id = 2,
                    TotalCents = 20000,
                    Active = true,
                    Name = "Desk",
                    Customer = ada,
                    Lines = { new LineRow { AmountCents = 200 }, new LineRow { AmountCents = 300 } }
                },
                new OrderRow { Id = 3, TotalCents = 1500, Active = false, Name = "Uncategorized" },
                new OrderRow { Id = 4, TotalCents = 9000, Active = false, Name = "Hidden", Customer = bob });
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            fixture.Commands.Clear();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Db != null) await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<OrderRow> Orders => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderRow>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        modelBuilder.Entity<OrderRow>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId);
    }
}

public sealed class OrderRow
{
    public int Id { get; set; }
    public int TotalCents { get; set; }
    public bool Active { get; set; }
    public string Name { get; set; } = "";
    public int? CustomerId { get; set; }
    public CustomerRow? Customer { get; set; }
    public List<LineRow> Lines { get; set; } = [];
}

public sealed class CustomerRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class LineRow
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int AmountCents { get; set; }
}

public sealed record RecordedCommand(string Sql, object?[] Values, CancellationToken CancellationToken);

public sealed class CommandRecorder : DbCommandInterceptor
{
    public List<RecordedCommand> Executed { get; } = [];
    public void Clear() => Executed.Clear();

    private void Record(DbCommand command, CancellationToken cancellationToken) =>
        Executed.Add(new RecordedCommand(command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(x => x.Value).ToArray(), cancellationToken));

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, CancellationToken.None);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command, cancellationToken);
        return ValueTask.FromResult(result);
    }
}
