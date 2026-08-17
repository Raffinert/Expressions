# Migration from Raffinert.Spec and Raffinert.Proj

## Packages and namespace

Replace references to `Raffinert.Spec` and `Raffinert.Proj` with `Raffinert.Expressions`. Change both old namespaces to:

```csharp
using Raffinert.Expressions;
```

The runtime remains `netstandard2.0` and has no EF Core dependency.

## Renamed semantic types

The aggregate package uses full names for its public semantic types:

- `Spec<T>` becomes `Specification<T>`;
- `Proj<TIn,TOut>` becomes `Projection<TSource,TResult>`;
- `Expr<TIn,TOut>` becomes `ComposableExpression<TSource,TResult>` for custom semantic wrappers.

The aggregate package otherwise preserves the structural capabilities:

- `Create`, subclassing, `GetExpression`, `True`, `False`, Boolean combinators and operators;
- direct `Where(specification)` use;
- projection creation, subclassing, `GetExpression`, and direct `Select(projection)` use;
- `MergeBindings`, `MapToExisting`, and debugger expression views;
- nested method-group forms through the canonical `Invoke` method.

Structural templates from `Raffinert.Spec` are not included in this release. Keep the old package where that feature
is required, or replace the adaptation with an explicit specification for each source type.

## Canonical invocation API

Use `Invoke` when composing either semantic type into another expression:

```csharp
var amount = Projection<Invoice, decimal>.Create(invoice => invoice.Amount);
var overdue = Specification<Invoice>.Create(invoice =>
    amount.Invoke(invoice) > 0m && invoice.DueDate < today);
```

The aggregate API deliberately removes the old execution aliases:

| Previous call | Replacement |
| --- | --- |
| `spec.IsSatisfiedBy(value)` | `spec.Invoke(value)` |
| `projection.Map(value)` | `projection.Invoke(value)` |
| `projection.MapIfNotNull(value)` | `projection.InvokeOrDefault(value)` |
| `Spec<T>.True()` | `Specification<T>.True` |
| `Spec<T>.False()` | `Specification<T>.False` |

`InvokeOrDefault` is available on the shared expression base. A null input returns `default(TResult)`, which means `false` for a specification and values such as `0` for value-type projections.

Use `projection.Then(nextProjection)` for `A -> B -> C` and `projection.Then(specification)` for `A -> B -> bool`.

## Deliberate safety changes

- `MergeBindings` removes duplicate destination bindings deterministically. The default is `UseLast`; choose `UseFirst` or `Throw` when appropriate.
- Conditional merge branches are matched by destination member, not binding order.
- `MapToExisting` throws a descriptive exception when a nested destination object is null. It does not automatically construct nested objects.
- Unsupported map-to-existing and merge shapes throw `NotSupportedException` instead of producing malformed trees.

## Query behavior

`IQueryable` extensions always submit expanded expressions. `IEnumerable` extensions use the cached compiled expanded delegate. No `AsExpandable()` call or provider wrapper is needed.
