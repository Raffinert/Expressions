# EF Core integration boundaries

For documented Raffinert 2.0.0, the optional adapter targets .NET 10 and requires EF Core/Relational versions in `[10.0.11,11.0.0)`. Core is transitive; QuerySyntax is **not** required for the EF adapter.

## Direct wrappers versus ordinary lambdas

Core (no adapter registration needed for the direct overloads):

```csharp
var active = Expr<Order>.Create(o => o.IsActive);
var summary = Expr<Order>.Create(o => new OrderDto { Id = o.Id });
var rows = await db.Orders.Where(active).Select(summary).ToListAsync();
```

If an ordinary EF-facing lambda contains `Invoke`, configure the adapter **after** the database provider:

```csharp
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseRaffinertExpressions()
    .Options;
```

Then ordinary method syntax and ordinary C# query syntax work:

```csharp
var active = Expr<Order>.Create(o => o.IsActive);
var name = Expr<Order>.Create(o => o.Name);

var query =
    from order in db.Orders
    where active.Invoke(order)
    orderby name.Invoke(order)
    select new { order.Id, Name = name.Invoke(order) };

var rows = await query.ToListAsync();
```

There is **no** `AsRaffinertQuery()` here. Merely referencing the QuerySyntax package does not activate it, either. Use QuerySyntax's explicit `AsRaffinertQuery()` facade when you want its independent query-syntax expansion without registering the EF adapter.

The adapter also provides condition-taking async overloads such as `AnyAsync(active, cancellationToken)` and `CountAsync(active, cancellationToken)`, without requiring `UseRaffinertExpressions()` for those direct overloads. Provider-specific async materializers remain provider-owned.

## Native EF semantics and limitations

- Raffinert expands marker calls before EF's native parameter extraction; EF owns extraction, parameter naming/binding, query caching, and SQL translation. Captured scalar values in ordinary queries are observed at execution, including cache hits.
- Native EF directives `EF.Parameter`, `EF.Constant`, and `EF.MultipleParameters` remain native EF directives when used in supported expressions. Collection/cardinality/provider behavior follows native EF.
- EF compiled queries support stable **closed** Raffinert wrappers and scalar compiled-delegate parameters within the documented integration. Runtime captures inside compiled wrapper bodies, wrappers passed as compiled-delegate parameters, and Raffinert invocation expansion in EF precompiled-query infrastructure are unsupported.
- EF10 internal `IQueryCompiler` integration requires revalidation across EF patch upgrades. Tested providers in the repository include SQLite and SQL Server; do not claim every third-party provider is certified.
- `ToQueryString()` can include captured parameter values even when sensitive-data logging is disabled. Treat query strings, exceptions, logs, and telemetry as potentially sensitive.
- The original provider-facing `query.Expression` need not be mutated by the EF adapter; expansion occurs during query compilation. Assert **executed results and SQL** instead of requiring `query.Expression.ToString()` to contain no `Invoke`.

## Tests for real-world integration

Prefer a real provider test that exercises a nested condition/projection, executes an actual SQL query, and checks results. For query syntax without QuerySyntax, do **not** call `AsRaffinertQuery()`; configure `UseRaffinertExpressions()`. If applicable, add a negative control showing a provider translation failure without adapter registration, and a packaged consumer without a QuerySyntax dependency.

The canonical implementation contract lives in `https://github.com/Raffinert/Expressions/blob/main/docs/efcore-integration.md`; confirm details against the installed release before relying on edge cases.