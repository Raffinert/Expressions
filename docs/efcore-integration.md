# EF Core integration

Raffinert owns expression expansion. EF Core owns parameter extraction, evaluation,
parameter names, binding, caching and SQL translation after expansion.

## Choosing a package

| Package | Target | Dependencies |
| --- | --- | --- |
| Raffinert.Expressions | netstandard2.0 | None |
| Raffinert.Expressions.QuerySyntax | netstandard2.1 | Core 1.2.0 |
| Raffinert.Expressions.EntityFrameworkCore | net10.0 | Core 1.2.0; EF Core/Relational [10.0.11,11.0.0) |

All public extension methods use `Raffinert.Expressions`. The EF adapter is optional;
Core and QuerySyntax remain independent of EF. SQL Server is a test-only dependency.
EF7, EF8, EF9 and EF11 are outside the adapter's support range.

## Async predicates without interception

```csharp
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

var expensive = Condition<Order>.Create(x => x.TotalCents > 1000);
bool any = await db.Orders.AnyAsync(expensive, cancellationToken);
var rows = await db.Orders.Where(expensive).ToArrayAsync(cancellationToken);
```

Direct `.Where(condition)` / `.Select(projection)` and the ten async condition
operators expand before passing the expression to EF. They need no adapter registration.
The async operators are Any, All, Count, LongCount, First, FirstOrDefault, Single,
SingleOrDefault, Last and LastOrDefault. Cancellation and native terminal semantics
are preserved; predicate-less and ordinary lambda overloads remain EF's own methods.

## Opt-in ordinary LINQ expansion

```csharp
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseRaffinertExpressions()
    .Options;

await using var db = new OrdersContext(options);
var threshold = 1000;
var expensive = Condition<Order>.Create(x => x.TotalCents > threshold);
var query = db.Orders.Where(x => expensive.Invoke(x));
var first = await query.ToArrayAsync();
threshold = 10000;
var second = await query.ToArrayAsync();
```

Call registration after the provider. Typed/untyped repeated calls are idempotent.
Registration decorates the already-installed scoped `IQueryCompiler`; it preserves
provider construction through its original type/factory descriptor. A private keyed
registration lets DI own the original service and dispose it once. Missing, duplicate,
singleton or instance compiler descriptors fail early with a descriptive error.
Tested custom compiler decorators compose in either extension-registration order.

The decorator passes the expanded tree once to the original compiler. EF then creates
its QueryContext, extracts native parameters, generates its own cache key, compiles on
miss and binds current values on every execution. No custom runtime binder, parameter
name generator, recording context factory, preparation state or cache-key decorator remains.
No EF extractor implementation is copied or called directly.

This boundary depends on EF10's **internal** `IQueryCompiler` contract. Local EF1001
exceptions cover that adapter and its contract tests. EF9100 is confined to forwarding
the experimental PrecompileQuery member. All other warnings remain errors; there is
no global NoWarn. Revalidate EF patch upgrades before adopting them.

## Expansion and caching

The existing core engine handles nested conditions/projections, typed interfaces,
wrapper delegates, casts and legal constant/closure/static-member/direct-constructor
targets. Unrelated methods named Invoke remain ordinary methods. Cycles and unresolved
wrapper parameters fail. Captured wrapper reassignment is observed in ordinary queries;
the core's established cache of a wrapper's nested composition still fixes that structure.

Native EF controls determine supported client expressions and getter evaluation.
There is no Raffinert scalar/computation/collection whitelist in ordinary queries.
Approved user code can execute during native extraction. A successful captured getter
is read once in the tested cases; EF may retry a failing reflected getter using its
compiled fallback. Native getter exceptions can retain user exception details.
Raffinert sanitizes failures while resolving wrapper targets, before delegation.

Tests separately inspect executed SQL, DbParameter values and EF compilation counts.
Twenty-five changing captured values plus a repeat reuse one compilation and one SQL
shape on SQLite and LocalDB. A -> B -> A transitions use current bindings. Identical
wrapper structures share native cache behavior; changed predicates produce current
results. EF may deduplicate repeated captures; exact generated names are not a contract.
Collection cardinality/nullability and forced constants can change executed SQL shape
independently of the compiled-query cache. No universal SQL-shape guarantee is made.

## Explicit EF directives

```csharp
var threshold = 1000;
int[] ids = [1, 3];
var predicate = Condition<Order>.Create(x =>
    x.TotalCents > EF.Parameter(threshold + 100) &&
    Enumerable.Contains(EF.MultipleParameters(ids), x.Id));
var query = db.Orders.Where(x => predicate.Invoke(x));
```

| Directive | Native behavior |
| --- | --- |
| EF.Parameter(value) | Forced parameter mode; collections use native single-collection parameter translation |
| EF.MultipleParameters(collection) | Native per-element mode, including provider/cardinality-dependent padding |
| EF.Constant(value) | Forced inline values; explicit literals can appear in SQL |

Conditions and projections preserve those EF calls. Tested cases include computed
integer/decimal/nullable scalars, client methods, arrays, mutable List<int>, nullable
arrays, empty/duplicate/null collections where native EF accepts them, mixed modes
and wrapper reassignment. Bare captured collections also use native extraction.
Raffinert generates no IN/VALUES SQL, JSON, collection padding or parameter-limit logic.
Row-dependent and nested directive operands fail as native EF does, before SQL.

Like, Property and Collate (literal/captured metadata) match native controls on both
providers; computed Like patterns also work. SQLite Glob and SQL Server DateDiffDay
have provider-specific execution tests. Property/collation names are metadata:
native EF establishes their form. Use a provider-valid collation and native-valid query.
Custom HasDbFunction mappings are not tested; no mapping fixture is available.

## SQL diagnostics

Native **ToQueryString may expose parameter values even when sensitive-data logging
is disabled**. Raffinert adds no diagnostic privacy guard. Treat rendered query strings,
client-evaluation exceptions, sensitive logging and custom telemetry accordingly.
Parameterized captures remain separate from executed command text in tested cases,
but are present in DbParameter.Value. EF.Constant explicitly requests inline SQL.
Tests verify both parameter binding and native diagnostic rendering without printing
synthetic private values. No Raffinert-specific diagnostic privacy guarantee remains.

## Limits and compiled queries

EF.CompileQuery / EF.CompileAsyncQuery retain the tested baseline: stable closed
wrappers and dynamic scalar delegate parameters work. Wrapper delegate parameters
cannot be resolved; runtime captures inside compiled wrappers are rejected before
capture getters are read. Closed wrappers are fixed when EF creates its compiled
delegate; changing them requires ordinary LINQ or a new compiled delegate.
Embedded explicit directives on the late compiled compatibility path remain unsupported.

The public RaffinertExpressionInterceptor remains available. Registered with the
helper, it is a no-op for already-expanded ordinary queries and expands closed compiled
wrappers after native compiled-query extraction. Standalone registration supports
closed constant targets, but cannot recover already-extracted ordinary wrapper captures;
use UseRaffinertExpressions for those. Its compatibility visitor only validates closed
operands and normalizes CLR defaults; it neither extracts nor binds runtime values.

Experimental PrecompileQuery forwards a no-marker expression unmodified. Raffinert
invocation expansion detected on that path fails early with NotSupportedException,
including wrapper delegates; there is no claimed NativeAOT/precompiled-query support.
Interface forwarding is tested separately from ordinary/compiled query execution.

## Tested versions

Local validation: Windows, SDK 10.0.401 (global.json 10.0.400 with latestPatch), runtime
10.0.12, EF Core/Relational/SQLite/SQL Server 10.0.11. Solution: **265 passed**
(62 core, 5 QuerySyntax, 14 original integration, 184 adapter). LocalDB: **78 passed**
(23 existing including 8 fixture-safety cases, 21 directive cases, 34 native-acceptance cases).
No failures or skips. Release builds, restore, formatting and diff checks pass.
Three 1.2.0 nupkg/snupkg artifacts retain their original TFMs and dependency boundaries.
The isolated consumer restores newly packed local packages into a fresh cache, with
no ProjectReferences, and checks captures, cache reuse, computed operands, all three
collection modes, native diagnostic behavior and Like.

The final exact-head Linux/Windows solution and packed-consumer runs, plus Windows
LocalDB run, are recorded with individual conclusions in the experiment's PR validation
section after the final report commit. See the
[native extraction experiment](experiments/ef10-native-extraction-experiment.md).
Azure SQL, other server versions/collations, custom mapped functions and NativeAOT
are not certified by these tests.

### SQL Server LocalDB

```powershell
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.csproj -c Release
```

This Windows-only project remains outside the cross-platform solution. Fixtures
require private current-user LocalDB and integrated authentication, verify master
connectivity and that the generated owned database does not exist, then create a
unique Raffinert_EfCoreTests_ database with exactly 32 lowercase hexadecimal GUID
characters. Cleanup checks exact ownership/catalog and uses a separate connection,
including partial-initialization failures. Remote/shared instances, attached files,
credentials and failover servers are rejected. Prerequisites fail rather than skip.
No LocalDB safety policy was relaxed during migration.

## Validation evidence and implementation history

This page remains the consolidated EF integration guide. Earlier validation/remediation,
runtime-lifting, naming, LocalDB and EF10-only details remain in Git history.
The late-lifting architecture at c247403 had 230 solution / 44 LocalDB passing tests.
It used scoped preparation, custom names and a prefix-based diagnostic guard. Those
are historical implementation details, superseded by the explicitly approved native
architecture and diagnostic contract. The earlier internal-API PoC stopped under its
then-stricter rules; the current owner separately authorized local EF1001 and EF9100
exceptions. See the experiment report for source references, RED/GREEN observations,
comparison, approvals, unsupported paths and final validation evidence.
