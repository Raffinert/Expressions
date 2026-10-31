[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

# Stop duplicating query logic. Start composing it.

**Raffinert.Expressions** turns reusable expression trees into first-class, composable building blocks. Define business logic once, combine it independently of LINQ, execute it in memory, or pass the resulting expression tree to your LINQ provider.

A single `Expr<TSource, TResult>` represents reusable expressions of any result type—from Boolean predicates to scalar selectors and object projections. It supports composition, nested reuse, in-memory execution, LINQ integration, and expression-tree expansion.

It combines the focused APIs of [Raffinert.Spec](https://github.com/Raffinert/Raffinert.Spec) and [Raffinert.Proj](https://github.com/Raffinert/Raffinert.Proj) around one expression-expansion engine. The runtime package targets `netstandard2.0`, has no EF Core dependency, and requires neither a custom `IQueryProvider` nor `AsExpandable()`.

[![Core NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.svg?label=Core)](https://www.nuget.org/packages/Raffinert.Expressions)
[![QuerySyntax NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.QuerySyntax.svg?label=QuerySyntax)](https://www.nuget.org/packages/Raffinert.Expressions.QuerySyntax)
[![EF Core NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.EntityFrameworkCore.svg?label=EF%20Core)](https://www.nuget.org/packages/Raffinert.Expressions.EntityFrameworkCore)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Raffinert.Expressions.svg)](https://www.nuget.org/packages/Raffinert.Expressions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

### Packages

**Using EF Core 10? You do not need the QuerySyntax package.** Install `Raffinert.Expressions.EntityFrameworkCore` and configure `UseRaffinertExpressions()`. Both ordinary LINQ method syntax and standard C# query syntax work without `AsRaffinertQuery()`.

| Package | Purpose |
|---|---|
| [Raffinert.Expressions](https://www.nuget.org/packages/Raffinert.Expressions) | Unified Boolean/value expressions, composition, expansion and direct LINQ operators. |
| [Raffinert.Expressions.EntityFrameworkCore](https://www.nuget.org/packages/Raffinert.Expressions.EntityFrameworkCore) | EF Core 10 async condition operators and opt-in expansion throughout ordinary EF queries. |
| [Raffinert.Expressions.QuerySyntax](https://www.nuget.org/packages/Raffinert.Expressions.QuerySyntax) | Independent query-syntax facade via the explicit `AsRaffinertQuery()` call. |

The Core package is included transitively when installing either optional integration package. The EF adapter does not depend on QuerySyntax. Merely referencing QuerySyntax does not activate its facade.

## Installation

```shell
dotnet add package Raffinert.Expressions
```

The core package targets `netstandard2.0` and contains pure expression composition plus LINQ method-style
extensions. To opt into the C# query-syntax facade and its provider-independent async materializers, add the
`netstandard2.1` satellite package instead (it brings in the core package transitively):

```shell
dotnet add package Raffinert.Expressions.QuerySyntax
```

For EF Core async condition overloads and optional expansion in ordinary LINQ lambdas, add:

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore --version 2.0.0
```

This adapter requires **.NET 10**, EF Core and Relational **`[10.0.11,11.0.0)`**, depends on core,
and does not depend on QuerySyntax.
All three packages expose their public API in the `Raffinert.Expressions` namespace.

## Define a calculation once, reuse it everywhere

```csharp
using Raffinert.Expressions;

var total = Expr<Order>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Expr<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var summary = Expr<Order>.Create(order => new OrderDto
{
    Id = order.Id,
    Total = total.Invoke(order)
});

var rows = await db.Orders
    .Where(expensive)
    .Select(summary)
    .ToListAsync();
```

`total` is defined once: `expensive` reuses it in a condition and `summary` reuses it in a projection. Raffinert's direct LINQ overloads expand nested invocations before passing the query to the provider, replacing each marker with the referenced expression body.

These examples use illustrative models. Async database materialization uses your provider's extension methods (for EF Core, `using Microsoft.EntityFrameworkCore;`). Every expanded expression must still be supported by that provider.

## Boolean expressions

Conditions form an independent algebra, outside LINQ:

```csharp
var active = Expr<Order>.Create(o => o.IsActive);
var expensive = Expr<Order>.Create(o => o.Total > 1000m);
var urgent = Expr<Order>.Create(o => o.IsUrgent);

var eligible = active.And(expensive).Or(urgent);

bool accepted = eligible.Invoke(order);
var query = db.Orders.Where(eligible);
```

The same composable object can evaluate an in-memory order or filter a database query.

Create a Boolean expression inline or subclass `Expr<T, bool>` and override `GetExpression()`:

```csharp
var inStock = Expr<Product>.Create(product => product.Stock > 0);
var visible = inStock.And(product => !product.IsHidden);
var wanted = visible.And(Expr<Product>.Create(product => product.IsDiscontinued).Not());

bool matches = wanted.Invoke(product);
```

`And`, `Or`, and `Not` build direct Boolean expression nodes with correctly rebound parameters.

For dynamic Boolean composition, use the mutable `PredicateBuilder<T>`. `Create()` skips any Boolean
seed for the first `And` or `Or`, and `Build()` produces an independent reusable `Expr<T,bool>`:

```csharp
var builder = PredicateBuilder<Product>.Create();
foreach (var keyword in keywords)
    builder.Or(product => product.Name.Contains(keyword));

var predicate = builder.Build(); // Balanced by default.
var query = db.Products.Where(predicate);
```

An empty `Create()` builds `false`; use `Create(defaultWhenEmpty: true)` for `true`.
To create a standalone constant predicate, use `Expr<T>.Create(_ => true)` or
`Expr<T>.Create(_ => false)` directly, without a builder. `Not()` negates the
entire current builder composition. Use `Build(balanced: false)` when the original left-associative
tree is required. Balancing changes only builder-owned homogeneous `AndAlso`/`OrElse`
groups, preserving operand order and short-circuiting. Builder mutations are not thread-safe
and do not affect expressions already returned by `Build()`.

Standalone `IExpr<T,bool>.And/Or/Not` calls continue to return new immutable expressions; they
do not mutate the original wrappers.

## Value and object projections

When the result is an anonymous type, specify only the source type; the result type is inferred from the
lambda:

```csharp
var summary = Expr<Product>.Create(product => new
{
    product.Name,
    Total = product.Price * 2
});
```

```csharp
var summary = Expr<Product, ProductSummary>.Create(product => new ProductSummary
{
    Id = product.Id,
    Name = product.Name
});

ProductSummary one = summary.Invoke(product);
```

The same expression abstraction works directly with in-memory and queryable sequences:

```csharp
var memoryRows = products.Where(inStock).Select(summary).ToArray();
var databaseRows = await db.Products.Where(inStock).Select(summary).ToArrayAsync();
```

## Cross-composition with `Invoke`

`Invoke` is the common composition marker. Boolean and value-valued composable expressions can be embedded in one another to any practical depth. `GetExpandedExpression()` recursively substitutes the referenced lambda body at the call site.

`Invoke` is the shared invocation and inlining primitive. Method groups such as `items.Any(condition.Invoke)` and `items.Select(projection.Invoke)` are expanded too.

## `Then` composition

Forward composition is available from projections:

```csharp
Expr<Order, Customer> customer = ...;
Expr<Customer, string> customerName = ...;
Expr<Customer, bool> active = ...;

Expr<Order, string> orderCustomerName = customer.Then(customerName);
Expr<Order, bool> activeCustomerOrder = customer.Then(active);
```

`Then` performs parameter substitution immediately; it does not introduce delegate invocation nodes. Boolean combinators and forward composition also accept interface-typed `IExpr<TSource, TResult>` operands, including custom implementations that supply an expanded lambda. Custom `IExpr` implementations are also supported as nested `Invoke` targets; they must return a compatible one-parameter lambda from `GetExpandedLambdaExpression()`.

## Structural adaptation

An existing condition or projection can be adapted to types with compatible public members; no separate
template object is required:

```csharp
Expr<InventoryItem, bool> inventoryFilter = productFilter.AdaptSource<InventoryItem>();

Expr<InventoryItem, InventoryDto> inventoryProjection =
    productProjection.Adapt<InventoryItem, InventoryDto>();
```

`AdaptSource<TNewSource>()` rebinds parameter-rooted property and field paths by name. Projections also provide
`AdaptResult<TNewResult>()` and `Adapt<TNewSource,TNewResult>()`. Result adaptation supports parameterless member
initializers and recursively adapts nested member initializers and compatible conditional branches. Missing,
ambiguous, inaccessible, and incompatible members fail descriptively when adaptation is requested.

## Null-safe projections

Use `InvokeOrDefault` only where a null input should produce `default(TOut)`:

```csharp
var category = Expr<Category>.Create(value => new CategoryDto { Name = value.Name });
var product = Expr<Product>.Create(value => new ProductDto
{
    Category = category.InvokeOrDefault(value.Category)
});
```

Expansion produces a normal conditional expression. Ordinary `Invoke` does not add an implicit null check. For value-type outputs the default is a value such as `0` or `false`, not null.

## Configurable Order Export

`MergeBindings` overlays compatible member-initializer projections. Duplicate members use `BindingConflictBehavior.UseLast` by default; `UseFirst` and `Throw` are available explicitly. Conditional branch bindings are matched by member identity rather than position.

An order export always includes the order number and customer name. Depending on the export configuration, it may also include the net or gross total. `MergeBindings()` adds the optional calculation without duplicating the base projection:

<p align="right"><a href="https://dotnetfiddle.net/iucjOg">Run .NET fiddle</a></p>

```csharp
var netAmount = Expr<OrderLine>.Create(l =>
    l.Quantity * l.UnitPrice);

var grossAmount = Expr<OrderLine>.Create(l =>
    netAmount.Invoke(l) * (1m + l.VatRate));

var export = Expr<Order>.Create(o => new OrderExportRow
{
    Number = o.Number,
    Customer = o.Customer.Name
});

if (includeAmounts)
{
    var lineAmount = includeVat ? grossAmount : netAmount;
    var total = Expr<Order>.Create(o =>
        o.Lines.Sum(l => lineAmount.Invoke(l)));

    export = export.MergeBindings(
        Expr<Order>.Create(o => new OrderExportRow
        {
            Total = total.Invoke(o)
        }));
}

var rows = await db.Orders.Select(export).ToListAsync();
```

The line calculation is defined once in `netAmount` and reused by `grossAmount`. The selected line projection feeds a single order-level sum, so VAT remains specific to each line.

Here `Order.Lines` contains `OrderLine` objects, `OrderExportRow` has a parameterless constructor and a `decimal? Total` property, and `VatRate` is a decimal fraction such as `0.25m` for 25%.

| Configuration | Projection |
|---|---|
| `includeAmounts = false` | Common export fields only |
| `includeAmounts = true`, `includeVat = false` | Common fields + net total |
| `includeAmounts = true`, `includeVat = true` | Common fields + gross total |

When `includeAmounts` is false, the `Total` member assignment is absent from the final expanded projection. This is structurally different from assigning `null` or `0`, or inserting a row-level conditional expression. The destination property retains its default value.

## EF Core and provider compatibility

The core runtime library knows nothing about EF Core. It expands a condition or projection before passing its
ordinary expression tree to LINQ:

```csharp
var rows = await db.Products
    .Where(condition)
    .Select(projection)
    .ToArrayAsync();
```

The provided `IQueryable` overloads call `GetExpandedExpression()`. EF Core therefore receives normal expression
nodes such as member access, LINQ calls, Boolean operators, member initialization, and conditionals.

This design requires:

- no EF dependency in the runtime package;
- no custom query provider;
- no query interceptor;
- no `AsExpandable()` call;
- no runtime expression traversal while rows are processed.

### Explicit expansion boundaries

The core package expands wrappers at explicit operator boundaries. Invocation markers must be expanded
before EF Core translates the query. The optional EF Core adapter enables expansion inside ordinary query lambdas.

Pass wrappers directly to the provided LINQ overloads:

```csharp
db.Products.Where(condition);
db.Products.Select(projection);
db.Products.OrderBy(sortProjection).ThenBy(nameProjection);
db.Products.Any(condition);
db.Products.GroupBy(categoryProjection);
```

The direct condition consumers are `Where`, `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`,
`Last`, `LastOrDefault`, `Single`, `SingleOrDefault`, `SkipWhile`, and `TakeWhile`. The direct projection consumers
are `Select`, `SelectMany`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, and `GroupBy` with either
a key selector or key and element selectors. Each is available for both `IQueryable` and `IEnumerable`.

See the [core architecture](docs/architecture.md) for the shared consumption contract, result covariance and custom wrapper guidance.

Operators requiring binary or indexed expressions, such as join result selectors and indexed predicates, are not
represented by the unary `IExpr<TSource, TResult>` contract. Pass ordinary expressions to those
operators and expand wrapper arguments explicitly where needed. Numeric aggregates can be expressed without extra
overloads by selecting first:

```csharp
var total = db.Products.Select(priceProjection).Sum();
```

With the core package alone, pass the expanded expression to EF's predicate-consuming async operators:

```csharp
var exists = await db.Products.AnyAsync(condition.GetExpandedExpression());
```

With only the core package, invocation markers inside ordinary provider-facing lambdas are not expanded:

```csharp
// Not automatically expanded:
db.Products.Where(product => condition.Invoke(product));
db.Products.Select(product => projection.Invoke(product));
db.Products.OrderBy(product => sortProjection.Invoke(product));
```

Those expressions are handled by the standard LINQ operators, so EF Core sees the `Invoke` method calls
without Raffinert first expanding them. Register the optional EF Core integration below to support this form.

Invocation inside a condition or projection remains supported because the wrapper is recursively
expanded before it is passed to the provider:

```csharp
var row = Expr<Product>.Create(product => new ProductRow
{
    Name = product.Name,
    IsVisible = condition.Invoke(product)
});

var query = db.Products.Select(row);
```

### Optional EF Core integration

`Raffinert.Expressions.EntityFrameworkCore` 2.0.0 adds async composable-condition
operators and opt-in invocation expansion for ordinary EF LINQ queries. It requires
**.NET 10**, EF Core and Relational **`[10.0.11,11.0.0)`**.

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore --version 2.0.0
```

For an application with an `OrdersContext` exposing an `Orders` set:

```csharp
using Microsoft.EntityFrameworkCore;
using Raffinert.Expressions;

var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite("Data Source=orders.db")
    .UseRaffinertExpressions()
    .Options;

await using var db = new OrdersContext(options);
var expensive = Expr<Order>.Create(x => x.TotalCents >= 10_000);
var rows = await db.Orders.Where(x => expensive.Invoke(x)).ToListAsync();
```

Register `UseRaffinertExpressions()` for invocations inside ordinary EF-facing
lambdas. Passing wrappers directly, such as `.Where(expensive)` or
`await db.Orders.AnyAsync(expensive)`, needs no registration.

The adapter supports nested conditions/projections, current captured values, native
EF parameterization and explicit scalar/collection directives. SQLite and SQL Server
are tested. See the [EF Core integration guide](docs/efcore-integration.md) for a
complete quick start, supported methods, diagnostics and compatibility limits.

Standard C# query syntax compiles into ordinary LINQ operator calls. With registration, the adapter expands the complete query tree before native EF parameter extraction:

```csharp
// DbContext is configured with UseRaffinertExpressions().
var active = Expr<Order>.Create(o => o.IsActive);
var total = Expr<Order>.Create(o => o.Total);

var query =
    from order in db.Orders
    where active.Invoke(order)
    orderby total.Invoke(order) descending
    select new
    {
        order.Id,
        Total = total.Invoke(order)
    };

var rows = await query.ToListAsync();
```

**No `AsRaffinertQuery()` call and no QuerySyntax package are required here.** Ordinary method syntax such as `.Where(o => active.Invoke(o))` works with the same registration.

The adapter also provides optional async condition overloads without registration:

```csharp
bool exists = await db.Orders.AnyAsync(active, cancellationToken);
int count = await db.Orders.CountAsync(active, cancellationToken);
```

**`ToQueryString()` follows native EF behavior and may expose parameter values even when sensitive-data logging is disabled.** Avoid exposing rendered SQL containing sensitive values.

Ordinary queries observe current scalar captures. A wrapper's structure must remain stable after its first expansion; construct a new wrapper when changing nested composition. EF compiled queries support stable closed wrappers and scalar delegate parameters, but runtime captures inside wrappers and wrappers passed as delegate parameters are unsupported. Exact parameter names and universal SQL-shape stability are not API guarantees. See the [EF Core guide](docs/efcore-integration.md) for the supported contract and the [adapter architecture](docs/architecture/efcore-adapter.md) for implementation details.

### QuerySyntax as an alternative integration

The independent facade is useful for provider-independent query-syntax expansion or when using its facade without registering the EF adapter. Install `Raffinert.Expressions.QuerySyntax`, then call `AsRaffinertQuery()` once at the query source to expand
invocation markers throughout a C# query expression:

```csharp
var query =
    from product in db.Products.AsRaffinertQuery()
    where condition.Invoke(product)
    orderby sortProjection.Invoke(product), nameProjection.Invoke(product)
    select projection.Invoke(product);

var rows = await query.ToListAsync();
```

The facade supports the complete query-expression pattern: `where`, `select`, `let`, multiple `from` clauses,
joins, group joins, ordering, grouping, continuations, and explicit range-variable types. It expands each
compiler-created lambda and then calls the standard `Queryable` operator. Its `Expression`, `Provider`, and
synchronous or asynchronous enumeration are delegated to the provider query; it does not replace or intercept
`IQueryProvider`.

Apply provider-specific operators such as EF Core's `Include`, `AsNoTracking`, and temporal-query methods before
`AsRaffinertQuery()`. An operator that returns an ordinary `IQueryable<T>` leaves the facade; call
`AsRaffinertQuery()` again if later lambdas contain invocation markers.

The facade provides unambiguous `ToListAsync` and `ToArrayAsync` instance methods and targets `netstandard2.1`.
Providers that do not support asynchronous enumeration fail with a descriptive `InvalidOperationException`.

.NET Framework 4.7.2 applications use the `netstandard2.0` core package and method-style composition, then call
the async materializer supplied by their LINQ provider. For example, with EF Core 3.1:

```csharp
var rows = await db.Products
    .Where(condition)
    .Select(projection)
    .ToListAsync();
```

The query-syntax satellite cannot be referenced from .NET Framework 4.7.2 because that platform does not implement
`netstandard2.1`.

Without `AsRaffinertQuery()` or `UseRaffinertExpressions()`, invocation markers inside ordinary provider-facing
query-syntax lambdas are not expanded. Direct invocation in query syntax works normally for in-memory `IEnumerable<T>` sequences because no
LINQ provider needs to translate the expression.

### Relationship to LINQKit

LINQKit already supports `PredicateBuilder`, `Invoke`, expansion and reusable expressions. Raffinert's distinction is its unified programming model: Boolean and result-valued expressions are independently composable objects with consistent execution, expansion and LINQ consumption capabilities. Core expands at direct operator boundaries, QuerySyntax expands facade clauses, and the registered EF adapter expands ordinary EF queries.

Expansion only performs expression composition. Every node remaining in the expanded expression must still be
supported by the selected LINQ provider.

SQLite integration tests cover nested and cross-composed conditions/projections, method and query syntax,
asynchronous materialization, ordering, condition consumers, joins, grouping, flattening, `Then`, structural
adaptation, null-safe mapping, and merged member initializers.

## Runtime execution

`Invoke` and `InvokeOrDefault` execute a lazily compiled expanded expression. Expanded expressions and compiled delegates are cached per immutable wrapper instance. Map-to-existing expressions and actions are cached separately, on demand, using weak keys; plain predicates allocate no mapping cache. Member-initializer operations are extensions and validate supported expression shapes at runtime. `GetExpression()` implementations are expected to remain stable after expansion or compilation is first requested.

Use `MapToExisting` to apply a member-initializer projection to an existing destination:

```csharp
ProductSummary? destination = existingSummary;
summary.MapToExisting(product, ref destination);
```

Existing nested destinations are updated in place; missing writable destinations are created. Mutable collections are cleared and refilled, without matching items by key. A null root destination is created from the projection. See [projection transformations](docs/architecture.md#projection-transformations) for collection replacement rules and supported shapes.

## Migration

Version 2 removes `Condition<T>` and `Projection<TSource,TResult>` in favor of a single `Expr<TSource,TResult>` type, with `Expr<TSource>.Create(...)` inferring the result type. Replace condition operators with `.And()`, `.Or()` and `.Not()`. The aggregate package deliberately uses one canonical invocation vocabulary. Replace `IsSatisfiedBy` and `Map` with `Invoke`, and replace `MapIfNotNull` with `InvokeOrDefault`. Use the single `Expr<TSource,TResult>` type for both predicates (`TResult = bool`) and projections; the `Expr<TSource>.Create(...)` factory infers the result type.

See the [migration guide](https://github.com/Raffinert/Raffinert.Expressions/blob/main/docs/migration.md) for details.

## Examples

- [LINQKit comparison](examples/LinqKitComparison/README.md) — the LINQKit README scenarios implemented three
  ways in a .NET 10 / EF Core 10 console app: plain EF Core lambdas, LINQKit, and Raffinert.Expressions.

## AI Agent Skill

Use Raffinert.Expressions in your own project with a portable [Agent Skill](https://agentskills.io/specification):

```shell
npx skills add Raffinert/Expressions
```

The `raffinert-expressions` skill teaches coding agents package selection, reusable `Expr<TSource, TResult>` composition, LINQ and EF Core expansion boundaries, query syntax without unnecessary packages, and common mistakes. It is stored in [`.agents/skills/raffinert-expressions/`](.agents/skills/raffinert-expressions/) and can also be read directly as a `SKILL.md` file. This skill helps **consumers** of the library; it does not require AI to participate in your build or runtime.

## Useful reading

Expression composition and reusable query logic:

- [Expression and Projection Magic for Entity Framework Core](https://bencull.com/blog/expression-projection-magic-entity-framework-core) — reusable and nested projections, expression visitors, and SQL translation.
- [LINQKit: Combining Expressions](https://www.albahari.com/nutshell/linqkit.aspx) — `Invoke`, expression expansion, predicate composition, and `AsExpandable`.
- [Re-use EF Core Expressions to Avoid Redundant Queries](https://schwabencode.com/blog/2023/07/31/EF-Core-Expression-Reuse) — practical reusable EF Core filters.
- [Specification Pattern in C#](https://www.c-sharpcorner.com/article/specification-pattern-in-c-sharp2/) — composable expression-based business rules.
- [Specifications](https://martinfowler.com/apsupp/spec.pdf), by Eric Evans and Martin Fowler — the foundational specification, parameterized specification, and composite specification patterns using predicate-style `IsSatisfiedBy`, `And`, `Or`, and `Not` operations.

Expression-tree implementation and performance:

- [Translate expression trees](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/expression-trees-translating) — visiting and rewriting immutable expression trees.
- [Build expression trees](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/expression-trees-building) — constructing expression-tree nodes programmatically.
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics) — dynamic query construction, parameterization, and query-shape stability.

Projection and collection-mapping behavior:

- [AutoMapper Queryable Extensions](https://docs.automapper.org/en/latest/Queryable-Extensions.html) — SQL-level DTO projection and query-provider limitations.
- [AutoMapper Lists and Arrays](https://docs.automapper.org/en/stable/Lists-and-arrays.html) — clear-and-refill behavior when mapping existing collections.
- [EF Core relationship changes](https://learn.microsoft.com/en-us/ef/core/change-tracking/relationship-changes) — relationship fixup and the effects of adding or removing navigation elements.

## Related repositories

- [Raffinert.Spec](https://github.com/Raffinert/Raffinert.Spec)
- [Raffinert.Proj](https://github.com/Raffinert/Raffinert.Proj)

## Feedback

Open an [issue](https://github.com/Raffinert/Raffinert.Expressions/issues) for bugs, compatibility problems, or feature proposals.
