[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

# Raffinert.Expressions

[![NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.svg)](https://www.nuget.org/packages/Raffinert.Expressions)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Raffinert.Expressions.svg)](https://www.nuget.org/packages/Raffinert.Expressions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Raffinert.Expressions** is a lightweight expression-composition library for reusable conditions and projections. Compose normal C# expression objects and hand the resulting pure expression tree to EF Core or any other LINQ provider—without a custom query provider or special query interception.

It combines the focused APIs of [Raffinert.Spec](https://github.com/Raffinert/Raffinert.Spec) and [Raffinert.Proj](https://github.com/Raffinert/Raffinert.Proj) around one expression-expansion engine. The runtime package targets `netstandard2.0`, has no EF Core dependency, and requires neither a custom `IQueryProvider` nor `AsExpandable()`.

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
dotnet add package Raffinert.Expressions.EntityFrameworkCore
```

This adapter targets `net6.0`, depends on core and EF Core 7.0.20 or later, and does not depend on QuerySyntax.
All three packages expose their public API in the `Raffinert.Expressions` namespace.

## 30-second example

```csharp
using Raffinert.Expressions;

var total = Projection<Order>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Condition<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var projection = Projection<Order>.Create(order => new OrderDto
{
    Id = order.Id,
    Total = total.Invoke(order),
    IsExpensive = expensive.Invoke(order)
});

var rows = await db.Orders
    .Where(expensive)
    .Select(projection)
    .ToArrayAsync();
```

The provider receives the equivalent of:

```csharp
order => new OrderDto
{
    Id = order.Id,
    Total = order.Lines.Sum(line => line.Price * line.Quantity),
    IsExpensive = order.Lines.Sum(line => line.Price * line.Quantity) > 1000m
}
```

The library produces this tree by replacing each `.Invoke(...)` call on a condition or projection
with the referenced expression body.

## Conditions

Create a condition inline or subclass `Condition<T>` and override `GetExpression()`:

```csharp
var inStock = Condition<Product>.Create(product => product.Stock > 0);
var visible = inStock.And(product => !product.IsHidden);
var wanted = visible & !Condition<Product>.Create(product => product.IsDiscontinued);

bool matches = wanted.Invoke(product);
```

`And`, `Or`, `Not`, `&`, `|`, `!`, `&&`, and `||` build direct Boolean expression nodes with correctly rebound parameters.

For a dynamic chain, `Start()` lets the first `And` or `Or` supply the condition without adding a Boolean
seed to the expression tree:

```csharp
var condition = Condition<Product>.Start();
foreach (var keyword in keywords)
    condition = condition.Or(product => product.Name.Contains(keyword));

var query = db.Products.Where(condition);
```

An unchanged starter evaluates to `false`. Use `Start(defaultWhenEmpty: true)` when an empty chain
should evaluate to `true`.

Conditions remain immutable, so retain the result of each composition call. `Condition<T>.True` and
`Condition<T>.False` remain ordinary unconditional conditions; they do not skip the first composition.

## Projections

When the result is an anonymous type, specify only the source type; the result type is inferred from the
lambda:

```csharp
var summary = Projection<Product>.Create(product => new
{
    product.Name,
    Total = product.Price * 2
});
```

```csharp
var summary = Projection<Product, ProductSummary>.Create(product => new ProductSummary
{
    Id = product.Id,
    Name = product.Name
});

ProductSummary one = summary.Invoke(product);
```

Both wrappers work directly with in-memory and queryable sequences:

```csharp
var memoryRows = products.Where(inStock).Select(summary).ToArray();
var databaseRows = await db.Products.Where(inStock).Select(summary).ToArrayAsync();
```

## Cross-composition with `Invoke`

`Invoke` is the common composition marker. A `Condition` can be embedded in a `Projection`, a `Projection` can be embedded in a `Condition`, and mixed chains can be nested to any practical depth. `GetExpandedExpression()` recursively substitutes the referenced lambda body at the call site.

`Invoke` is the only execution and composition primitive. Method groups such as `items.Any(condition.Invoke)` and `items.Select(projection.Invoke)` are expanded too.

## `Then` composition

Forward composition is available from projections:

```csharp
Projection<Order, Customer> customer = ...;
Projection<Customer, string> customerName = ...;
Condition<Customer> active = ...;

Projection<Order, string> orderCustomerName = customer.Then(customerName);
Condition<Order> activeCustomerOrder = customer.Then(active);
```

`Then` performs parameter substitution immediately; it does not introduce delegate invocation nodes.

## Structural adaptation

An existing condition or projection can be adapted to types with compatible public members; no separate
template object is required:

```csharp
Condition<InventoryItem> inventoryFilter = productFilter.AdaptSource<InventoryItem>();

Projection<InventoryItem, InventoryDto> inventoryProjection =
    productProjection.Adapt<InventoryItem, InventoryDto>();
```

`AdaptSource<TNewSource>()` rebinds parameter-rooted property and field paths by name. Projections also provide
`AdaptResult<TNewResult>()` and `Adapt<TNewSource,TNewResult>()`. Result adaptation supports parameterless member
initializers and recursively adapts nested member initializers and compatible conditional branches. Missing,
ambiguous, inaccessible, and incompatible members fail descriptively when adaptation is requested.

## Null-safe projections

Use `InvokeOrDefault` only where a null input should produce `default(TOut)`:

```csharp
var category = Projection<Category>.Create(value => new CategoryDto { Name = value.Name });
var product = Projection<Product>.Create(value => new ProductDto
{
    Category = category.InvokeOrDefault(value.Category)
});
```

Expansion produces a normal conditional expression. Ordinary `Invoke` does not add an implicit null check. For value-type outputs the default is a value such as `0` or `false`, not null.

## Merge bindings

`MergeBindings` overlays compatible member-initializer projections. Duplicate members use `BindingConflictBehavior.UseLast` by default; `UseFirst` and `Throw` are available explicitly. Conditional branch bindings are matched by member identity rather than position.

```csharp
var result = basis.MergeBindings(overrides); // overrides win
```

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
before EF Core translates the query. The optional EF Core adapter can expand an entire query during compilation.

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

These operators consume `IComposableExpression<TSource,TResult>`. Its covariant result type allows sequence
projections returning concrete types such as `List<T>` or arrays to be passed directly to `SelectMany` without an
`AsEnumerable()` conversion. `ComposableExpression<TSource,TResult>` remains the supported base class for custom
semantic wrappers and owns expansion and compiled-delegate caching; custom wrappers should inherit it rather than
implementing the consumption interface directly.

Operators requiring binary or indexed expressions, such as join result selectors and indexed predicates, are not
represented by the unary `IComposableExpression<TSource, TResult>` contract. Pass ordinary expressions to those
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
var row = Projection<Product>.Create(product => new ProductRow
{
    Name = product.Name,
    IsVisible = condition.Invoke(product)
});

var query = db.Products.Select(row);
```

### Optional EF Core integration

Install `Raffinert.Expressions.EntityFrameworkCore` for predicate-consuming async methods:

```csharp
using Raffinert.Expressions;
using Microsoft.EntityFrameworkCore;

var expensive = Condition<Order>.Create(x => x.TotalCents >= 10_000);
bool exists = await db.Orders.AnyAsync(expensive);
```

All ten async condition operators expand before calling EF and work without registration.
Existing EF `ToListAsync()` and `ToArrayAsync()` already work when the query is expanded.

For invocation markers inside ordinary EF LINQ lambdas, opt in when configuring the context:

```csharp
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection) // Keep an in-memory SQLite connection open throughout its use.
    .UseRaffinertExpressions()
    .Options;

var rows = await db.Orders
    .Where(x => expensive.Invoke(x))
    .Select(x => new { x.Id, IsExpensive = expensive.Invoke(x) })
    .ToListAsync();
```

Create `db` using those options. Registration is local to the configured contexts and uses public EF APIs.
The adapter preserves extracted wrapper values and generates the provider's cache key from the expanded
query, so changing a captured wrapper or a scalar inside it cannot reuse an obsolete predicate.
This opt-in interceptor uses constant-snapshot mode: newly inlined scalar closure values become SQL
constants. Changing them creates new EF cache keys and SQL shapes, potentially fragmenting both query
and database plan caches. Keep frequently changing values in the outer LINQ lambda or use direct
wrapper overloads to retain EF parameterization. Hidden captured collections require direct operators.
Native wrappers accessed through `IComposableExpression<,>` interfaces also expand.

SQLite execution tests verify the same EF 7-compiled adapter on EF **7.0.20**, **8.0.31**, **9.0.20** and
**10.0.11**. EF 7 / .NET 6 are retired compatibility baselines. Compiled EF queries support stable closed
wrappers and scalar delegate parameters; wrapper delegate parameters and changing closed wrappers are unsupported.
See [EF Core integration](docs/efcore-integration.md) for examples, caching details and limitations.

### LINQ query syntax

Install `Raffinert.Expressions.QuerySyntax`, then call `AsRaffinertQuery()` once at the query source to expand
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

Without `AsRaffinertQuery()` or the optional EF interceptor, invocation markers inside ordinary provider-facing
query-syntax lambdas are not expanded. Direct invocation in query syntax works normally for in-memory `IEnumerable<T>` sequences because no
LINQ provider needs to translate the expression.

### Trade-off

Libraries such as LINQKit can wrap the query provider and expand reusable expressions anywhere in the complete
query. Raffinert instead uses explicit expansion boundaries. Core method overloads expand one supplied condition
or projection; the optional `AsRaffinertQuery()` facade expands compiler-created query-syntax lambdas clause by
clause while delegating to the original provider. This keeps both packages provider-independent and the final
expression tree directly inspectable.

The separate EF Core adapter adds async predicate consumers and optional whole-query expansion at compilation.
The core and QuerySyntax packages keep their existing provider-independent boundaries.

Expansion only performs expression composition. Every node remaining in the expanded expression must still be
supported by the selected LINQ provider.

SQLite integration tests cover nested and cross-composed conditions/projections, method and query syntax,
asynchronous materialization, ordering, condition consumers, joins, grouping, flattening, `Then`, structural
adaptation, null-safe mapping, and merged member initializers.

## Runtime execution

`Invoke` and `InvokeOrDefault` execute a lazily compiled expanded expression. Expanded expressions, compiled delegates, and map-to-existing actions are cached per immutable wrapper instance. `GetExpression()` implementations are expected to remain stable after expansion or compilation is first requested.

`MapToExisting` updates assignments in an existing member-initialized destination. Existing nested destinations are updated in place, while missing writable nested destinations are created from the projection. Mutable collection members (`ICollection<T>` and `IList`) follow AutoMapper-style replacement semantics: the existing collection instance is preserved, cleared, and refilled with the projected elements; a null projected collection clears it to empty, and a missing writable collection is created. Arrays, writable members exposed only as `IEnumerable<T>`, and known read-only collection wrappers are replaced. Collection items are not matched or updated by key. If the root destination is null, `MapToExisting` creates it with the normal projection.

## Migration

The aggregate package deliberately uses one canonical invocation vocabulary. Replace `IsSatisfiedBy` and `Map` with `Invoke`, and replace `MapIfNotNull` with `InvokeOrDefault`. Rename `Spec<T>` to `Condition<T>`, `Proj<TIn,TOut>` to `Projection<TSource,TResult>`, and the public `Expr<TIn,TOut>` base to `ComposableExpression<TSource,TResult>`.

See the [migration guide](https://github.com/Raffinert/Raffinert.Expressions/blob/main/docs/migration.md) for details.

## Examples

- [LINQKit comparison](examples/LinqKitComparison/README.md) — the LINQKit README scenarios implemented three
  ways in a .NET 10 / EF Core 10 console app: plain EF Core lambdas, LINQKit, and Raffinert.Expressions.

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
