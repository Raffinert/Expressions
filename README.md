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

## 30-second example

```csharp
using Raffinert.Expressions;

var total = Projection<Order, decimal>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Condition<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var projection = Projection<Order, OrderDto>.Create(order => new OrderDto
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

`Invoke` is the only execution and composition primitive. Method groups such as `items.Any(spec.Invoke)` and `items.Select(projection.Invoke)` are expanded too.

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
var category = Projection<Category, CategoryDto>.Create(value => new CategoryDto { Name = value.Name });
var product = Projection<Product, ProductDto>.Create(value => new ProductDto
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

The runtime library knows nothing about EF Core. It expands a condition or projection before passing its
ordinary expression tree to LINQ:

```csharp
var rows = await db.Products
    .Where(condition)
    .Select(projection)
    .ToArrayAsync();
```

The `Where` and `Select` overloads above call `GetExpandedExpression()`. EF Core therefore receives normal
expression nodes such as member access, LINQ calls, Boolean operators, member initialization, and conditionals.

This design requires:

- no EF dependency in the runtime package;
- no custom query provider;
- no query interceptor;
- no `AsExpandable()` call;
- no runtime expression traversal while rows are processed.

### Explicit expansion boundaries

Because Raffinert.Expressions does not wrap the query provider, it does not scan or rewrite an entire `IQueryable`
expression tree. Invocation markers must be expanded before the expression reaches EF Core.

Pass wrappers directly to the provided `Where` and `Select` overloads:

```csharp
db.Products.Where(condition);
db.Products.Select(projection);
```

For other LINQ operators, pass the expanded expression explicitly:

```csharp
db.Products.OrderBy(sortProjection.GetExpandedExpression());
db.Products.Any(condition.GetExpandedExpression());
```

Do not place invocation markers directly inside an ordinary provider-facing lambda:

```csharp
// Not automatically expanded:
db.Products.Where(product => condition.Invoke(product));
db.Products.Select(product => projection.Invoke(product));
db.Products.OrderBy(product => sortProjection.Invoke(product));
```

Those expressions are handled by the standard LINQ operators, so EF Core sees the `Invoke` method calls
without Raffinert first expanding them.

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

### LINQ query syntax

LINQ query syntax can be used for ordinary provider-translatable expressions, but Raffinert invocation markers
inside `where` and `select` clauses are not automatically expanded:

```csharp
// Not automatically expanded for IQueryable<T>:
var query =
    from product in db.Products
    where condition.Invoke(product)
    select projection.Invoke(product);
```

Query syntax can be combined with Raffinert's method-based expansion boundaries:

```csharp
var filtered =
    from product in db.Products.Where(condition)
    where product.IsActive
    select product;

var rows = filtered.Select(projection);
```

Direct invocation in query syntax works normally for in-memory `IEnumerable<T>` sequences because no LINQ
provider needs to translate the expression.

### Trade-off

Libraries such as LINQKit can wrap the query provider and expand reusable expressions anywhere in the complete
query. Raffinert instead uses explicit expansion boundaries. This keeps the runtime provider-independent and
makes the final expression tree directly inspectable, but reusable invocations must be contained in a Raffinert
wrapper or passed through `GetExpandedExpression()`.

Expansion only performs expression composition. Every node remaining in the expanded expression must still be
supported by the selected LINQ provider.

SQLite integration tests cover nested and cross-composed conditions/projections, `Then`, structural adaptation,
null-safe mapping, and merged member initializers.

## Runtime execution

`Invoke` and `InvokeOrDefault` execute a lazily compiled expanded expression. Expanded expressions, compiled delegates, and map-to-existing actions are cached per immutable wrapper instance. `GetExpression()` implementations are expected to remain stable after expansion or compilation is first requested.

`MapToExisting` updates assignments in an existing member-initialized destination. Existing nested destinations are updated in place, while missing writable nested destinations are created from the projection. Mutable collection members (`ICollection<T>` and `IList`) follow AutoMapper-style replacement semantics: the existing collection instance is preserved, cleared, and refilled with the projected elements; a null projected collection clears it to empty, and a missing writable collection is created. Arrays, writable members exposed only as `IEnumerable<T>`, and known read-only collection wrappers are replaced. Collection items are not matched or updated by key. If the root destination is null, `MapToExisting` creates it with the normal projection.

## Migration

The aggregate package deliberately uses one canonical invocation vocabulary. Replace `IsSatisfiedBy` and `Map` with `Invoke`, and replace `MapIfNotNull` with `InvokeOrDefault`. Rename `Spec<T>` to `Condition<T>`, `Proj<TIn,TOut>` to `Projection<TSource,TResult>`, and the public `Expr<TIn,TOut>` base to `ComposableExpression<TSource,TResult>`.

See the [migration guide](https://github.com/Raffinert/Raffinert.Expressions/blob/main/docs/migration.md) for details.

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
