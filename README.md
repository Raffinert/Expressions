# Raffinert.Expressions

**Raffinert.Expressions** is a lightweight expression-composition library for reusable predicates and projections. Compose normal C# expression objects and hand the resulting pure expression tree to EF Core or any other LINQ provider—without a custom query provider or special query interception.

It combines the focused APIs of `Raffinert.Spec` and `Raffinert.Proj` around one expression-expansion engine. The runtime package targets `netstandard2.0`, has no EF Core dependency, and requires neither a custom `IQueryProvider` nor `AsExpandable()`.

## 30-second example

```csharp
using Raffinert.Expressions;

var total = Proj<Order, decimal>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Spec<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var projection = Proj<Order, OrderDto>.Create(order => new OrderDto
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

There are no Raffinert method calls and no `Expression.Invoke` nodes left in the expanded tree.

## Specifications

Create a predicate inline or subclass `Spec<T>` and override `GetExpression()`:

```csharp
var inStock = Spec<Product>.Create(product => product.Stock > 0);
var visible = inStock.And(product => !product.IsHidden);
var wanted = visible & !Spec<Product>.Create(product => product.IsDiscontinued);

bool matches = wanted.Invoke(product);
```

`And`, `Or`, `Not`, `&`, `|`, `!`, `&&`, and `||` build direct Boolean expression nodes with correctly rebound parameters.

## Projections

```csharp
var summary = Proj<Product, ProductSummary>.Create(product => new ProductSummary
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

`Invoke` is the common composition marker. A `Spec` can be embedded in a `Proj`, a `Proj` can be embedded in a `Spec`, and mixed chains can be nested to any practical depth. `GetExpandedExpression()` recursively substitutes the referenced lambda body at the call site.

`Invoke` is the only execution and composition primitive. Method groups such as `items.Any(spec.Invoke)` and `items.Select(projection.Invoke)` are expanded too.

## `Then` composition

Forward composition is available from projections:

```csharp
Proj<Order, Customer> customer = ...;
Proj<Customer, string> customerName = ...;
Spec<Customer> active = ...;

Proj<Order, string> orderCustomerName = customer.Then(customerName);
Spec<Order> activeCustomerOrder = customer.Then(active);
```

`Then` performs parameter substitution immediately; it does not introduce delegate invocation nodes.

## Null-safe projections

Use `InvokeOrDefault` only where a null input should produce `default(TOut)`:

```csharp
var category = Proj<Category, CategoryDto>.Create(value => new CategoryDto { Name = value.Name });
var product = Proj<Product, ProductDto>.Create(value => new ProductDto
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

## Structural expression templates

Define a predicate against a small structural shape and adapt it to types with compatible readable members:

```csharp
var template = ExpressionTemplate<Product>.Create(
    product => new { product.Name, product.Price },
    shape => shape.Price > 10m && shape.Name != null);

Spec<InventoryItem> adapted = template.AdaptSpec<InventoryItem>();
```

Properties and fields are supported. Missing, ambiguous, unreadable, and incompatible target members fail descriptively at runtime. The optional `Raffinert.Expressions.Analyzers` package reports unsupported shapes and incompatible adaptations at compile time. `SpecTemplate` remains as a migration facade.

## EF Core and provider compatibility

The runtime library knows nothing about EF Core. Queryable overloads pass `GetExpandedExpression()` directly to LINQ. Expanded expressions contain ordinary nodes such as member access, calls to normal LINQ methods, Boolean operators, member initialization, and conditionals.

This means:

- no EF dependency in the runtime package;
- no custom query provider;
- no query interceptor;
- no `AsExpandable()` call;
- no per-row reflection or expression traversal.

SQLite integration tests cover nested and cross-composed predicates/projections, `Then`, null-safe mapping, merged member initializers, and structural templates.

## Runtime execution

`Invoke` and `InvokeOrDefault` execute a lazily compiled expanded expression. Expanded expressions, compiled delegates, and map-to-existing actions are cached per immutable wrapper instance. `GetExpression()` implementations are expected to remain stable after expansion or compilation is first requested.

`MapToExisting` updates assignments in an existing member-initialized destination. Nested destinations must already exist; a descriptive exception is thrown when one is null. If the root destination is null, `MapToExisting` creates it with the normal projection.

## Migration

The aggregate package deliberately uses one canonical invocation vocabulary. Replace `IsSatisfiedBy` and `Map` with `Invoke`, and replace `MapIfNotNull` with `InvokeOrDefault`. The semantic types `Spec<T>` and `Proj<TIn,TOut>` remain unchanged.

See [MIGRATION.md](MIGRATION.md) for details.
