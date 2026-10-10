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

### Runtime parameter lifting

`UseRaffinertExpressions()` expands native embedded wrappers automatically and binds
supported scalar captures as native EF execution parameters. No extra caller-side
expansion method is needed:

```csharp
var email = "synthetic-private-value@example.invalid";
var condition = Condition<OrderRow>.Create(x => x.Name == email);
var query = db.Orders.Where(x => condition.Invoke(x));
await query.ToArrayAsync(); // WHERE Name = @parameter; email in DbParameter.Value
email = "another-synthetic-value@example.invalid";
await query.ToArrayAsync(); // same compiled shape, fresh parameter value
```

Captured values do not become SQL literals or value-specific cache keys. Twenty-five
changing thresholds plus a repeat now produce one compilation and one SQL shape,
matching normal EF, outer captures and direct operators on the tested SQLite versions.
Null parameter optimization may change executed SQL (for example to IS NULL) without
changing the normalized EF compiled-query key.

EF extracts its original parameters before querying its cache. The helper decorates
public IQueryContextFactory and ICompiledQueryCacheKeyGenerator services, resolves
extracted wrappers and calls the existing core inliner once per execution. Supported
late captures receive deterministic, collision-free parameter names and values on that
execution's QueryContext. The provider's original key generator receives the normalized
parameterized expression, never the capture values. On a miss, interception consumes
that same prepared expression; on a hit, fresh values are already bound.

Preparation belongs to the specific QueryContext through scoped weak/ephemeron ownership.
Compilation consumes it; a new execution invalidates prior state, including pooled leases
and recovery after errors/cancellation. There is no global capture cache or persistent
strong reference to user contexts. The core visitor remains the only Invoke expander.

### Readable parameter names

Lifted logical names use source member metadata with the reserved `__raffinert_`
prefix and a per-base numeric suffix. For example, `threshold` becomes
`__raffinert_threshold_0`, `customerEmail` becomes `__raffinert_customerEmail_0`,
and `settings.MinPrice` becomes `__raffinert_settings_MinPrice_0`. A repeated
`threshold` occurrence gets `_1`; an existing `_0` name also advances allocation
to `_1`. Names compare case-insensitively to avoid collisions conservatively.

Only ASCII letters, digits and underscores remain; unsupported character runs
become an underscore, and an unavailable name falls back to `__raffinert_p_0`.
Long paths are truncated with suffix space reserved, keeping logical names at
most 96 characters. Allocation follows expression traversal within each execution.
Naming never evaluates getters, serializes objects or reads captured values.

This is EF-style naming using expression metadata, without EF's private naming
implementation or a promise of identical EF/provider formatting. Providers may
add SQL sigils or transform names. Values continue to use `DbParameter.Value`.
Changing values preserves names and cache reuse for the same capture metadata;
different source capture names may produce different compiled-query keys.
`ToQueryString()` blocks all lifted names under the shared prefix before rendering.

See [naming validation](pr7-parameter-naming-validation.md).

### SQL diagnostics and migration from constant snapshots

This replaces the earlier constant-snapshot implementation; there is no implicit legacy
fallback. Unsupported runtime captures fail before SQL. Explicit developer-authored
constants may still be SQL literals; the guarantee concerns runtime captures, not every
constant in an expression.

Values still exist in DbParameter.Value. Sensitive parameter logging, bind profiling or
custom telemetry can expose them; this feature does not redact those systems. With
sensitive logging disabled, the executed-command and default diagnostic tests keep the
synthetic captured strings out of SQL and diagnostic messages.

EF's ToQueryString formats parameter values as debug declarations/comments independently
of sensitive logging. A public IRelationalQueryStringFactory decorator therefore rejects
rendering queries with lifted parameters before the provider formats their values.
Inspect DbCommand.CommandText for the executed SQL shape instead. Ordinary/direct queries
retain EF's own diagnostic behavior. The adapter now references EF Relational to install
this public diagnostic guard; core remains EF-independent and QuerySyntax independent.

Direct Where(condition), async condition overloads and AsRaffinertQuery() continue to
expand before EF extraction and require no interception. Standalone
AddInterceptors(RaffinertExpressionInterceptor.Instance) supports constant wrapper targets
without runtime captures; extracted wrappers and runtime captures require the helper.

See [runtime validation](pr7-parameter-lifting-validation.md) and
[acceptance tests](../tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/RuntimeParameterLiftingTests.cs).

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
  Each scalar member occurrence is read during preparation; repeated occurrences are not
  deduplicated, so no universal once-per-getter contract is promised.
- Supported inlined runtime captures include primitive/enum values, strings, decimals,
  Guid, DateTime, DateTimeOffset, DateOnly, TimeOnly and TimeSpan, including nullable forms.
  Hidden captured arrays/lists are rejected before SQL with guidance to use direct
  wrapper operators. Direct operators allow EF to extract collections and observe their
  current contents. Use `Enumerable.Contains(ids, x.Id)` explicitly for array captures
  when C# overload resolution would otherwise choose a span-based `Contains` method.
  Arbitrary method-based evaluation remains outside the runtime capture contract.
- Keep captures stable during preparation and getters free of side effects/reentrant DB
  calls. Compilation reuses preparation and does not reread captures. Standard EF
  DbContext concurrency restrictions still apply.
- `InvokeOrDefault` returns the result type's default for a null reference/nullable input:
  null for reference results, zero for numeric results and false for bool. It adds no null
  guard for nonnullable value inputs. Optional relationships need an explicit EF mapping.
- EF compiled sync/async queries are verified with stable closed wrappers without runtime
  scalar captures and with scalar delegate parameters outside the wrapper. Runtime captures
  inside compiled wrappers now fail before SQL; explicit compilation bypasses per-execution
  preparation. Closed wrappers are fixed at compilation. Use scalar delegate parameters
  or ordinary LINQ for changing values. This intentionally replaces legacy frozen literals.
- A wrapper supplied as an `EF.CompileQuery` / `EF.CompileAsyncQuery` delegate parameter
  cannot be expanded at compilation and fails before SQL execution. Use normal LINQ or a
  stable closed wrapper instead. EF precompiled/AOT queries have not been verified.
- Manually supplied internal service providers and service replacements that discard
  existing decorators are unsupported. Configure the normal EF provider and use the helper.
  Public service decorators that retain and forward the original scoped services are tested
  in both registration orders; this does not establish arbitrary extension compatibility.
  SQL Server provider 10.0.11 is also verified on Windows LocalDB with .NET 10;
  other SQL Server EF versions, Azure SQL and other providers remain unverified.
  No private EF API is used.
- AddDbContextPool and AddPooledDbContextFactory are verified on SQLite with poolSize 1
  across repeated leases and reused scoped services. Each new query invalidates prior
  preparation; correctness does not depend on weak-reference collection at pool return.
- Other query interceptors may append operations before Raffinert or rewrite the prepared
  tree after it. Both tested registration orders retain added filters. Destructive rewrites
  before Raffinert that remove the original subtree fail safely; register Raffinert first.
- In helper mode, the factory records the newly created QueryContext before expansion.
  An absent recorded context leaves the query unchanged; extracted captured markers then
  fail with the existing resolution diagnostic. Discarding this decorator is unsupported.
  Ordinary queries, direct operators and stable closed compiled wrappers can legitimately
  work without extracted-wrapper state, so absence alone is not an unconditional error.
  Standalone interception still supports constant targets, and cannot recover extracted
  captured targets. Tests cover failure/cancellation followed by valid queries and
  sequential deferred enumeration; concurrent operations on one context remain unsupported.

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

### SQL Server LocalDB

A separate Windows-only project executes the real Microsoft SQL Server provider
10.0.11 on net10.0. It verifies bound captures and readable names, 25-value cache
reuse, A → B → A bindings, nullable values, nested composition/getters, repeated
captures, EF-name collisions, wrapper reassignment, diagnostic protection,
compiled-query restrictions, context isolation and recovery after cancellation/failure.
The production adapter has no SQL Server dependency.

Run explicitly on Windows with LocalDB installed and the current user's instance running:

```powershell
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.csproj -c Release
```

The project is intentionally outside `Raffinert.Expressions.slnx`; the dedicated
Windows CI job fails if prerequisites are unavailable. Each fixture verifies
`master`, creates a unique disposable database, and deletes only its owned database.
An optional `RAFFINERT_LOCALDB_MASTER_CONNECTION` must point to a private
`(localdb)\Instance` under the current Windows user using integrated authentication;
remote/shared instances, attached files, credentials and failover servers are rejected.
The supplied catalog is replaced with `master` for prerequisite checks and then
with the generated fixture database. Default `Encrypt=False` is for ephemeral local
tests, not production guidance.

See [LocalDB evidence](pr7-sqlserver-localdb-validation.md). This coverage does not
certify SQL Server EF 7–9, Azure SQL, Linux SQL Server or all server collations/versions.
