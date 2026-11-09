[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

# Raffinert.Expressions

**Stop duplicating query logic. Start composing it.**

Define reusable, strongly typed expressions once, then combine them into predicates, calculations, and projections. Use the same expressions in memory or in LINQ queries without compiling delegates or introducing a custom query provider.

`Expr<TSource, TResult>` represents any single-input expression: a Boolean condition, a scalar calculation, or an object projection. Expressions can be written inline or as named classes, and nested calls are expanded into ordinary expression trees before reaching the LINQ provider.

[![Core NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.svg?label=Core)](https://www.nuget.org/packages/Raffinert.Expressions)
[![EF Core NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.EntityFrameworkCore.svg?label=EF%20Core)](https://www.nuget.org/packages/Raffinert.Expressions.EntityFrameworkCore)
[![QuerySyntax NuGet](https://img.shields.io/nuget/v/Raffinert.Expressions.QuerySyntax.svg?label=QuerySyntax)](https://www.nuget.org/packages/Raffinert.Expressions.QuerySyntax)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Quick start

```shell
dotnet add package Raffinert.Expressions
```

Define a calculation once and reuse it inside both a condition and a projection:

```csharp
using Raffinert.Expressions;

var lineTotal = Expr<OrderLine>.Create(line => line.Price * line.Quantity);

var orderTotal = Expr<Order>.Create(order =>
    order.Lines.Sum(line => lineTotal.Invoke(line)));

var expensive = Expr<Order>.Create(order =>
    orderTotal.Invoke(order) > 1000m);

var summary = Expr<Order>.Create(order => new OrderDto
{
    Id = order.Id,
    Total = orderTotal.Invoke(order)
});

var rows = await db.Orders
    .Where(expensive)
    .Select(summary)
    .ToListAsync();
```

`Where(expensive)` and `Select(summary)` use Raffinert's LINQ overloads, which recursively inline nested `Invoke` calls. EF Core receives provider-translatable expression trees, not compiled delegates. The core package needs no EF-specific registration for this usage.

*Examples use illustrative models and assume an existing `DbContext` and the usual provider-specific LINQ imports. The resulting expression must still be supported by the LINQ provider.*

## Define expressions inline or as classes

Create expressions with `Expr<T>.Create(...)` for concise, local definitions:

```csharp
var available = Expr<Product>.Create(p => p.Stock > 0);
var affordable = Expr<Product>.Create(p => p.Price < 100m);
var eligible = available.And(affordable);
bool matches = eligible.Invoke(product);
```

For named, parameterized business rules, inherit `Expr<TSource, TResult>` and override `GetExpression()`:

```csharp
using System.Linq.Expressions;
using Raffinert.Expressions;

public sealed class ProductNameExpression(string name) : Expr<Product, bool>
{
    public override Expression<Func<Product, bool>> GetExpression()
        => product => product.Name == name;
}

public sealed class ProductSummaryExpression : Expr<Product, ProductDto>
{
    public override Expression<Func<Product, ProductDto>> GetExpression()
        => product => new ProductDto
        {
            Name = product.Name,
            Price = product.Price
        };
}

var filter = new ProductNameExpression("Banana")
    .And(p => p.Price < 100m);

var products = await db.Products
    .Where(filter)
    .Select(new ProductSummaryExpression())
    .ToListAsync();
```

Class-based expressions and inline expressions have the same composition, expansion, and LINQ capabilities. Keep the structure returned by `GetExpression()` stable once the expression has been expanded or evaluated, because expanded trees and compiled delegates are cached per instance.

## Compose expressions

- **`And` / `Or` / `Not`:** combine Boolean expressions without changing the originals.
- **`Invoke`:** reuse conditions, calculations, or projections inside other expressions.
- **`Then`:** compose one projection with the next.
- **`PredicateBuilder<T>`:** build dynamic Boolean conditions.
- **`InvokeOrDefault`:** return the default result for a null input.
- **`AdaptSource` / `AdaptResult` / `Adapt`:** adapt expressions to compatible source/result types.
- **`MergeBindings` / `MapToExisting`:** combine object initializers or update existing destination objects.

For example, dynamically combine search terms:

```csharp
var builder = PredicateBuilder<Product>.Create();

foreach (var term in searchTerms)
    builder.Or(p => p.Name.Contains(term));

var query = db.Products.Where(builder.Build());
```

## Reuse expressions in nested collections

The same line condition can filter parent orders and the projected child collection:

```csharp
var validLine = Expr<OrderLine>.Create(line =>
    !line.IsCancelled && line.Quantity > 0);

var hasValidLines = Expr<Order>.Create(order =>
    order.Lines.Any(line => validLine.Invoke(line)));

var lineDto = Expr<OrderLine>.Create(line => new OrderLineDto
{
    Id = line.Id,
    Quantity = line.Quantity
});

var orderDto = Expr<Order>.Create(order => new OrderDto
{
    Id = order.Id,
    Lines = order.Lines
        .Where(line => validLine.Invoke(line))
        .Select(line => lineDto.Invoke(line))
        .ToList()
});

var results = await db.Orders
    .Where(hasValidLines)
    .Select(orderDto)
    .ToListAsync();
```

Nested `Invoke` calls are inlined before query translation. The query excludes orders with no valid lines and includes only valid line DTOs. This projects a filtered collection; it does not partially load or modify the tracked navigation.

## Packages and EF Core integration

| Package | Use it for |
| --- | --- |
| [Raffinert.Expressions](https://www.nuget.org/packages/Raffinert.Expressions) | Core composition and direct LINQ overloads; targets `netstandard2.0`. |
| [Raffinert.Expressions.EntityFrameworkCore](https://www.nuget.org/packages/Raffinert.Expressions.EntityFrameworkCore) | EF Core 10 async predicate operators and expansion inside ordinary query lambdas; requires .NET 10. |
| [Raffinert.Expressions.QuerySyntax](https://www.nuget.org/packages/Raffinert.Expressions.QuerySyntax) | Provider-independent C# query-syntax expansion via `AsRaffinertQuery()`; targets `netstandard2.1`. |

With the core package, pass an expression directly: `db.Products.Where(filter)` or `db.Products.Select(projection)`. No special registration is necessary.

If you need `Invoke` inside an **ordinary EF Core LINQ lambda** (for example, `db.Products.Where(p => filter.Invoke(p))`), install the EF Core package and enable the adapter on the queried context:

```shell
dotnet add package Raffinert.Expressions.EntityFrameworkCore
```

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connectionString)
    .UseRaffinertExpressions()
    .Options;
```

The adapter also enables standard C# query syntax with nested `Invoke` calls, without requiring the QuerySyntax package. For independent provider/query-syntax scenarios, use `AsRaffinertQuery()` instead. See the [EF Core integration guide](docs/efcore-integration.md) for compatibility, complete setup, and limitations.

## AI agent skill

Install the portable skill to help coding agents use Raffinert.Expressions correctly in your projects:

```shell
npx skills add Raffinert/Expressions
```

The skill lives in [`.agents/skills/raffinert-expressions/`](.agents/skills/raffinert-expressions/).

## More documentation

- [Architecture and supported expression shapes](docs/architecture.md)
- [EF Core integration, examples, and troubleshooting](docs/efcore-integration.md)
- [LINQKit comparison example](examples/LinqKitComparison/README.md)
- [Migration guide](docs/migration.md)

Questions or ideas? [Open an issue](https://github.com/Raffinert/Expressions/issues).
