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
on netstandard2.0; QuerySyntax stays on netstandard2.1. The EF adapter requires
**net10.0 and EF Core 10.x**, built against 10.0.11. EF Core 7/8/9 and older
consumer frameworks are unsupported. Install it alongside an EF10 provider:

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11
```

Use .NET 10. Both EF Core/Relational dependencies use the bounded NuGet range
`[10.0.11,11.0.0)`: >= 10.0.11 and < 11.0.0. Compatible EF10 patches are allowed;
EF11 is excluded from the dependency contract and remains unverified.
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
    public string Name { get; set; } = "";
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
matching normal EF, outer captures and direct operators on SQLite and LocalDB EF10.
Null parameter optimization may change executed SQL (for example to IS NULL) without
changing the normalized EF compiled-query key.

EF extracts its original parameters before querying its cache. The helper decorates
public IQueryContextFactory and ICompiledQueryCacheKeyGenerator services, resolves
extracted wrappers and calls the existing core inliner once per execution. Supported
late captures receive deterministic, collision-free parameter names and values on that
execution's QueryContext. The provider's original key generator receives the normalized
parameterized expression, never the capture values. On a miss, interception consumes
that same prepared expression; on a hit, fresh values are already bound.

The adapter constructs public EF10 `QueryParameterExpression` nodes and binds values
through `QueryContext.Parameters` directly. No version probes or reflected EF APIs remain.

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

### Explicit EF directives

Ordinary queries registered with `UseRaffinertExpressions()` preserve native EF10
`EF.Constant` and `EF.Parameter` modes inside late-expanded wrappers:

```csharp
var minimum = 1000;
var maximum = 4;
var condition = Condition<OrderRow>.Create(x =>
    x.TotalCents > EF.Constant(minimum) && x.Id < EF.Parameter(maximum));
var query = db.Orders.Where(x => condition.Invoke(x));
await query.ToArrayAsync();
minimum = 2000;
maximum = 5;
await query.ToArrayAsync(); // current constant and current bound parameter
```

Supported directive operands are scalar captured member chains, scalar literals
and defaults. Literal `EF.Parameter(1000)` binds under `__raffinert_p_0`;
captured operands keep readable names. Captured A → B → A, nullable value → null
→ value, mixed modes, nested wrapper composition and wrapper reassignment are
tested against native direct EF controls on SQLite and real SQL Server LocalDB.
Embedded `Projection<T>` with a projected Boolean comparison using either directive
also matches native EF10 controls on SQLite and LocalDB across A → B → A, including
current bindings/forced constants and parameter-guarded/constant-only diagnostics.
Already lifted operands are left to native EF normalization; literal/default
operands receive a native parameter node/binding before normalization.

`EF.Constant` intentionally renders its value in SQL and has no DbParameter.
The automatic capture privacy guarantee and ToQueryString guard do not redact
these requested constants; a constant-only query can render normally. Queries
with actual lifted bound parameters still fail before ToQueryString renders values.

Inside late-expanded directives, row-dependent, computed, conversion-wrapped,
method/constructor and nested directive operands are rejected with a sanitized
error before traversal/SQL. Unsupported computed operands do not read getters.
Native EF also rejects nested directive operands; nested wrappers are supported.
Direct `.Where(condition)` expands before native extraction and retains EF's
broader client-evaluatable operand support. No generic evaluator is provided.
Embedded literal directives need ordinary per-execution preparation and are
unsupported in standalone interception or explicitly compiled wrappers.

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

See [acceptance tests](../tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/RuntimeParameterLiftingTests.cs).

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
  EF7/8/9 are unsupported; Azure SQL, other SQL Server versions/collations and
  other providers remain unverified.
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

| EF Core / provider version | Consumer framework | Execution tests |
| --- | --- | --- |
| 10.0.11 / SQLite | net10.0 | 149 passed |
| 10.0.11 / SQL Server LocalDB (Windows) | net10.0 | 44 passed |

The release-hardening solution run passed 230 tests; the separate Windows-only suite passed 44, all with
zero failures/skips. Counts include 21 explicit-directive cases per provider and
8 LocalDB fixture safety cases. The single adapter is compiled against EF10.
EF7/8/9 compatibility projects and CI lanes have been removed; historical results
do not establish current support. Other providers/future major versions are not certified.

The [EF10 source checkpoint CI](https://github.com/Raffinert/Expressions/actions/runs/38065677880)
passed all five jobs: Windows/Linux solution verification, Windows/Linux isolated
nupkg consumers, and real Windows LocalDB. The latest exact-head CI result is
available in the [CI workflow](https://github.com/Raffinert/Expressions/actions/workflows/ci.yml);
select the run for the current commit.

Run the cross-platform checks from the repository root:

```shell
dotnet restore Raffinert.Expressions.slnx
dotnet build Raffinert.Expressions.slnx -c Release --no-restore
dotnet test Raffinert.Expressions.slnx -c Release --no-restore
dotnet format Raffinert.Expressions.slnx --no-restore --verify-no-changes
```

### SQL Server LocalDB

A separate Windows-only project executes the real Microsoft SQL Server provider
10.0.11 on net10.0. It verifies bound captures and readable names, 25-value cache
reuse, A → B → A bindings, nullable values, nested composition/getters, repeated
captures, EF-name collisions, wrapper reassignment, diagnostic protection,
compiled-query restrictions, context isolation and recovery after cancellation/failure.
Explicit-directive cases compare embedded conditions and projections with native EF controls.
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

Master connectivity has a 30-second timeout. The fixture verifies that its generated
`Raffinert_EfCoreTests_` database does not already exist before creating it. The prefix
is shared by generation and the anchored ownership guard, which requires exactly
32 lowercase hexadecimal GUID characters. Disposal checks
the generated name/catalog and deletes through a separate cleanup context, including
partial initialization failures without masking their original error. Local validation
found zero leftover fixture databases. Recorders normalize `DBNull` to null, assert
real SqlClient parameters and keep values separate from SQL; values are never printed.

EF7–9 are unsupported.
This coverage does not certify Azure SQL, Linux SQL Server or all server collations/versions.

## Validation evidence and implementation history

This section condenses the former integration-validation, remediation, runtime-lifting,
parameter-naming, LocalDB and EF10-only reports. Their detailed phase logs remain in
[Git history](https://github.com/Raffinert/Expressions/tree/14eb9f14f73b99e4dde1fa6d10e760d1ecdd6dd5/docs).
Historical snapshot behavior and EF7–9 results describe earlier implementations;
they are not the current support contract.

### Why preparation precedes cache lookup

An interceptor-only prototype failed to resolve captured wrappers because EF had
already extracted them into query parameters. An evaluation-filter experiment still
parameterized the enclosing closure. Decorating the public context factory restores
those execution values; decorating the provider's cache-key generator prepares the
expanded tree before lookup. `QueryExecutionState` hands that exact tree to compilation
on misses and supplies fresh values on hits. Scoped/ephemeron ownership, invalidation
on each new execution and consumption after compilation prevent stale state across
failures, contexts and pooled leases. The singleton interceptor stores no execution data.

The first snapshot implementation produced 25 compilations and 25 SQL shapes for
25 captured thresholds. Runtime-lifting acceptance tests reproduced captured string
literals and missing DbParameters; a native-node prototype proved safe late binding
before the production fix. Current tests assert one compilation and one executed shape
for embedded, ordinary EF, outer-capture and direct async query forms over 25 values
plus a repeat. This measures EF compilation/commands, not database plan-cache behavior.

The EF10 migration removed `Major`, `ValuesProperty`, `ParameterType`,
`ParameterConstructor`, `AddParameterMethod`, `NameProperty` and their reflected
version probes. Native construction/name access and `QueryContext.Parameters.Add`
replace them; generic `ParameterExpression.Name` recognition remains. Expression
`MemberInfo` inspection is still required for capture discovery and naming.
See the public [parameter node](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/QueryParameterExpression.cs),
[extraction pipeline](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs)
and [directive normalizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs).
These sources explain behavior; no private EF API is called/copied.

### Test-first findings

The initial condition directive suite passed native controls on both providers; embedded
queries passed 9 of 13 cases and failed four. The final suite has 19 cases per
provider at the EF10-only checkpoint, all passing to the scope below, without skips
or client filtering. Two later projection regressions passed immediately on both
providers; no production expansion change was necessary.

| Scenario | Initial finding | Final SQLite / LocalDB evidence |
| --- | --- | --- |
| Automatic and captured Constant/Parameter, A → B → A | Already passed | Native results match; current bindings or requested constants |
| Literal Constant / Parameter | Invalid cast during late normalization | Native results match after narrow native-node lifting |
| Mixed modes, nested wrappers, outer-wrapper replacement | Passed after correcting a cached-inner-wrapper test assumption | Independent modes/current results; stable core structure retained |
| Nullable modes, value → null → value | Already passed | Native results match; null may alter executed SQL |
| Direct wrapper operators | Already passed | Before-extraction semantics retained |
| Private string parameter / constant-only ToQueryString | Already passed | Bound value absent from SQL; constant-only rendering allowed |
| Row-dependent operands (both modes) | Embedded invalid cast; native rejected | Sanitized rejection before SQL |
| Nested directive operands (four combinations) | Native rejected | Native and adapter reject; nested wrappers remain supported |
| Computed operand getter | Read before rejection | Unsupported operand rejected without reading getter |
| Supported getters / failure diagnostics | Added regression | One read per occurrence/execution; failure detail sanitized |
| Parameter/Constant in embedded projections, A → B → A | Passed immediately in hardening tests | Projected rows match native controls; current mode and diagnostics retained |

Captured automatic, Constant and Parameter A → B → A tests each observed two combined
native/embedded compilations, one per query form, on both providers. Forced constants
match native SQL with no DbParameters. Ordinary 25-value tests enforce one compilation.
Executed parameter shape on SQL Server (SQLite uses quoted identifiers):

```sql
SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[TotalCents] > @__raffinert_threshold_0
```

Recorders separately assert current synthetic bindings and normalized logical names.
Nested `settings.MinPrice` tests observe three reads across A → B → A; repeated
captures get distinct parameters. Nine generator cases cover metadata-only naming,
ASCII/truncation/collisions, and four integration regressions cover actual bindings,
privacy, nullable values and getter counts. Default diagnostics, sanitized exceptions
and guarded ToQueryString contain no private captured values.

Earlier remediation also reproduced interface/cast marker failures, missing
DateOnly/TimeOnly normalization and hidden-collection diagnostics. Fixes retained the
single core visitor, cycle detection and unrelated-method safety. Regression coverage
includes all ten async terminals/overload resolution, null/default relationships,
server composition, wrapper/context isolation, forwarding decorators in either order,
interceptors, deferred execution, pooled contexts/factories and cancellation/failure
recovery. Unsupported compiled captures fail before reading getters; stable closed
compiled wrappers and scalar delegate controls execute successfully.

See the [canonical tests](../tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests),
[directive comparisons](../tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/ExplicitEfParameterizationTests.cs)
and [LocalDB tests/fixture](../tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests).

### Package and CI verification

Local validation used SDK 10.0.401/runtime 10.0.12 on Windows, with LocalDB
SQL Server 2025 CU3 engine 17.0.4025.3; CI confirmed the same engine/runtime.
Release-hardening solution: **230 passed** (62 core, 5 QuerySyntax, 14 existing integration,
149 adapter). Separate LocalDB: **44 passed** (23 existing + 21 directive cases,
including 8 fixture safety cases). No failures/skips. Release build completed
without warnings/errors; solution/separate-project formatting and diff checks passed.

All three 1.2.0 nupkg/snupkg builds passed. ZIP/nuspec inspection confirmed:

| Package | Library TFM | Dependency versions/ranges |
| --- | --- | --- |
| Core | netstandard2.0 | None |
| QuerySyntax | netstandard2.1 | Core 1.2.0 |
| EF adapter | net10.0 | Core 1.2.0; EF Core and Relational [10.0.11,11.0.0) |

README, XML docs, release/repository metadata and symbols are included. The isolated
EF10 consumer has no ProjectReferences, maps Raffinert packages exclusively to the
new local feed and restores into a fresh cache. Both Windows/Linux CI consumers
passed privacy, readable names, 25-value cache reuse, A → B → A and directive checks.
Linux verification also uploaded all three packages and symbol packages; Windows
LocalDB uploaded TRX and passed its separate formatting check.

```powershell
foreach ($name in @('Raffinert.Expressions', 'Raffinert.Expressions.QuerySyntax', 'Raffinert.Expressions.EntityFrameworkCore')) {
    dotnet pack "src/$name/$name.csproj" -c Release --output artifacts/local-feed
    if ($LASTEXITCODE -ne 0) { throw 'Package build failed.' }
}
$cache = Join-Path $env:TEMP ('raffinert-ef10-' + [guid]::NewGuid().ToString('N'))
dotnet restore tests/Raffinert.Expressions.EntityFrameworkCore.PackageSmoke/PackageSmoke.csproj "-p:RestorePackagesPath=$cache"
if ($LASTEXITCODE -ne 0) { throw 'Consumer restore failed.' }
dotnet run --project tests/Raffinert.Expressions.EntityFrameworkCore.PackageSmoke/PackageSmoke.csproj -c Release --no-restore "-p:RestorePackagesPath=$cache"
if ($LASTEXITCODE -ne 0) { throw 'Consumer execution failed.' }
```

### Historical checkpoints

| Stage | Recorded evidence at that stage |
| --- | --- |
| Initial core/QuerySyntax baseline, `8c8c9ad` | 64 solution tests; no adapter yet |
| Review remediation, `70d612b` | 164 solution / 83 adapter per EF major; interface, scalar and service fixes; [CI](https://github.com/Raffinert/Expressions/actions/runs/38045212951) |
| Runtime lifting, `8cfaf21` | 196 solution / 115 adapter per EF major; secure bindings/shared preparation; [CI](https://github.com/Raffinert/Expressions/actions/runs/38060364691) |
| Readable naming, `40cd334` | 209 solution / 128 adapter per EF major; [CI](https://github.com/Raffinert/Expressions/actions/runs/38061591008) |
| LocalDB baseline, `3711497` | 209 solution + 23 LocalDB; [CI](https://github.com/Raffinert/Expressions/actions/runs/38063914180) |
| EF10-only source, `0916e8a` | 228 solution + 42 LocalDB; all five current jobs passed; [CI](https://github.com/Raffinert/Expressions/actions/runs/38065677880) |
| Release-hardening baseline, `7ec5dd8` | 228 solution + 42 LocalDB reproduced before edits; [CI](https://github.com/Raffinert/Expressions/actions/runs/38066194497) |

Earlier EF7/8/9/10 matrix results used one EF7-built assembly and are historical only.
The current adapter supports EF10 and retains automatic runtime lifting. Migration
checkpoints, changed/deleted files and commit subjects are available in the
[migration comparison](https://github.com/Raffinert/Expressions/compare/3711497019fe6a24522060b23ba14f815c590a68...feature/efcore-integration).
Check the CI workflow for the exact current commit. Version remains 1.2.0;
release validation does not merge changes, publish packages or modify user planning files.
