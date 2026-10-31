# Migration to Raffinert.Expressions 2.0

Version 2 intentionally removes the previous public `Condition<T>` and `Projection<TSource,TResult>` wrapper types. They are both now `Expr<TSource,TResult>`. This is a **source- and binary-breaking** change and no compatibility aliases are provided.

## Package upgrade

Upgrade all Raffinert packages together to `2.0.0` (Core, EntityFrameworkCore and QuerySyntax as applicable). Core continues to target `netstandard2.0`; QuerySyntax targets `netstandard2.1`, and the optional EF Core adapter targets `net10.0` with EF Core 10.

## Creation and composition

| In version 1 | In version 2 |
|---|---|
| `Condition<Order>.Create(o => o.IsActive)` | `Expr<Order>.Create(o => o.IsActive)` |
| `Projection<Order>.Create(o => o.Total)` | `Expr<Order>.Create(o => o.Total)` |
| `Projection<Order, decimal>.Create(...)` | `Expr<Order, decimal>.Create(...)` |
| `Condition<Order>.True` / `False` | `Expr<Order>.Create(_ => true)` / `Expr<Order>.Create(_ => false)` |
| `Condition<Order>.Start()` | `PredicateBuilder<Order>.Create()` (then `Build()`) |
| `condition1 && condition2` | `condition1.And(condition2)` |
| `condition1 || condition2` | `condition1.Or(condition2)` |
| `!condition` | `condition.Not()` |

Boolean-specific immutable composition remains available for `IExpr<T,bool>`. For dynamic chains, `PredicateBuilder<T>` is mutable: `Create()` provides seed-free first `And`/`Or`, and `Build(bool balanced = true)` returns a reusable immutable `Expr<T,bool>`. Use `Expr<T>.Create(_ => true/false)` for standalone constants instead of a builder. An empty `Create()` builds `false` (or `true` with `defaultWhenEmpty: true`). Builder `Not()` materializes the empty fallback before negating. Snapshot results are independent of later builder mutations; builders themselves are not thread-safe. The former `Expr<T>.True/False` static singleton behavior is not preserved; ordinary constant expressions are independently constructed unless callers cache them.

```csharp
var amount = Expr<Invoice>.Create(invoice => invoice.Amount);
var overdue = Expr<Invoice>.Create(invoice => invoice.DueDate < today)
    .And(invoice => amount.Invoke(invoice) > 0m);
var query = db.Invoices.Where(overdue);
```

## Shared capabilities

`Invoke`, `InvokeOrDefault`, `GetExpression`, `GetExpandedExpression`, `AdaptSource`, `AdaptResult`, and `Adapt` are available on the unified concrete type. `Then` also accepts `IExpr` operands. `MergeBindings`, `GetMapToExistingExpression`, `GetMapToExistingAction`, and `MapToExisting` are now extension methods; their mapping state is allocated lazily. `MapToExisting` requires a reference-type destination. Member-initializer-specific features still require compatible expression shapes and throw for unsupported shapes; Boolean composition is statically limited to Boolean outputs.

Subclass `Expr<TSource,TResult>` and override `GetExpression()` for custom wrappers. `IExpr<TSource,out TResult>` remains the covariant LINQ consumer interface.

## LINQ extension type names

The LINQ methods themselves (`Where`, `Select`, `AnyAsync`, etc.) remain unchanged. Public extension class names use the role of their `IExpr` argument instead of the former wrapper types:

| Version 1 | Version 2 |
|---|---|
| `ConditionEnumerableExtensions` | `PredicateEnumerableExtensions` |
| `ConditionQueryableExtensions` | `PredicateQueryableExtensions` |
| `ProjectionEnumerableExtensions` | `SelectorEnumerableExtensions` |
| `ProjectionQueryableExtensions` | `SelectorQueryableExtensions` |
| `ConditionEntityFrameworkQueryableExtensions` | `PredicateEntityFrameworkQueryableExtensions` |

Named arguments change from `condition:` to `predicate:` and from `projection:` to `selector:` for the corresponding LINQ overloads. `keySelector` and `elementSelector` keep their existing names.

## EF Core and QuerySyntax

Existing `Where(expression)`, `Select(expression)`, `AnyAsync(expression)` and similar direct overloads consume the shared `IExpr` contract and need no special migration except for wrapper creation/type names. Nested invocations continue to expand as before. `UseRaffinertExpressions()` and `AsRaffinertQuery()` retain their behavior.

## Earlier Spec / Proj migrations

If moving from pre-aggregate `Raffinert.Spec` or `Raffinert.Proj`, map `Spec<T>` to `Expr<T,bool>` and `Proj<TIn,TOut>` to `Expr<TIn,TOut>`. Use `Invoke` instead of legacy `IsSatisfiedBy`, `Map` or `MapIfNotNull` (the latter becomes `InvokeOrDefault`).
