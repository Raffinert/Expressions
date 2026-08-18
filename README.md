# Raffinert.Expressions

**Raffinert.Expressions** is a lightweight expression-composition library for reusable predicates and projections. Compose normal C# expression objects and hand the resulting pure expression tree to EF Core or any other LINQ provider—without a custom query provider or special query interception.

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

var expensive = Specification<Order>.Create(order =>
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

The library produces this tree by replacing each `.Invoke(...)` call on a specification or projection
with the referenced expression body.

## Specifications

Create a predicate inline or subclass `Specification<T>` and override `GetExpression()`:

```csharp
var inStock = Specification<Product>.Create(product => product.Stock > 0);
var visible = inStock.And(product => !product.IsHidden);
var wanted = visible & !Specification<Product>.Create(product => product.IsDiscontinued);

bool matches = wanted.Invoke(product);
```

`And`, `Or`, `Not`, `&`, `|`, `!`, `&&`, and `||` build direct Boolean expression nodes with correctly rebound parameters.

## Projections

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

`Invoke` is the common composition marker. A `Specification` can be embedded in a `Projection`, a `Projection` can be embedded in a `Specification`, and mixed chains can be nested to any practical depth. `GetExpandedExpression()` recursively substitutes the referenced lambda body at the call site.

`Invoke` is the only execution and composition primitive. Method groups such as `items.Any(spec.Invoke)` and `items.Select(projection.Invoke)` are expanded too.

## `Then` composition

Forward composition is available from projections:

```csharp
Projection<Order, Customer> customer = ...;
Projection<Customer, string> customerName = ...;
Specification<Customer> active = ...;

Projection<Order, string> orderCustomerName = customer.Then(customerName);
Specification<Order> activeCustomerOrder = customer.Then(active);
```

`Then` performs parameter substitution immediately; it does not introduce delegate invocation nodes.

## Structural adaptation

An existing specification or projection can be adapted to types with compatible public members; no separate
template object is required:

```csharp
Specification<InventoryItem> inventoryFilter = productFilter.AdaptSource<InventoryItem>();

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

The runtime library knows nothing about EF Core. Queryable overloads pass `GetExpandedExpression()` directly to LINQ. Expanded expressions contain ordinary nodes such as member access, calls to normal LINQ methods, Boolean operators, member initialization, and conditionals.

This means:

- no EF dependency in the runtime package;
- no custom query provider;
- no query interceptor;
- no `AsExpandable()` call;
- no per-row reflection or expression traversal.

SQLite integration tests cover nested and cross-composed predicates/projections, `Then`, structural adaptation,
null-safe mapping, and merged member initializers.

## Runtime execution

`Invoke` and `InvokeOrDefault` execute a lazily compiled expanded expression. Expanded expressions, compiled delegates, and map-to-existing actions are cached per immutable wrapper instance. `GetExpression()` implementations are expected to remain stable after expansion or compilation is first requested.

`MapToExisting` updates assignments in an existing member-initialized destination. Existing nested destinations are updated in place, while missing writable nested destinations are created from the projection. Mutable collection members (`ICollection<T>` and `IList`) follow AutoMapper-style replacement semantics: the existing collection instance is preserved, cleared, and refilled with the projected elements; a null projected collection clears it to empty, and a missing writable collection is created. Arrays, writable members exposed only as `IEnumerable<T>`, and known read-only collection wrappers are replaced. Collection items are not matched or updated by key. If the root destination is null, `MapToExisting` creates it with the normal projection.

## Migration

The aggregate package deliberately uses one canonical invocation vocabulary. Replace `IsSatisfiedBy` and `Map` with `Invoke`, and replace `MapIfNotNull` with `InvokeOrDefault`. Rename `Spec<T>` to `Specification<T>`, `Proj<TIn,TOut>` to `Projection<TSource,TResult>`, and the public `Expr<TIn,TOut>` base to `ComposableExpression<TSource,TResult>`.

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

## Project values

[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

This project:

- **condemns Russia and its military aggression against Ukraine**;
- **recognizes that Russia is an occupant that unlawfully invaded a sovereign state**;
- **supports Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea and Donbas**;
- **rejects false narratives perpetuated by Russian state propaganda**.

These statements express the maintainer's values and do not add conditions to the project's MIT license. To learn more about the war and how you can help, visit [Stand With Ukraine](https://stand-with-ukraine.pp.ua). Glory to Ukraine!
