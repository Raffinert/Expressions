using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<SmokeContext>().UseSqlite(connection).UseRaffinertExpressions().Options;
await using var db = new SmokeContext(options);
await db.Database.EnsureCreatedAsync();
db.Rows.AddRange(new SmokeRow { Id = 1, Value = 200 }, new SmokeRow { Id = 2, Value = 20000 });
await db.SaveChangesAsync();

IComposableExpression<SmokeRow, bool> condition = Condition<SmokeRow>.Create(x => x.Value > 1000);
Check(await db.Rows.AnyAsync(condition), "condition overload");
Check(await db.Rows.AnyAsync(x => x.Value > 1000), "lambda overload");
Check(await db.Rows.AnyAsync(), "no-predicate overload");
try
{
    await db.Rows.AnyAsync((Expression<Func<SmokeRow, bool>>)null!);
    throw new InvalidOperationException("Typed null must be rejected.");
}
catch (ArgumentNullException error) when (error.ParamName == "predicate") { }

var query = db.Rows.Where(x => condition.Invoke(x)).Select(x => x.Id);
Check((await query.ToArrayAsync()).SequenceEqual(new[] { 2 }), "interface marker");
condition = Condition<SmokeRow>.Create(x => x.Value < 1000);
Check((await query.ToArrayAsync()).SequenceEqual(new[] { 1 }), "interface reassignment");

var threshold = 1000;
var captured = Condition<SmokeRow>.Create(x => x.Value > threshold);
Check(await db.Rows.CountAsync(captured) == 1, "direct capture");
threshold = 100;
Check(await db.Rows.CountAsync(captured) == 2, "changed direct capture");
Check(await db.Rows.AnyAsync(new ExternalCondition()), "external interface direct operator");

Console.WriteLine($"Package smoke passed: EF {typeof(DbContext).Assembly.GetName().Version}, runtime {Environment.Version}.");

static void Check(bool success, string scenario)
{
    if (!success) throw new InvalidOperationException($"Package smoke failed: {scenario}.");
}

public sealed class SmokeContext(DbContextOptions<SmokeContext> options) : DbContext(options)
{
    public DbSet<SmokeRow> Rows => Set<SmokeRow>();
}

public sealed class SmokeRow
{
    public int Id { get; set; }
    public int Value { get; set; }
}

public sealed class ExternalCondition : IComposableExpression<SmokeRow, bool>
{
    public bool Invoke(SmokeRow value) => value.Value > 1000;
    public LambdaExpression GetExpandedLambdaExpression() => (Expression<Func<SmokeRow, bool>>)(x => x.Value > 1000);
}
