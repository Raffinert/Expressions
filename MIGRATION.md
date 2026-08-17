# Migration from Raffinert.Spec and Raffinert.Proj

## Packages and namespace

Replace references to `Raffinert.Spec` and `Raffinert.Proj` with `Raffinert.Expressions`. Change both old namespaces to:

```csharp
using Raffinert.Expressions;
```

The runtime remains `netstandard2.0` and has no EF Core dependency.

## Preserved APIs

The aggregate package preserves the structural APIs:

- `Spec<T>.Create`, subclassing, `GetExpression`, `True`, `False`, Boolean combinators and operators;
- direct `Where(spec)` use;
- `Proj<TIn,TOut>.Create`, subclassing, `GetExpression`, and direct `Select(proj)` use;
- `MergeBindings`, `MapToExisting`, and debugger expression views;
- nested method-group forms through the canonical `Invoke` method;
- `SpecTemplate` as a facade over the new structural template engine.

## Canonical invocation API

Use `Invoke` when composing either semantic type into another expression:

```csharp
var amount = Proj<Invoice, decimal>.Create(invoice => invoice.Amount);
var overdue = Spec<Invoice>.Create(invoice =>
    amount.Invoke(invoice) > 0m && invoice.DueDate < today);
```

The aggregate API deliberately removes the old execution aliases:

| Previous call | Replacement |
| --- | --- |
| `spec.IsSatisfiedBy(value)` | `spec.Invoke(value)` |
| `projection.Map(value)` | `projection.Invoke(value)` |
| `projection.MapIfNotNull(value)` | `projection.InvokeOrDefault(value)` |
| `Spec<T>.True()` | `Spec<T>.True` |
| `Spec<T>.False()` | `Spec<T>.False` |

`InvokeOrDefault` is available on the shared expression base. A null input returns `default(TOut)`, which means `false` for a specification and values such as `0` for value-type projections.

Use `projection.Then(nextProjection)` for `A -> B -> C` and `projection.Then(specification)` for `A -> B -> bool`.

## Deliberate safety changes

- `MergeBindings` removes duplicate destination bindings deterministically. The default is `UseLast`; choose `UseFirst` or `Throw` when appropriate.
- Conditional merge branches are matched by destination member, not binding order.
- `MapToExisting` throws a descriptive exception when a nested destination object is null. It does not automatically construct nested objects.
- Unsupported map-to-existing and merge shapes throw `NotSupportedException` instead of producing malformed trees.
- Template adaptation correctly rejects missing or incompatible members and supports both target properties and fields.
- Template selectors must use direct sample-member reads and retain member names.

## Query behavior

`IQueryable` extensions always submit expanded expressions. `IEnumerable` extensions use the cached compiled expanded delegate. No `AsExpandable()` call or provider wrapper is needed.
