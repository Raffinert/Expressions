---
name: raffinert-expressions
description: Use Raffinert.Expressions correctly in C#/.NET applications. Activate when composing reusable business rules, predicates, conditions, projections, DTO mappings, dynamic filters, LINQ or EF Core queries; migrating expression composition from LINQKit; or choosing between the Core, EntityFrameworkCore, and QuerySyntax packages. Covers Invoke expansion boundaries, native EF parameterization, and common API pitfalls.
license: MIT
metadata:
  author: Raffinert
  project: https://github.com/Raffinert/Expressions
---

# Raffinert.Expressions — application integration

Use this skill to implement **consumer code** with Raffinert.Expressions. Model conditions and projections as reusable, composable objects, not as a set of unrelated expression-tree helpers. Do not introduce the library into an application merely to replace a simple inline lambda used once.

## Workflow

1. Identify the target framework, query provider (if any), EF Core version, and whether the code runs in memory or in an `IQueryable` pipeline.
2. Select **only the package(s) needed** from the table below; inspect the installed version and adapt examples to its actual API. Do not blindly upgrade EF or add optional packages.
3. Express reusable Boolean rules as `ComposableExpression<T, bool>` and reusable value/DTO transformations as `ComposableExpression<TSource, TResult>` (or `ComposableExpression<T>.Create(...)` with inferred result type).
4. Compose independent rules before consumption (`And`/`Or`/`Not`, `&`/`|`/`!`, `Then`, or nested `Invoke`). Keep compositions immutable: assign the returned wrapper rather than expecting in-place mutation.
5. Choose the correct expansion boundary for the **specific** query form. Do not leave an unexpanded Raffinert invocation for a provider to translate.
6. Test observable results. For `IQueryable`, execute against the actual provider (SQLite/SQL Server as appropriate) and inspect SQL/parameters where material. Do not mistake passing `IEnumerable` tests for proof of database translation.

## Package and expansion decision table

| Need | Choose | Invocation rules |
|---|---|---|
| Independent composition; in-memory use; direct `Where(condition)` / `Select(projection)` | `Raffinert.Expressions` (Core, `netstandard2.0`) | Core's direct LINQ overloads expand their wrapper arguments. Nested `Invoke` inside an **outer Raffinert wrapper** is recursively expanded when that wrapper is consumed. |
| EF Core 10 query lambdas, ordinary C# `from` syntax, or async condition overloads | `Raffinert.Expressions.EntityFrameworkCore` (`net10.0`) | Configure `UseRaffinertExpressions()` after the EF provider for invocation inside **ordinary EF-facing lambdas**. The adapter brings Core transitively and does **not** need QuerySyntax. Direct wrapper operators and adapter async condition overloads do not need registration. |
| Provider-independent C# query-syntax facade without the EF adapter | `Raffinert.Expressions.QuerySyntax` (`netstandard2.1`) | Opt in explicitly with `source.AsRaffinertQuery()`. Simply referencing the package does nothing. The package brings Core transitively. |

EF adapter compatibility for the documented 1.2.0 release: .NET 10 and EF Core/EF Core Relational `>= 10.0.11` and `< 11.0.0`. Check the installed package's dependency range rather than assuming support for EF Core 7–9 or 11.

Read [the usage patterns](references/patterns.md) for composition and structural mapping. Read [EF Core boundaries](references/ef-core.md) for database integration, parameterization, and tests. Do not load extra references for tasks that do not need them.

## Start with a useful composition, not a mechanical API demo

*Illustrative `Order`, `OrderLine`, and `OrderDto` models; the `OrderDto.Total` type matches the calculation.*

```csharp
using Raffinert.Expressions;

var total = ComposableExpression<Order>.Create(o =>
    o.Lines.Sum(l => l.Price * l.Quantity));

var expensive = ComposableExpression<Order>.Create(o =>
    total.Invoke(o) > 1000m);

var summary = ComposableExpression<Order>.Create(o => new OrderDto
{
    Id = o.Id,
    Total = total.Invoke(o)
});

var rows = await db.Orders.Where(expensive).Select(summary).ToListAsync();
```

`total` is reusable in the condition and the projection. The final provider-facing tree has the expression bodies inlined. Do **not** add `IsExpensive = expensive.Invoke(o)` to a projection following `.Where(expensive)` solely to demonstrate nested calls: it would always be true and obscures the domain intent.

## In-memory invocation versus composition marker

`condition.Invoke(entity)` and `projection.Invoke(entity)` **execute** a lazily compiled, per-wrapper cached delegate in ordinary code. Inside a Raffinert expression being expanded, those same `Invoke` calls are composition markers whose bodies are substituted. They are not arbitrary EF-translatable CLR methods.

```csharp
var active = ComposableExpression<Order>.Create(o => o.IsActive);
var expensive = ComposableExpression<Order>.Create(o => o.Total > 1000m);
var urgent = ComposableExpression<Order>.Create(o => o.IsUrgent);
var eligible = (active & expensive) | urgent;

bool accepted = eligible.Invoke(order); // in-memory execution
var query = db.Orders.Where(eligible); // Core direct operator expands
```

`ComposableExpression<T>.Start()` is for dynamic `And`/`Or` folds: an unchanged starter evaluates to `false` unless `Start(defaultWhenEmpty: true)` was used; its first composition supplies the initial rule without an extra Boolean seed. `True` and `False` are ordinary conditions, not starters.

## Stop and check these traps

- With **Core only**, `db.Orders.Where(o => eligible.Invoke(o))` is an ordinary provider-facing lambda; Core cannot expand its marker at that boundary. Pass `.Where(eligible)`, pass an appropriate expanded expression explicitly, or register the compatible EF adapter. Do not prescribe `AsExpandable()`.
- With the EF adapter registered, standard C# `from`/`where`/`select` works **without** QuerySyntax or `AsRaffinertQuery()`.
- A plain C# conditional inside an expression (e.g. `flag ? left.Invoke(o) : right.Invoke(o)`) is not a structural splicing API. Neither `Splice.Select` nor `BindIf` is a released Raffinert API. Choose a wrapper before building another wrapper, or conditionally compose `MergeBindings` when a destination assignment must be absent.
- `MergeBindings` requires compatible parameterless member-initializer shapes. Omitted bindings remain omitted; assigning `null` or `default` is not the same thing. On mapping into an existing destination, omitted members are not updated.
- `AdaptSource`, `AdaptResult`, and `Adapt` require compatible public member structure. Do not represent them as arbitrary model conversion.
- Provider translation is still the provider's job. Do not call `Compile()` or arbitrary CLR functions inside a provider-facing expression and expect SQL translation.
- A wrapper's structural expression must remain stable once expansion/compilation is first requested; create a new wrapper for changed composition. Scalar captures in ordinary EF queries follow native EF semantics.
- EF's `ToQueryString()` may contain sensitive values even with sensitive-data logging disabled. Do not log it indiscriminately.

## Finish the task

Preserve existing business behavior and package compatibility. Verify the actual code compiles and that relevant provider tests execute. Explain which expansion boundary was chosen and which optional package, if any, was needed. Never invent an API or claim that a hypothetical future feature exists.

Canonical project docs: https://github.com/Raffinert/Expressions (README, `docs/efcore-integration.md`, and `docs/architecture.md`).