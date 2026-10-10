using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Raffinert.Expressions;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var commands = new SmokeCommands();
var compilations = 0;
var options = new DbContextOptionsBuilder<SmokeContext>().UseSqlite(connection).UseRaffinertExpressions()
    .AddInterceptors(commands).EnableSensitiveDataLogging(false).EnableServiceProviderCaching(false)
    .LogTo(_ => compilations++, new[] { CoreEventId.QueryCompilationStarting }).Options;
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

commands.Executed.Clear();
compilations = 0;
var lifted = db.Rows.Where(x => captured.Invoke(x));
for (var i = 0; i < 25; i++)
{
    threshold = 100 + i;
    Check(await lifted.CountAsync() == 2, "fresh lifted capture");
    Check(commands.Executed.Last().Values.Single() is int value && value == threshold, "current runtime binding");
    Check(commands.Executed.Last().Names.Single() == "__raffinert_threshold_0", "readable stable lifted name");
}
threshold = 100;
Check(await lifted.CountAsync() == 2, "lifted cache hit");
Check(compilations == 1, "one lifted compilation");
Check(commands.Executed.Select(x => x.Sql).Distinct().Count() == 1, "one lifted SQL shape");

foreach (var next in new[] { 1000, 30000, 1000 })
{
    threshold = next;
    Check(await lifted.CountAsync() == (next == 30000 ? 0 : 1), "automatic A -> B -> A");
    Check(Equals(commands.Executed.Last().Values.Single(), threshold), "automatic fresh cache-hit binding");
}

foreach (var constant in new[] { false, true })
{
    commands.Executed.Clear();
    var forced = constant ? Condition<SmokeRow>.Create(x => x.Value > EF.Constant(threshold))
        : Condition<SmokeRow>.Create(x => x.Value > EF.Parameter(threshold));
    var forcedQuery = db.Rows.Where(x => forced.Invoke(x));
    foreach (var next in new[] { 1000, 30000, 1000 })
    {
        threshold = next;
        Check(await forcedQuery.CountAsync() == (next == 30000 ? 0 : 1), "explicit directive fresh results");
        var command = commands.Executed.Last();
        if (constant) Check(command.Values.Length == 0, "explicit constant mode");
        else
        {
            Check(Equals(command.Values.Single(), threshold), "explicit parameter binding");
            Check(command.Names.Single() == "__raffinert_threshold_0", "explicit parameter readable name");
            Check(!command.Sql.Contains(threshold.ToString(), StringComparison.Ordinal), "explicit parameter absent from SQL");
        }
    }
}
commands.Executed.Clear();
var literalParameter = Condition<SmokeRow>.Create(x => x.Value > EF.Parameter(1000));
Check(await db.Rows.CountAsync(x => literalParameter.Invoke(x)) == 1, "literal EF.Parameter");
Check(commands.Executed.Last().Names.Single() == "__raffinert_p_0", "literal fallback name");
Check(Equals(commands.Executed.Last().Values.Single(), 1000), "literal forced binding");
var literalConstant = Condition<SmokeRow>.Create(x => x.Value > EF.Constant(1000));
Check(await db.Rows.CountAsync(x => literalConstant.Invoke(x)) == 1, "literal EF.Constant");
Check(commands.Executed.Last().Values.Length == 0, "literal forced constant");

commands.Executed.Clear();
var marker = "synthetic-smoke-private@example.invalid";
var privateCondition = Condition<SmokeRow>.Create(x => x.Name == marker);
var privateQuery = db.Rows.Where(x => privateCondition.Invoke(x));
Check((await privateQuery.ToArrayAsync()).Length == 0, "private query result");
var privateCommand = commands.Executed.Single();
Check(!privateCommand.Sql.Contains(marker, StringComparison.Ordinal), "private capture absent from SQL");
Check(privateCommand.Values.Contains(marker), "private capture bound as DbParameter");
try
{
    privateQuery.ToQueryString();
    throw new InvalidOperationException("Lifted ToQueryString must fail safely.");
}
catch (NotSupportedException error)
{
    Check(!error.ToString().Contains(marker, StringComparison.Ordinal), "sanitized rendering diagnostic");
}

commands.Executed.Clear();
var explicitPrivate = Condition<SmokeRow>.Create(x => x.Name == EF.Parameter(marker));
Check((await db.Rows.Where(x => explicitPrivate.Invoke(x)).ToArrayAsync()).Length == 0, "private explicit parameter result");
Check(commands.Executed.Single().Values.Contains(marker), "private explicit parameter binding");
Check(!commands.Executed.Single().Sql.Contains(marker, StringComparison.Ordinal), "private explicit parameter absent from SQL");

foreach (var constant in new[] { false, true })
{
    commands.Executed.Clear();
    var computed = constant ? Condition<SmokeRow>.Create(x => x.Value > EF.Constant(threshold * 2))
        : Condition<SmokeRow>.Create(x => x.Value > EF.Parameter(threshold + 100));
    var computedQuery = db.Rows.Where(x => computed.Invoke(x));
    var before = compilations;
    foreach (var next in new[] { 1000, 30000, 1000 })
    {
        threshold = next;
        Check(await computedQuery.CountAsync() == (next == 30000 ? 0 : 1), "computed A -> B -> A results");
        var command = commands.Executed.Last();
        if (constant)
        {
            Check(command.Values.Length == 0, "computed constant has no parameters");
            Check(command.Sql.Contains((threshold * 2).ToString(), StringComparison.Ordinal), "computed constant current literal");
        }
        else
        {
            Check(Equals(command.Values.Single(), threshold + 100), "computed current binding");
            Check(command.Names.Single() == "__raffinert_computed_0", "computed stable name");
            Check(!command.Sql.Contains((threshold + 100).ToString(), StringComparison.Ordinal), "computed value absent from SQL");
        }
    }
    if (constant) Check(computedQuery.ToQueryString().Contains("2000", StringComparison.Ordinal), "computed constant rendering");
    else
    {
        Check(compilations - before == 1, "computed one compilation");
        Check(commands.Executed.Select(x => x.Sql).Distinct().Count() == 1, "computed one SQL shape");
        try
        {
            computedQuery.ToQueryString();
            throw new InvalidOperationException("Computed parameter rendering must fail safely.");
        }
        catch (NotSupportedException error)
        {
            Check(error.InnerException == null, "computed sanitized diagnostic");
        }
    }
}

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
    public string Name { get; set; } = "";
}

public sealed class SmokeCommands : DbCommandInterceptor
{
    public List<(string Sql, object?[] Values, string[] Names)> Executed { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Executed.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(x => x.Value).ToArray(),
            command.Parameters.Cast<DbParameter>().Select(x => x.ParameterName.TrimStart('@', ':', '$')).ToArray()));
        return ValueTask.FromResult(result);
    }
}

public sealed class ExternalCondition : IComposableExpression<SmokeRow, bool>
{
    public bool Invoke(SmokeRow value) => value.Value > 1000;
    public LambdaExpression GetExpandedLambdaExpression() => (Expression<Func<SmokeRow, bool>>)(x => x.Value > 1000);
}
