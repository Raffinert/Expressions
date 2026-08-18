# Codex Implementation Specification: `Raffinert.Expressions`

> [!NOTE]
> This is the historical planning input for the initial implementation. It is not the current product contract; unchecked criteria and superseded API decisions are intentionally preserved as an archival record. In particular, it uses the former `Specification<T>` name; the current API is `Condition<T>`. See the [README](../../README.md) and [architecture document](../architecture.md) for current behavior.

## 0. Mission

Implement a new aggregated library named **`Raffinert.Expressions`** by consolidating the useful concepts of:

- `Raffinert.Spec`
- `Raffinert.Proj`

The new library must preserve the semantics of the source packages under the first-class `Specification<T>` and `Projection<TSource,TResult>` APIs, while moving their shared behavior onto a single reusable expression-composition core.

The primary architectural idea is:

> `Specification<T>` and `Projection<TSource,TResult>` are specialized typed wrappers around reusable LINQ expression trees. They must be composable with each other, expandable into pure `Expression<TDelegate>` trees, executable in memory, and consumable by EF Core or other LINQ providers without a custom query provider or `AsExpandable()`-style interception.

The implementation should favor **small, explicit expression-tree transformations** over framework magic.

---

# 1. Source repositories and current behavior

Use the current public repositories as the behavioral source material:

- `https://github.com/Raffinert/Raffinert.Spec`
- `https://github.com/Raffinert/Raffinert.Proj`

Important existing behavior to preserve unless this specification explicitly changes it:

The source repositories use the short type names `Spec<T>` and `Proj<TIn,TOut>`; the aggregate API deliberately expands those names to `Specification<T>` and `Projection<TSource,TResult>`.

## `Raffinert.Spec`

- `Spec<T>.Create(Expression<Func<T,bool>>)`
- subclassing `Spec<T>` and overriding `GetExpression()`
- `True()` / `False()`
- `And(...)`, `Or(...)`, `Not()`
- `&`, `|`, `!`
- conditional operator support currently enabled through `operator true/false`
- runtime predicate evaluation
- nested specification expansion
- method-group usage in LINQ (`items.Any(spec.IsSatisfiedBy)` today)
- direct use with `IQueryable<T>.Where(spec)`
- direct use with `IEnumerable<T>.Where(spec)`
- debugger expression view

## `Raffinert.Proj`

- `Proj<TIn,TOut>.Create(Expression<Func<TIn,TOut>>)`
- subclassing and overriding `GetExpression()`
- runtime mapping
- nested projection expansion
- nested projection through method groups in `Select`
- null-safe nested projection (`MapIfNotNull` today)
- `MergeBindings`
- `MapToExisting`
- direct use with `IQueryable<T>.Select(proj)`
- direct use with `IEnumerable<T>.Select(proj)`
- debugger expression view

Do not blindly copy existing implementation internals. Consolidate them around the architecture specified below.

---

# 2. Package and namespace layout

## Required repository name

`Raffinert.Expressions`

## Required primary NuGet package

`Raffinert.Expressions`

## Required primary namespace

```csharp
namespace Raffinert.Expressions;
```

## Suggested solution layout

```text
Raffinert.Expressions.sln

src/
  Raffinert.Expressions/
    Core/
    Specifications/
    Projections/
    Extensions/
    Debugging/

tests/
  Raffinert.Expressions.UnitTests/
  Raffinert.Expressions.IntegrationTests/

benchmarks/
  Raffinert.Expressions.Benchmarks/      # optional, only after functional completion
```

Keep the runtime package free of EF Core dependencies.

Target **`netstandard2.0`** unless a concrete implementation blocker is found. The existing libraries target `netstandard2.0`, so preserving that compatibility is preferred.

Nullable reference types must remain enabled.

---

# 3. Core conceptual model

Introduce an internal/shared abstraction representing a reusable expression from `TSource` to `TResult`.

The public design exposes `ComposableExpression<TSource,TResult>` as a supported base for custom semantic wrappers, but **does not require users to know or use it** for normal `Specification`/`Projection` scenarios.

Preferred design:

```csharp
public abstract class ComposableExpression<TSource, TResult>
{
    public abstract Expression<Func<TSource, TResult>> GetExpression();

    public Expression<Func<TSource, TResult>> GetExpandedExpression();

    public TResult Invoke(TSource value);
}
```

Then:

```csharp
public abstract class Specification<T> : ComposableExpression<T, bool>
{
    // specification-specific API
}

public abstract class Projection<TSource, TResult> : ComposableExpression<TSource, TResult>
{
    // projection-specific API
}
```

If inheritance introduces unacceptable API or implementation complexity, use an internal interface/base class instead, but the following invariant is mandatory:

> `Specification` and `Projection` must share one expansion engine and one generalized expression-node abstraction.

There must not be separate near-duplicate `IsSatisfiedByCallVisitor` and `MapCallVisitor` implementations in the final architecture.

---

# 4. Canonical invocation API

Introduce **`Invoke`** as the canonical expression-composition marker.

## Core behavior

```csharp
TResult Invoke(TSource value)
```

At runtime, outside an expression tree, this executes the compiled expression.

Inside a parent expression tree, calls to `Invoke(...)` on a supported `ComposableExpression`/`Specification`/`Projection` instance must be **inlined** by `GetExpandedExpression()`.

Example:

```csharp
var categoryProjection = Projection<Category, CategoryDto>.Create(c => new CategoryDto
{
    Name = c.Name
});

var productProjection = Projection<Product, ProductDto>.Create(p => new ProductDto
{
    Name = p.Name,
    Category = categoryProjection.Invoke(p.Category)
});
```

Expanded result:

```csharp
p => new ProductDto
{
    Name = p.Name,
    Category = new CategoryDto
    {
        Name = p.Category.Name
    }
}
```

## Semantic aliases to keep

Keep aliases for readability and source migration:

```csharp
public bool IsSatisfiedBy(T value) => Invoke(value);
```

```csharp
public TResult Map(TSource value) => Invoke(value);
```

The expansion engine must recognize both the canonical `Invoke` API and legacy semantic aliases during the migration period.

`Invoke` should be the API used in new documentation for cross-composition.

---

# 5. Cross-composition: mandatory feature

This is one of the primary goals of the aggregation.

## 5.1 `Specification` inside `Projection`

Required:

```csharp
Specification<Product> expensive =
    Specification<Product>.Create(p => p.Price > 100m);

Projection<Product, ProductDto> projection =
    Projection<Product, ProductDto>.Create(p => new ProductDto
    {
        Name = p.Name,
        IsExpensive = expensive.Invoke(p)
    });
```

Expanded expression must be equivalent to:

```csharp
p => new ProductDto
{
    Name = p.Name,
    IsExpensive = p.Price > 100m
}
```

## 5.2 `Projection` inside `Specification`

Required:

```csharp
Projection<Order, decimal> total =
    Projection<Order, decimal>.Create(o =>
        o.Lines.Sum(x => x.Price * x.Quantity));

Specification<Order> expensive =
    Specification<Order>.Create(o => total.Invoke(o) > 1000m);
```

Expanded expression must be equivalent to:

```csharp
o => o.Lines.Sum(x => x.Price * x.Quantity) > 1000m
```

## 5.3 Arbitrarily nested composition

The expansion engine must recursively expand until the resulting tree has no recognized library invocation markers.

Example chain:

```text
Specification -> Projection -> Projection -> Specification
```

must be supported when type-compatible.

Detect cycles where practical. A self-referential expression graph must fail deterministically with a meaningful exception rather than causing infinite recursion/stack overflow.

---

# 6. Generic composition API (`Then` / composition algebra)

Implement typed composition as a first-class feature after core invocation expansion works.

## Required overloads

Conceptually:

```csharp
Projection<TSource, TNext> Then<TNext>(Projection<TResult, TNext> next);
```

and:

```csharp
Specification<TSource> Then(Specification<TResult> next);
```

for `Projection<TSource,TResult>`.

Meaning:

```text
A -> B
B -> C
------
A -> C
```

and:

```text
A -> B
B -> bool
---------
A -> bool
```

Examples:

```csharp
Projection<Order, Customer> customer = ...;
Projection<Customer, string> name = ...;

Projection<Order, string> customerName = customer.Then(name);
```

```csharp
Projection<Order, Customer> customer = ...;
Specification<Customer> active = ...;

Specification<Order> orderWithActiveCustomer = customer.Then(active);
```

The resulting expressions must be direct composed expression trees, not delegate invocation expressions unsupported by common LINQ providers.

Do not add a large functional-programming API beyond what is necessary for clear expression composition.

---

# 7. Expression expansion engine

Implement one generalized expansion engine.

Suggested internal abstraction:

```csharp
internal interface IExpressionExpansionSource
{
    LambdaExpression GetExpression();
    LambdaExpression? GetCachedExpandedExpression();
    LambdaExpression CacheExpandedExpression(LambdaExpression expression);
}
```

or equivalent.

## Mandatory engine responsibilities

1. Find calls to library invocation markers (`Invoke`, plus compatibility aliases).
2. Resolve the referenced expression object.
3. Obtain its already-expanded inner expression.
4. Replace the inner lambda parameter with the invocation argument expression.
5. Inline the body.
6. Continue recursively.
7. Preserve nested lambda scoping.
8. Avoid incorrect parameter replacement across shadowed nested lambdas.

## Parameter replacement

Build one robust reusable parameter-replacement visitor.

It must support replacing a `ParameterExpression` with an arbitrary `Expression`, not only another parameter.

Conceptual API:

```csharp
internal sealed class ReplaceExpressionVisitor : ExpressionVisitor
{
    public ReplaceExpressionVisitor(Expression from, Expression to);
}
```

or a specialized parameter replacement equivalent.

## Invocation target resolution

Support at least:

- captured variables/fields;
- readonly fields;
- static fields where possible;
- direct `new SomeSpecification(...)` / `new SomeProjection(...)` cases already supported by current libraries;
- method-group scenarios generated by C# for `Any(spec.Invoke)` / `Select(proj.Invoke)`;
- closure member access chains where safely resolvable.

Avoid assuming closure storage is always a single `ConstantExpression -> FieldInfo` pair.

Implement a safe member-value evaluator for constant/closure-rooted member chains rather than compiling arbitrary user expression subtrees whenever possible.

Do **not** execute arbitrary parameter-dependent expression code at expansion time.

## No `Expression.Invoke` in final provider-facing trees

The final expanded tree must inline composition. It must not rely on `InvocationExpression`/`Expression.Invoke` as the core mechanism, because provider compatibility is a central goal.

---

# 8. `Specification<T>` API

Preserve the semantic specification type.

Required public API shape:

```csharp
public abstract class Specification<T> : ComposableExpression<T, bool>
{
    public abstract override Expression<Func<T, bool>> GetExpression();

    public static Specification<T> Create(Expression<Func<T, bool>> expression);

    public static Specification<T> True();
    public static Specification<T> False();

    public bool IsSatisfiedBy(T candidate);

    public Specification<T> And(Specification<T> spec);
    public Specification<T> And(Expression<Func<T, bool>> expression);

    public Specification<T> Or(Specification<T> spec);
    public Specification<T> Or(Expression<Func<T, bool>> expression);

    public Specification<T> Not();

    public static Specification<T> operator &(Specification<T> left, Specification<T> right);
    public static Specification<T> operator |(Specification<T> left, Specification<T> right);
    public static Specification<T> operator !(Specification<T> spec);
}
```

Retain `operator true/false` only if required to preserve current `&&`/`||` behavior and tests. Do not add surprising boolean conversion semantics.

## Composition rules

`And` uses `Expression.AndAlso`.

`Or` uses `Expression.OrElse`.

`Not` uses `Expression.Not`.

Always parameter-rebind the right expression to the left expression's parameter without `Expression.Invoke`.

## Runtime execution

Compiled delegate caching must remain lazy.

Thread safety does not need elaborate locking; duplicate compilation under a benign race is acceptable only if documented and there is no corrupted state. Prefer `Lazy<T>` or a simple safe cache if inexpensive.

---

# 9. `Projection<TSource,TResult>` API

Preserve the semantic projection type.

Required core API:

```csharp
public abstract class Projection<TSource, TResult> : ComposableExpression<TSource, TResult>
{
    public abstract override Expression<Func<TSource, TResult>> GetExpression();

    public static Projection<TSource, TResult> Create(Expression<Func<TSource, TResult>> expression);

    public TResult Map(TSource value);

    public TResult? MapIfNotNull(TSource? value);

    public Projection<TSource, TResult> MergeBindings(Projection<TSource, TResult> other);
    public Projection<TSource, TResult> MergeBindings(Expression<Func<TSource, TResult>> other);

    public Expression<Action<TSource, TResult>> GetMapToExistingExpression();
    public Action<TSource, TResult> GetMapToExistingAction();
    public void MapToExisting(TSource source, ref TResult? destination);
}
```

`Map` is a semantic alias of `Invoke`.

Do not remove it.

---

# 10. Null-safe invocation

Retain current `MapIfNotNull` behavior, but generalize the internal mechanism.

Prefer adding a generalized null-safe expression composition API only if it remains simple.

At minimum:

```csharp
proj.MapIfNotNull(value)
```

must expand to a provider-friendly conditional expression.

For reference-type input:

```csharp
value == null
    ? default(TOut)
    : /* expanded projection */
```

Do not use `Activator.CreateInstance` to obtain expression-tree constants when `Expression.Default(type)` is sufficient.

If nullable value types are supported, cover them explicitly in tests.

Do not silently invent null checks for normal `Invoke` calls.

---

# 11. `MergeBindings` redesign

Current projection binding merging must be made deterministic and structurally safe.

## Supported expression bodies

At minimum:

- `MemberInitExpression`
- supported conditional expressions whose branches contain compatible member initializers

## Duplicate binding semantics

Introduce an explicit conflict policy.

Suggested enum:

```csharp
public enum BindingConflictBehavior
{
    UseLast,
    UseFirst,
    Throw
}
```

Default should be **`UseLast`** because it makes:

```csharp
baseProjection.MergeBindings(overrides)
```

behave like an overlay.

Example:

```text
First:  Id, Name, Price
Second: Name, Category
Result: Id(first), Price(first), Name(second), Category(second)
```

Do not implement merging as raw `Concat` that leaves duplicate member bindings.

## Conditional bindings

Do not zip branch bindings by position.

Match conditional branch assignments by `MemberInfo`/member identity.

Fail clearly when branches are structurally incompatible and cannot safely be merged.

Add tests with branch properties in different orders.

---

# 12. `MapToExisting` rules

Preserve current feature but harden it.

`GetMapToExistingExpression()` rewrites creation/member-init projections into update actions.

Required behavior:

```csharp
source => new Destination
{
    A = source.A,
    B = source.B
}
```

becomes conceptually:

```csharp
(source, existing) =>
{
    existing.A = source.A;
    existing.B = source.B;
}
```

## Nested objects

Nested member initializers recursively update existing destination instances. When a writable destination member is null, create and assign the member initializer produced by the projection.

Do not accidentally dereference an existing nested destination that can be null.

Define and test behavior for:

- existing nested object present;
- destination nested object null;
- source-side conditional producing null;
- nested projection expanded through `Invoke`.

When a missing nested destination is read-only and cannot be assigned, throw a clear exception rather than allowing an opaque `NullReferenceException` during a compiled mapper.

## Unsupported projection bodies

Throw `NotSupportedException` with a descriptive message for unsupported shapes instead of producing malformed expression trees.

---

# 13. Queryable and Enumerable integration

Preserve direct ergonomic usage.

## IQueryable

```csharp
IQueryable<T> Where<T>(this IQueryable<T> source, Specification<T> spec)
```

must call:

```csharp
source.Where(spec.GetExpandedExpression())
```

and:

```csharp
IQueryable<TResult> Select<TSource,TResult>(
    this IQueryable<TSource> source,
    Projection<TSource,TResult> projection)
```

must call:

```csharp
source.Select(projection.GetExpandedExpression())
```

## IEnumerable

Equivalent overloads should execute compiled delegates.

Keep extension class names/namespaces non-conflicting. Avoid placing generic classes named simply `Queryable` / `Enumerable` in the global namespace. Use descriptive static classes such as:

```csharp
public static class SpecificationQueryableExtensions
public static class ProjectionQueryableExtensions
public static class SpecificationEnumerableExtensions
public static class ProjectionEnumerableExtensions
```

all under `Raffinert.Expressions` or `Raffinert.Expressions.Extensions`.

---

# 14. Direct structural adaptation

Existing specifications and projections are their own structural definitions; do not introduce a separate public
template abstraction.

Required API:

```csharp
specification.AdaptSource<TNewSource>();
projection.AdaptSource<TNewSource>();
projection.AdaptResult<TNewResult>();
projection.Adapt<TNewSource,TNewResult>();
```

Source adaptation rebinds parameter-rooted public property and field paths by name and compatible type. Result
adaptation rebuilds parameterless member initializers against the new result type, including nested initializers and
compatible conditional branches. Perform adaptation once when the method is called; provider-facing execution must
contain only the resulting ordinary expression tree.

Missing, ambiguous, inaccessible, and incompatible members must fail descriptively. Reject direct source-parameter
usage, source-specific operations that cannot be safely rebound, and result constructors with arguments.

---

# 15. Debugging experience

Preserve custom debugger support.

`Specification` and `Projection` should expose a useful `DebuggerDisplay` showing the **expanded expression**, because that is what the LINQ provider receives.

Avoid duplicating almost-identical debugger infrastructure where generic helpers can be shared.

The debugger proxy should make at least these available:

- Original expression
- Expanded expression
- Compiled/runtime representation where useful

Do not add runtime dependencies solely for pretty-printing expressions.

---

# 16. Caching rules

Cache only deterministic results owned by an immutable expression wrapper instance.

Reasonable caches:

- compiled delegate;
- expanded expression;
- map-to-existing expression/action.

Do not globally cache expression trees keyed by arbitrary objects unless there is a demonstrated need.

Document an important invariant:

> Implementations of `GetExpression()` are assumed stable for the lifetime of a `Specification` / `Projection` instance once expansion or compilation has been requested.

If a subclass returns a materially different expression on each call, cached behavior is undefined/not supported.

---

# 17. EF/LINQ provider compatibility principles

The runtime package must remain provider-agnostic.

Do not reference EF Core from the main package.

The library should produce ordinary expression trees consisting of common expression nodes that providers can inspect.

Avoid introducing provider-unfriendly nodes merely for internal convenience.

Especially avoid leaving:

```csharp
Expression.Invoke(...)
```

in expanded provider-facing output when composition can be represented by direct substitution.

Integration tests may use EF Core SQLite or another lightweight EF provider to prove translation.

---

# 18. Backward compatibility strategy

This is a new package and namespace, so binary compatibility with old NuGets is not required, but source migration should be deliberately easy.

## Keep names

Keep:

```text
Specification<T>
Projection<TSource,TResult>
IsSatisfiedBy
Map
MapIfNotNull
MergeBindings
MapToExisting
GetExpression
GetExpandedExpression
```

## Introduce canonical new API

Add:

```text
ComposableExpression<TSource,TResult>
Invoke
Then
```

## Optional compatibility packages

Do **not** implement compatibility packages unless needed after the new library works.

Possible later packages:

```text
Raffinert.Spec -> depends on Raffinert.Expressions and forwards/wraps old API
Raffinert.Proj -> depends on Raffinert.Expressions and forwards/wraps old API
```

This is outside MVP unless explicitly requested.

---

# 19. API naming conventions

Use semantic types for user code:

```csharp
Specification<Order>
Projection<Order, OrderDto>
```

Do not replace them with a generic name such as:

```csharp
ExpressionConstructor<...>
```

The project/package is named `Raffinert.Expressions`, while semantic wrappers retain domain vocabulary.

Use:

- `Invoke` = common fundamental composition/execution primitive;
- `IsSatisfiedBy` = spec semantic alias;
- `Map` = projection semantic alias;
- `Then` = typed forward composition;
- `GetExpandedExpression` = explicit inspection API.

Avoid `Apply`, `Execute`, `Evaluate`, `Call`, and `Compose` aliases unless they solve a demonstrated ambiguity. Do not create synonym-heavy APIs.

---

# 20. Required tests

The implementation is not complete until these scenarios are covered.

## 20.1 Core expression tests

- parameter replacement;
- nested lambda parameter shadowing;
- captured constant values;
- captured spec/proj field;
- direct `new SpecificationSubclass(...)` invocation;
- direct `new ProjectionSubclass(...)` invocation;
- nested expansion depth > 2;
- cycle behavior;
- expanded tree contains no library `Invoke` marker calls.

## 20.2 Specification tests

- inline `Create`;
- subclass;
- `True` / `False`;
- `And`;
- `Or`;
- `Not`;
- operators;
- `IEnumerable.Where(spec)`;
- `IQueryable.Where(spec)`;
- nested `Specification.Invoke`;
- compatibility `IsSatisfiedBy` expansion;
- method group in `Any`.

## 20.3 Projection tests

- inline `Create`;
- subclass;
- runtime `Map`;
- runtime `Invoke`;
- queryable projection;
- enumerable projection;
- nested projection `Invoke`;
- compatibility `Map` expansion;
- method group in nested `Select`;
- null-safe mapping;
- `MergeBindings` without conflict;
- conflict `UseLast`;
- conflict `UseFirst`;
- conflict `Throw`;
- conditional binding merge with branch order mismatch;
- map-to-existing simple;
- map-to-existing nested;
- map-to-existing null behavior.

## 20.4 Cross-composition tests

Mandatory:

```text
Specification inside Projection
Projection inside Specification
Specification -> Projection -> Specification nested chain
Projection -> Projection -> Projection nested chain
Projection.Then(Projection)
Projection.Then(Specification)
```

Prove both:

1. runtime compiled execution;
2. provider-facing expanded expression.

## 20.5 EF Core integration tests

Use a relational provider such as SQLite in-memory.

Test at least:

- nested spec translates;
- nested projection translates;
- spec reused inside projection translates;
- projection reused inside spec translates;
- composed scalar projection translates;
- null-safe nested projection translates;
- merged member-init projection translates.

Assert results and, where useful, inspect generated SQL or ensure no client-evaluation/translation exception occurs.

## 20.6 Structural adaptation tests

- specification source adaptation;
- projection source and result adaptation, independently and together;
- property/field interchange;
- nested source paths and nested result initializers;
- missing, incompatible, and unsupported shapes;
- adapted expressions used in an EF Core query.

# 21. Test style

Prefer behavioral tests over exact `Expression.ToString()` comparisons.

Exact expression-string tests may be retained for a small number of expansion-shape assertions, but they must not be the only correctness proof.

Where structure matters, inspect `ExpressionType`, member names, parameter identity, and absence of marker calls.

Use clear Arrange/Act/Assert structure.

Avoid brittle tests tied to compiler-generated closure class names.

---

# 22. Performance expectations

Performance is not the first milestone, but avoid obvious regressions.

Expected characteristics:

- expansion occurs once per wrapper instance and is cached;
- compilation occurs once per wrapper instance and is cached;
- normal `IQueryable` use does not compile expressions;
- no per-row reflection is allowed when using `IQueryable`;
- no per-row expression traversal;
- no custom `IQueryProvider` wrapper.

After functional completion, optional benchmarks should compare:

- raw handwritten expression;
- `Specification` composed expression expansion;
- `Projection` nested expression expansion;
- compiled runtime execution after warm-up.

Do not optimize by sacrificing API clarity or correctness before profiling.

---

# 23. Exception/error design

Throw early and descriptively when the library cannot safely transform an expression.

Examples:

```text
Unable to resolve expression instance for invocation marker ...
Expression composition cycle detected ...
MergeBindings requires a MemberInitExpression ...
Projection branches bind incompatible members ...
Target type 'X' is missing required member 'Y' ...
MapToExisting does not support expression node '...' ...
```

Include relevant type/member names.

Do not silently return partially expanded provider-facing expressions when the invocation marker is recognized but cannot be expanded.

If an unrelated method named `Invoke` exists, ignore it unless its declaring/target type is part of the Raffinert expression abstraction.

---

# 24. README requirements

Rewrite the new root README around the actual niche, not around "yet another specification library".

Suggested opening:

> **Raffinert.Expressions** is a lightweight expression-composition library for reusable predicates and projections. Compose normal C# expression objects and hand the resulting pure expression tree to EF Core or any other LINQ provider — without a custom query provider or special query interception.

The first example should demonstrate **cross-composition**, because that differentiates the aggregated library.

Example:

```csharp
var total = Projection<Order, decimal>.Create(o =>
    o.Lines.Sum(x => x.Price * x.Quantity));

var expensive = Specification<Order>.Create(o =>
    total.Invoke(o) > 1000m);

var projection = Projection<Order, OrderDto>.Create(o => new OrderDto
{
    Id = o.Id,
    Total = total.Invoke(o),
    IsExpensive = expensive.Invoke(o)
});

var rows = await db.Orders
    .Where(expensive)
    .Select(projection)
    .ToArrayAsync();
```

Then show the conceptual expanded expression.

README sections should be approximately:

1. What problem this solves
2. 30-second example
3. Specifications
4. Projections
5. Cross-composition with `Invoke`
6. `Then` composition
7. Null-safe projections
8. Merge bindings
9. Structural adaptation
10. EF Core/provider compatibility
11. Runtime execution
12. Migration from `Raffinert.Spec` / `Raffinert.Proj`

Explicitly state:

- no EF dependency;
- no custom query provider;
- no `AsExpandable()` requirement;
- final expressions are ordinary expression trees.

---

# 25. NuGet metadata

Suggested package description:

> Lightweight composable and structurally adaptable expression trees for reusable specifications, projections, and LINQ/EF Core query logic.

Suggested tags:

```text
LINQ
Expressions
ExpressionTree
Specification
Projection
EFCore
Composable
Predicate
Selector
```

Use correct GitHub URLs for the new repository.

Avoid duplicate MSBuild properties currently present in the old csproj files.

Prefer central package/version properties if useful, but do not introduce a large build framework merely for packaging.

---

# 26. Implementation phases

Codex should execute in this order.

## Phase 1 — Bootstrap aggregate repository

- create solution/projects;
- copy/adapt necessary tests and model fixtures;
- establish `Raffinert.Expressions` package metadata;
- preserve `netstandard2.0` runtime target.

## Phase 2 — Shared core

- implement common expression abstraction;
- generalized parameter/expression replacement;
- unified invocation expansion engine;
- caching;
- debugger basics.

Do not implement cross-composition with two independent visitors.

## Phase 3 — `Specification`

- migrate `Specification<T>`;
- boolean composition;
- queryable/enumerable extensions;
- `Invoke` + `IsSatisfiedBy` compatibility;
- existing behavioral tests.

## Phase 4 — `Projection`

- migrate `Projection<TSource,TResult>`;
- `Invoke` + `Map` compatibility;
- null-safe projection;
- queryable/enumerable extensions;
- nested projection tests.

## Phase 5 — Cross-composition

- `Specification` inside `Projection`;
- `Projection` inside `Specification`;
- mixed deep nesting;
- method groups;
- EF integration tests.

This phase is a release blocker.

## Phase 6 — `Then`

- `Projection -> Projection`;
- `Projection -> Specification`;
- tests and documentation.

## Phase 7 — Projection advanced features

- hardened `MergeBindings`;
- explicit conflict behavior;
- hardened `MapToExisting`.

## Phase 8 — Structural adaptation

- adapt specification and projection source types directly;
- adapt projection result types;
- add runtime validation and EF translation tests.

## Phase 9 — Documentation and cleanup

- new README;
- migration guide;
- XML documentation on public APIs;
- package metadata;
- remove dead duplicated visitors/utilities;
- run formatting and all tests.

---

# 27. Acceptance criteria

The work is accepted only when all of the following are true:

- [ ] One shared expansion engine serves `Specification` and `Projection`.
- [ ] `Specification<T>` remains a first-class API.
- [ ] `Projection<TSource,TResult>` remains a first-class API.
- [ ] `Invoke` is available as common composition API.
- [ ] `IsSatisfiedBy` remains available.
- [ ] `Map` remains available.
- [ ] `Specification` can be used inside `Projection` and is inlined.
- [ ] `Projection` can be used inside `Specification` and is inlined.
- [ ] Deep mixed composition works.
- [ ] Provider-facing expressions contain no unresolved Raffinert invocation marker calls.
- [ ] Provider-facing composition does not require `Expression.Invoke`.
- [ ] Existing `And`/`Or`/`Not` behavior works.
- [ ] Existing nested `Any(spec...)` scenarios work.
- [ ] Existing nested `Select(proj...)` scenarios work.
- [ ] Queryable extension methods use expanded expressions.
- [ ] Enumerable extension methods use compiled delegates.
- [ ] Null-safe nested projection behavior works.
- [ ] `MergeBindings` has deterministic duplicate-member semantics.
- [ ] Conditional binding merge is member-based, not positional.
- [ ] Existing map-to-existing scenarios work or have explicitly documented safer behavior.
- [ ] Template missing-member bug is fixed.
- [ ] Template property/field mismatch is fixed.
- [ ] Runtime template validation tests pass.
- [ ] EF Core integration tests cover mixed `Specification`/`Projection` reuse.
- [ ] Runtime package has no EF Core dependency.
- [ ] Runtime package still targets `netstandard2.0` unless a documented blocker required change.
- [ ] Public APIs have XML documentation.
- [ ] README leads with expression composition/cross-reuse rather than Specification Pattern history.
- [ ] `dotnet test` passes for the entire solution.
- [ ] `dotnet pack` succeeds for the runtime package.

---

# 28. Non-goals for the first release

Do not expand scope into the following unless required for the acceptance criteria:

- custom `IQueryProvider`;
- EF Core interceptors;
- source generation for normal expression composition;
- automatic object-to-object mapper competing with AutoMapper/Mapperly/AlephMapper;
- SQL-provider-specific rewrites;
- arbitrary partial evaluation engine;
- expression serialization;
- dynamic string-based expression parser;
- generalized optics/lenses library;
- async expressions;
- multi-parameter expression algebra beyond what existing features require;
- aggressive global caching;
- compatibility shims/packages for old NuGets before the new package is functionally complete.

Keep the package focused on **typed reusable expression composition**.

---

# 29. Design guardrails for Codex

When there is ambiguity, follow these priorities in order:

1. Correct expression semantics.
2. LINQ-provider friendliness.
3. Strong static typing.
4. Simple public API.
5. Preservation of `Specification` and `Projection` semantic vocabulary.
6. Reuse of one shared expression core.
7. Backward source familiarity.
8. Performance.
9. Cleverness.

Do not solve a local issue by duplicating expansion logic.

Do not add public abstractions simply because an internal implementation needs them.

Keep `ComposableExpression<TSource,TResult>` public only as a useful supported base for custom semantic expression wrappers, not merely as inheritance plumbing.

Do not silently broaden the library into a mapping framework.

---

# 30. Expected final Codex deliverables

Codex should finish with:

1. Compiling `Raffinert.Expressions.sln`.
2. Runtime package project.
3. Unit tests.
4. EF integration tests.
5. README.
6. Migration document from both legacy libraries.
7. Changelog/release notes for initial aggregated release.
8. A short architecture document explaining:
   - invocation expansion;
   - parameter substitution;
   - cross-composition;
   - caching;
   - supported/unsupported expression shapes.
9. A final implementation report listing:
   - files changed;
   - deliberate API deviations from this specification;
   - remaining limitations;
   - `dotnet test` result;
   - `dotnet pack` result.

If an acceptance criterion cannot be implemented cleanly, Codex must **document the blocker and the smallest alternative**, not silently omit the feature.

---

# 31. Canonical target usage example

The implementation should make this style of code natural:

```csharp
using Raffinert.Expressions;

var total = Projection<Order, decimal>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Specification<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var activeCustomer = Specification<Customer>.Create(customer =>
    customer.IsActive);

var customer = Projection<Order, Customer>.Create(order =>
    order.Customer);

var activeCustomerOrder = customer.Then(activeCustomer);

var projection = Projection<Order, OrderDto>.Create(order => new OrderDto
{
    Id = order.Id,
    Total = total.Invoke(order),
    IsExpensive = expensive.Invoke(order),
    HasActiveCustomer = activeCustomer.Invoke(order.Customer)
});

var result = await db.Orders
    .Where(expensive & activeCustomerOrder)
    .Select(projection)
    .ToArrayAsync();
```

The SQL/LINQ provider must receive an ordinary fully expanded expression tree equivalent to handwritten inline query logic.

That is the core success condition of `Raffinert.Expressions`.
