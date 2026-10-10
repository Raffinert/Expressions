# EF Core integration

`Raffinert.Expressions.EntityFrameworkCore` provides async condition operators and
optional expansion of Raffinert invocation markers in ordinary EF Core LINQ trees.
Its public API uses the `Raffinert.Expressions` namespace.

## Choosing a package

| Package | Purpose | Registration |
| --- | --- | --- |
| Raffinert.Expressions | Expression composition and direct `.Where(condition)` / `.Select(projection)` overloads | None |
| Raffinert.Expressions.QuerySyntax | Provider-independent `AsRaffinertQuery()` expansion at each query-syntax operator | Opt in at the query source |
| Raffinert.Expressions.EntityFrameworkCore | EF async condition terminals and ordinary LINQ marker expansion | Async overloads need none; interception uses `UseRaffinertExpressions()` |

The adapter references core and EF Core, with no dependency on QuerySyntax. Core stays
on netstandard2.0; QuerySyntax stays on netstandard2.1. The EF adapter is one net6.0
assembly built against EF Core 7.0.20. Install it alongside your chosen EF provider:

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11
```

Use the target framework required by your EF provider: EF 10 requires .NET 10, for example.
The 1.2.0 adapter depends on core 1.2.0, which supplies the internal whole-query expansion seam.

## Async predicates without interception

```csharp
using Raffinert.Expressions;
using Microsoft.EntityFrameworkCore;

var expensive = Condition<OrderRow>.Create(x => x.TotalCents >= 10_000);
bool hasExpensive = await db.Orders.AnyAsync(expensive, cancellationToken);
int count = await db.Orders.CountAsync(expensive, cancellationToken);
```

The overloads consume `IComposableExpression<T, bool>`, so both `Condition<T>` and interface
variables work. They expand the predicate before calling EF's corresponding method,
pass cancellation through unchanged, and preserve EF's exceptions/defaults.
Supported methods are `AnyAsync`, `AllAsync`, `CountAsync`, `LongCountAsync`, `FirstAsync`,
`FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync` and
`LastOrDefaultAsync`. Use deterministic ordering for first/last operations.

Normal lambda overloads such as `.AnyAsync(x => x.Active)` remain available. An untyped
`null` argument can be ambiguous between EF's lambda overload and the condition overload;
cast to the intended predicate type when testing null arguments.

Core direct wrappers already expand, and EF's no-predicate async materializers work as usual:

```csharp
var rows = await db.Orders.Where(expensive).ToListAsync(cancellationToken);
```

## Opt-in ordinary LINQ expansion

This complete SQLite example keeps its connection open for the database's entire lifetime:

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
// hasExpensive is true; rows contains { Id = 2, IsExpensive = true }.

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
```

Configure the provider before `UseRaffinertExpressions()`. The typed and untyped options
builder overloads return the same builder. Repeated helper calls register once.
Registration affects only those context options; installing the package has no global effect.
`AsRaffinertQuery()` continues to work independently and can be used with the interceptor.

## Expansion and caching

### Release policy: constant-snapshot interception

`UseRaffinertExpressions()` opts into **constant-snapshot mode** for captures introduced
by wrapper expansion. This is a limited interceptor, not a promise of generic EF
parameterization. Values are correct for each execution, but a different in-wrapper
scalar value creates a different EF compiled-query cache key and usually different SQL.
Hot paths can fragment both EF's query cache and the database's execution-plan cache.

The regression diagnostic executes 25 threshold values plus a repeat: the in-wrapper
case compiles 25 times and emits 25 SQL shapes with no threshold parameter. Normal EF,
an outer scalar, and a direct async condition operator each compile once, bind the
current parameter value, and reuse one SQL shape. See the remediation work log for
observed SQL and version-specific results.

EF extracts query parameters before invoking `IQueryExpressionInterceptor` and consults
its query cache before compilation. A compilation callback alone cannot recover captured
wrapper objects or prevent stale cache hits when their expressions change.

The helper registers the stateless `RaffinertExpressionInterceptor` and decorates the
provider's public `IQueryContextFactory` and `ICompiledQueryCacheKeyGenerator` services.
The factory exposes extracted wrapper values within that context's execution scope;
the key generator applies the same expansion as the interceptor before delegating to
the provider's existing cache-key logic. The adapter retains no expanded-root cache or
global query state. Execution tracking uses a scoped weak reference.

The shared core visitor still performs all invocation expansion, nested composition,
parameter substitution, cycle detection and cached wrapper-body reuse. EF normalization
then folds late null/default nodes and snapshots supported scalar closure members into
constants, because EF's parameter extraction has already finished. Scalar snapshots
participate in the query key. Reassigning a wrapper or changing a scalar inside it
therefore yields current results on normal query execution.

For frequently changing thresholds, keep the scalar outside the wrapper so EF can
parameterize it normally, or pass the wrapper directly to a core/async operator:

```csharp
var active = Condition<OrderRow>.Create(x => x.Active);
int threshold = 1000;
var query = db.Orders.Where(x => active.Invoke(x) && x.TotalCents > threshold);
```

Alternatively, expand before EF's parameter extraction using the direct operators:

```csharp
var expensive = Condition<OrderRow>.Create(x => x.TotalCents > threshold);
var query = db.Orders.Where(expensive); // no embedded Invoke
int count = await db.Orders.CountAsync(expensive); // no interception required
```

`AddInterceptors(RaffinertExpressionInterceptor.Instance)` is available for query trees
whose wrapper targets are already constants. It does not install extracted-value/cache
support. Use the helper for captured wrappers. Manually adding the same interceptor
alongside the helper duplicates callbacks; this is unnecessary.

## Limits and compiled queries

- Expanded expressions must be supported by the chosen provider. SQL translation/provider
  failures propagate; the adapter never compiles predicates for client filtering.
- Wrappers must keep a stable expression structure after first expansion, as required by
  core. Replacing a wrapper is supported in ordinary queries; mutating a custom wrapper's
  cached structure is unsupported.
- Native `Condition` / `Projection` wrappers can be accessed through
  `IComposableExpression<TSource, TResult>`, including casts and nested composition.
  Embedded invocation markers on external interface implementations are unsupported:
  use their public expanded-lambda contract through the direct operators instead.
- Core's evaluator can read closure/static members and run property getters or direct
  constructors. Keep these deterministic and free of database queries or other side effects.
  Getters can run more than once in a single query, including cache-key generation.
- Supported inlined closure snapshots include primitive/enum values, strings, decimals,
  Guid, DateTime, DateTimeOffset, DateOnly, TimeOnly and TimeSpan, including nullable forms.
  Hidden captured arrays/lists are rejected before SQL with guidance to use direct
  wrapper operators. Direct operators allow EF to extract collections and observe their
  current contents. Use `Enumerable.Contains(ids, x.Id)` explicitly for array captures
  when C# overload resolution would otherwise choose a span-based `Contains` method.
  Arbitrary method-based evaluation remains outside the snapshot contract.
- Scalar values must remain stable during one query execution. Standard EF `DbContext`
  concurrency restrictions still apply.
- `InvokeOrDefault` returns the result type's default for a null reference/nullable input:
  null for reference results, zero for numeric results and false for bool. It adds no null
  guard for nonnullable value inputs. Optional relationships need an explicit EF mapping.
- EF compiled sync/async queries are verified with stable closed wrappers and scalar delegate
  parameters. Closed wrappers are fixed at compilation; changing them or captured values
  inside them afterward is unsupported. Pass changing values as scalar delegate parameters.
- A wrapper supplied as an `EF.CompileQuery` / `EF.CompileAsyncQuery` delegate parameter
  cannot be expanded at compilation and fails before SQL execution. Use normal LINQ or a
  stable closed wrapper instead. EF precompiled/AOT queries have not been verified.
- Manually supplied internal service providers and service replacements that discard
  existing decorators are unsupported. Configure the normal EF provider and use the helper.
  Public service decorators that retain and forward the original scoped services are tested
  in both registration orders; this does not establish arbitrary extension compatibility.
  Providers other than SQLite remain unverified. No private EF API is used.

## Tested versions

| EF Core / SQLite version | Consumer framework | Shared SQLite execution tests |
| --- | --- | --- |
| 7.0.20 | net6.0 | Passed |
| 8.0.31 | net8.0 | Passed |
| 9.0.20 | net8.0 | Passed |
| 10.0.11 | net10.0 | Passed |

Each leg runs the same suite against the single adapter compiled with EF 7.0.20.
These results establish runtime compatibility for the listed versions, beyond merely
resolving a NuGet dependency. They do not promise compatibility with untested future
versions or other providers. EF 7 / .NET 6 are retired compatibility baselines, rather
than deployment recommendations.

See [validation details](efcore-validation.md) for commands, test counts, cache regression
coverage and the implementation adjustment needed for EF's parameter-extraction ordering.
