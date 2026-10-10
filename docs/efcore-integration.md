# EF Core integration

User guide for `Raffinert.Expressions.EntityFrameworkCore` **1.2.0**.
The optional adapter provides async composable-condition operators and invocation expansion for ordinary EF LINQ queries.

## 1. Installation and compatibility

Install the adapter and your database provider:

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore --version 1.2.0
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11
```

| Requirement | Supported contract |
| --- | --- |
| Application | .NET 10 (`net10.0`) |
| EF Core and EF Core Relational | `>= 10.0.11` and `< 11.0.0` (`[10.0.11,11.0.0)`) |
| Core dependency | `Raffinert.Expressions` 1.2.0 |
| Public namespace | `Raffinert.Expressions` |

EF Core 7, 8, 9 and 11 are outside the adapter's range. Install the appropriate provider for your database. QuerySyntax is optional; Core (`netstandard2.0`) and QuerySyntax (`netstandard2.1`) are independent of EF.

## 2. Quick start

This complete SQLite example creates an in-memory database and queries a seeded order. Keep the connection open while using an in-memory database.

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseRaffinertExpressions()
    .Options;

await using var db = new OrdersContext(options);
await db.Database.EnsureCreatedAsync();
db.Orders.Add(new Order { Id = 1, TotalCents = 2000, Active = true, Name = "Desk" });
await db.SaveChangesAsync();

var expensive = Condition<Order>.Create(x => x.TotalCents > 1000);
var rows = await db.Orders
    .Where(x => expensive.Invoke(x))
    .Select(x => new { x.Id, x.TotalCents })
    .ToListAsync();

public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public int Id { get; set; }
    public int TotalCents { get; set; }
    public bool Active { get; set; }
    public string Name { get; set; } = "";
}
```

The following snippets use this context and model.

## 3. Direct operators and asynchronous methods

Pass wrappers directly to Raffinert's LINQ overloads:

```csharp
var expensive = Condition<Order>.Create(x => x.TotalCents > 1000);
var summary = Projection<Order>.Create(x => new { x.Id, x.Name });
var rows = await db.Orders.Where(expensive).Select(summary).ToArrayAsync();
bool exists = await db.Orders.AnyAsync(expensive, cancellationToken);
```

Direct `.Where(condition)` and `.Select(projection)` expand their wrapper arguments without registration. The adapter's condition overloads also work without `UseRaffinertExpressions()`:

| Async condition methods | Result |
| --- | --- |
| `AnyAsync`, `AllAsync` | Boolean |
| `CountAsync`, `LongCountAsync` | Matching element count |
| `FirstAsync`, `FirstOrDefaultAsync` | First matching element |
| `SingleAsync`, `SingleOrDefaultAsync` | Single matching element |
| `LastAsync`, `LastOrDefaultAsync` | Last matching element |

They accept `IComposableExpression<T, bool>` and an optional cancellation token. EF determines empty-sequence, single-element and ordering semantics. Order a query before a last-element operator. EF's ordinary lambda and predicate-less overloads work as usual. Use EF's `ToListAsync`/`ToArrayAsync` for materialization.

## 4. Opt-in invocation expansion

Call `UseRaffinertExpressions()` after configuring the provider when ordinary EF-facing lambdas contain Raffinert invocations:

```csharp
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseRaffinertExpressions()
    .Options;

await using var configuredDb = new OrdersContext(options);
var query = configuredDb.Orders.Where(x => expensive.Invoke(x) && x.Active);
```

Registration applies only to contexts configured with it; repeated calls are harmless. It supports nested conditions/projections, native wrappers referenced through `IComposableExpression<,>`, `InvokeOrDefault`, and Raffinert method-group/captured-delegate callbacks.

Passing a wrapper directly to an operator needs no registration. Invocation inside another wrapper also works when that outer wrapper is passed directly to an operator. Register the adapter for invocations in ordinary provider-facing lambdas without such an explicit expansion boundary.

## 5. Conditions, projections and captured values

```csharp
var threshold = 1000;
var amount = Projection<Order>.Create(x => x.TotalCents);
var expensive = Condition<Order>.Create(x => amount.Invoke(x) > threshold);
var summary = Projection<Order>.Create(x => new
{
    x.Id,
    IsExpensive = expensive.Invoke(x)
});
var query = db.Orders.Where(x => expensive.Invoke(x)).Select(summary);

var first = await query.ToArrayAsync();
threshold = 10000;
var second = await query.ToArrayAsync(); // Uses the current threshold.
```

Raffinert expands expressions; **EF owns native parameter extraction, naming, binding, caching and translation**. Ordinary queries use current captured values on every execution, including cache hits. Reassigning an outer captured wrapper is observed on the next ordinary execution.

A wrapper's `GetExpression()` must have stable structure after its first expansion or evaluation. Nested composition is cached per instance; construct a new wrapper when changing that composition. Changing scalar captures needs no new wrapper. Exact generated parameter names and universal SQL-shape stability are not API guarantees; constants and collection cardinality can affect SQL.

`InvokeOrDefault` produces `default(TResult)` for a null input; ordinary `Invoke` adds no null check. Unrelated methods named `Invoke` are ordinary methods. Every expanded expression must still be accepted by the selected provider.

Wrapper and delegate-callback target getters can run during expansion. Keep them simple and free of side effects. EF evaluates ordinary captured expressions and can retry a failing getter; there is no universal getter-read count guarantee.

## 6. EF.Parameter, EF.Constant and EF.MultipleParameters

Use EF's explicit directives inside ordinary conditions and projections:

```csharp
var threshold = 1000;
int[] ids = [1, 3];
var selected = Condition<Order>.Create(x =>
    x.TotalCents > EF.Parameter(threshold + 100) &&
    Enumerable.Contains(EF.MultipleParameters(ids), x.Id));
var query = db.Orders.Where(x => selected.Invoke(x));
```

| Directive | Native EF behavior |
| --- | --- |
| `EF.Parameter(value)` | Requests parameterization; collections use native single-collection parameter translation |
| `EF.Constant(value)` | Requests inline values, which can appear in SQL |
| `EF.MultipleParameters(collection)` | Requests per-element collection parameters; padding and SQL depend on provider/cardinality |

Computed scalars, client methods, arrays and mutable lists follow native semantics. Supported collection cases include empty/duplicate elements and nullable/null collections where the provider accepts them. Ordinary captured collections also use native parameterization. Collection SQL shape and parameter limits are provider-specific. EF rejects row-dependent or nested directive operands; use a native-valid operand.

## 7. Supported provider-specific functionality

```csharp
var prefix = "D";
var named = Condition<Order>.Create(x => EF.Functions.Like(x.Name, prefix + "%"));
var query = db.Orders.Where(x => named.Invoke(x));
```

`EF.Functions.Like`, `EF.Property` and `EF.Functions.Collate` are covered on SQLite and SQL Server, including captured metadata and computed Like patterns. SQLite `Glob` and SQL Server `DateDiffDay` are covered on their respective providers. Use property names and collations valid for your provider/model. Raffinert does not add provider functions or make an untranslatable expression valid. Custom `HasDbFunction` mappings are outside the tested scope.

## 8. Compiled-query behavior and limitations

With `UseRaffinertExpressions()`, `EF.CompileQuery` and `EF.CompileAsyncQuery` support stable closed wrappers and scalar parameters to the compiled delegate:

```csharp
var active = Condition<Order>.Create(x => x.Active);
var countActive = EF.CompileQuery((OrdersContext context, int minimum) =>
    context.Orders.Count(x => active.Invoke(x) && x.TotalCents > minimum));

int count = countActive(db, 1000);
```

Keep changing scalar values in delegate parameters, as `minimum` is above. Runtime captures inside compiled wrapper bodies and wrappers passed as compiled delegate parameters are unsupported. A closed wrapper is fixed when EF creates its compiled delegate; use ordinary queries or a new compiled delegate for wrapper changes. Parameterization directives inside compiled wrapper bodies are outside the supported contract; use ordinary queries for those expressions.

Raffinert invocation expansion is unsupported in EF's experimental precompiled-query infrastructure. NativeAOT and actual precompiled-code generation are not certified.

`RaffinertExpressionInterceptor.Instance` is available for standalone registration with closed wrapper targets. For ordinary captured wrappers, configure `UseRaffinertExpressions()` instead of registering the interceptor alone.

## 9. Diagnostics and security considerations

**`ToQueryString()` follows native EF behavior and may expose parameter values even when sensitive-data logging is disabled.** Do not expose rendered SQL containing sensitive information in logs, telemetry or user interfaces.

Parameterized values are bound separately from executed SQL text but are accessible through command parameters. `EF.Constant` requests inline values. EF client-evaluation exceptions can include user exception details. Treat query strings, exception output, sensitive-data logging and custom command interceptors as potential sources of sensitive data.

## 10. Tested providers and compatibility limits

| Provider | Tested boundary |
| --- | --- |
| `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11 | .NET 10 on Windows and Linux; execution and isolated package consumers |
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.11 | .NET 10 on Windows; real SQL Server 2025 LocalDB, RTM-CU3 17.0.4025.3 |

Coverage includes expansion, directives, current captures, cache reuse, nullability, cancellation/recovery, compiled-query scope and pooled contexts/context factories. The dependency range does not certify every EF patch; revalidate upgrades in your application. Other providers, Azure SQL, other SQL Server versions/collations, custom mapped functions and NativeAOT are not certified.

## 11. Troubleshooting

| Symptom | Action |
| --- | --- |
| EF cannot translate a Raffinert `Invoke` call | Configure the queried context with `UseRaffinertExpressions()`, or pass the wrapper directly to a Raffinert operator |
| Wrapper target cannot be resolved | Use a stable captured/constant wrapper target; arbitrary factory calls and row-dependent wrapper targets are unsupported |
| Composition-cycle error | Break cyclic references between wrappers |
| Compiled wrapper throws `NotSupportedException` | Use ordinary queries for captures/directives, or scalar compiled delegate parameters |
| Standalone interceptor cannot expand an ordinary capture | Use `UseRaffinertExpressions()` |
| Provider rejects a function, collation or directive operand | Check that the equivalent native EF expression is supported by your provider/model |
| Last-element operator needs ordering | Apply `OrderBy` before `LastAsync` or `LastOrDefaultAsync` |
| Compiler registration error with custom EF services | Configure the provider first and avoid conflicting query compiler registrations |
| Sensitive values appear in `ToQueryString()` | Treat rendered SQL as sensitive; disabling sensitive-data logging does not hide these values |
