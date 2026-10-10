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
    Check(commands.Executed.Last().Names.Length == 1, "one native parameter");
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
            Check(command.Names.Length == 1, "one explicit native parameter");
            Check(!command.Sql.Contains(threshold.ToString(), StringComparison.Ordinal), "explicit parameter absent from SQL");
        }
    }
}
commands.Executed.Clear();
var literalParameter = Condition<SmokeRow>.Create(x => x.Value > EF.Parameter(1000));
Check(await db.Rows.CountAsync(x => literalParameter.Invoke(x)) == 1, "literal EF.Parameter");
Check(commands.Executed.Last().Names.Length == 1, "literal native parameter");
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
Check(privateQuery.ToQueryString().Contains(marker, StringComparison.Ordinal), "native ToQueryString includes parameter value");

commands.Executed.Clear();
var explicitPrivate = Condition<SmokeRow>.Create(x => x.Name == EF.Parameter(marker));
Check((await db.Rows.Where(x => explicitPrivate.Invoke(x)).ToArrayAsync()).Length == 0, "private explicit parameter result");
Check(commands.Executed.Single().Values.Contains(marker), "private explicit parameter binding");
Check(!commands.Executed.Single().Sql.Contains(marker, StringComparison.Ordinal), "private explicit parameter absent from SQL");

var computed = Condition<SmokeRow>.Create(x => x.Value > EF.Parameter(threshold + 100));
var computedQuery = db.Rows.Where(x => computed.Invoke(x));
foreach (var next in new[] { 1000, 30000, 1000 })
{
    threshold = next;
    Check(await computedQuery.CountAsync() == (next == 30000 ? 0 : 1), "computed parameter A -> B -> A");
    Check(Equals(commands.Executed.Last().Values.Single(), next + 100), "computed native binding");
}
int[] ids = [1];
foreach (var mode in new[] { "parameter", "multiple", "constant" })
{
    Expression<Func<SmokeRow, bool>> predicate = mode switch
    {
        "constant" => x => Enumerable.Contains(EF.Constant(ids), x.Id),
        "multiple" => x => Enumerable.Contains(EF.MultipleParameters(ids), x.Id),
        _ => x => Enumerable.Contains(EF.Parameter(ids), x.Id)
    };
    var collection = Condition<SmokeRow>.Create(predicate);
    var collectionQuery = db.Rows.Where(x => collection.Invoke(x)).OrderBy(x => x.Id).Select(x => x.Id);
    foreach (var next in new int[][] { [1], [2], [], [1, 1, 2], [1] })
    {
        ids = next;
        var native = await db.Rows.Where(predicate).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var control = commands.Executed.Last();
        Check((await collectionQuery.ToArrayAsync()).SequenceEqual(native), "collection native results");
        Check(commands.Executed.Last().Values.SequenceEqual(control.Values), "collection native bindings");
        Check(commands.Executed.Last().Sql == control.Sql, "collection native SQL shape");
    }
}
var pattern = "D%";
var like = Condition<SmokeRow>.Create(x => EF.Functions.Like(x.Name, pattern));
Check(await db.Rows.CountAsync(x => like.Invoke(x)) == await db.Rows.CountAsync(x => EF.Functions.Like(x.Name, pattern)), "native Like function");

var ordinary = new SmokeCallbackHolder();
var carried = db.Rows.OrderBy(x => x.Id).Select(x => ordinary.Callback);
foreach (var offset in new[] { 10, 20, 10 })
{
    ordinary.Offset = offset;
    ordinary.Reads = 0;
    Check((await carried.ToArrayAsync()).All(callback => callback(1) == offset + 1), "carried delegate current result");
    Check(ordinary.Reads == 1, "carried delegate native getter count");
    ordinary.Reads = 0;
    var methodGroups = await db.Rows.Select(x => (Func<int, int>)ordinary.Target.Invoke).ToArrayAsync();
    Check(methodGroups.All(callback => callback(1) == offset + 1), "ordinary method group current result");
    Check(ordinary.Reads == 1, "ordinary method group native getter count");
}
var positive = Condition<SmokeRow>.Create(x => x.Value > threshold);
Func<SmokeRow, bool> delegated = positive.Invoke;
foreach (var next in new[] { 1000, 30000, 1000 })
{
    threshold = next;
    Check(await db.Rows.CountAsync(row => db.Rows.Where(x => x.Id == row.Id).Any(positive.Invoke)) == (next == 30000 ? 0 : 1), "marker method group server expansion");
    Check(await db.Rows.CountAsync(row => db.Rows.Where(x => x.Id == row.Id).Any(delegated)) == (next == 30000 ? 0 : 1), "captured delegate server expansion");
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

public sealed class SmokeCallbackHolder
{
    public int Reads;
    public int Offset;
    public Func<int, int> Callback { get { Reads++; var offset = Offset; return value => value + offset; } }
    public SmokeCallbackTarget Target { get { Reads++; return new SmokeCallbackTarget(Offset); } }
}

public sealed class SmokeCallbackTarget(int offset)
{
    public int Invoke(int value) => value + offset;
}
