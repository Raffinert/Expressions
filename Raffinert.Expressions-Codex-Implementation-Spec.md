# Codex Implementation Specification: `Raffinert.Expressions`

## 0. Mission

Implement a new aggregated library named **`Raffinert.Expressions`** by consolidating the useful concepts of:

- `Raffinert.Spec`
- `Raffinert.Proj`

The new library must preserve `Spec<T>` and `Proj<TIn,TOut>` as first-class semantic APIs, while moving their shared behavior onto a single reusable expression-composition core.

The primary architectural idea is:

> `Spec<T>` and `Proj<TIn,TOut>` are specialized typed wrappers around reusable LINQ expression trees. They must be composable with each other, expandable into pure `Expression<TDelegate>` trees, executable in memory, and consumable by EF Core or other LINQ providers without a custom query provider or `AsExpandable()`-style interception.

The implementation should favor **small, explicit expression-tree transformations** over framework magic.

---

# 1. Source repositories and current behavior

Use the current public repositories as the behavioral source material:

- `https://github.com/Raffinert/Raffinert.Spec`
- `https://github.com/Raffinert/Raffinert.Proj`

Important existing behavior to preserve unless this specification explicitly changes it:

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
- specification templates and analyzer behavior

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
    Templates/
    Extensions/
    Debugging/

  Raffinert.Expressions.Analyzers/

tests/
  Raffinert.Expressions.UnitTests/
  Raffinert.Expressions.IntegrationTests/
  Raffinert.Expressions.Analyzers.Tests/

benchmarks/
  Raffinert.Expressions.Benchmarks/      # optional, only after functional completion
```

Keep the runtime package free of EF Core dependencies.

Target **`netstandard2.0`** unless a concrete implementation blocker is found. The existing libraries target `netstandard2.0`, so preserving that compatibility is preferred.

Nullable reference types must remain enabled.

---

# 3. Core conceptual model

Introduce an internal/shared abstraction representing a reusable expression from `TIn` to `TOut`.

The public design may expose `Expr<TIn,TOut>` if doing so materially improves usability, but **do not require users to know or use it** for normal `Spec`/`Proj` scenarios.

Preferred design:

```csharp
public abstract class Expr<TIn, TOut>
{
    public abstract Expression<Func<TIn, TOut>> GetExpression();

    public Expression<Func<TIn, TOut>> GetExpandedExpression();

    public TOut Invoke(TIn value);
}
```

Then:

```csharp
public abstract class Spec<T> : Expr<T, bool>
{
    // specification-specific API
}

public abstract class Proj<TIn, TOut> : Expr<TIn, TOut>
{
    // projection-specific API
}
```

If inheritance introduces unacceptable API or implementation complexity, use an internal interface/base class instead, but the following invariant is mandatory:

> `Spec` and `Proj` must share one expansion engine and one generalized expression-node abstraction.

There must not be separate near-duplicate `IsSatisfiedByCallVisitor` and `MapCallVisitor` implementations in the final architecture.

---

# 4. Canonical invocation API

Introduce **`Invoke`** as the canonical expression-composition marker.

## Core behavior

```csharp
TOut Invoke(TIn value)
```

At runtime, outside an expression tree, this executes the compiled expression.

Inside a parent expression tree, calls to `Invoke(...)` on a supported `Expr`/`Spec`/`Proj` instance must be **inlined** by `GetExpandedExpression()`.

Example:

```csharp
var categoryProjection = Proj<Category, CategoryDto>.Create(c => new CategoryDto
{
    Name = c.Name
});

var productProjection = Proj<Product, ProductDto>.Create(p => new ProductDto
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
public TOut Map(TIn value) => Invoke(value);
```

The expansion engine must recognize both the canonical `Invoke` API and legacy semantic aliases during the migration period.

`Invoke` should be the API used in new documentation for cross-composition.

---

# 5. Cross-composition: mandatory feature

This is one of the primary goals of the aggregation.

## 5.1 `Spec` inside `Proj`

Required:

```csharp
Spec<Product> expensive =
    Spec<Product>.Create(p => p.Price > 100m);

Proj<Product, ProductDto> projection =
    Proj<Product, ProductDto>.Create(p => new ProductDto
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

## 5.2 `Proj` inside `Spec`

Required:

```csharp
Proj<Order, decimal> total =
    Proj<Order, decimal>.Create(o =>
        o.Lines.Sum(x => x.Price * x.Quantity));

Spec<Order> expensive =
    Spec<Order>.Create(o => total.Invoke(o) > 1000m);
```

Expanded expression must be equivalent to:

```csharp
o => o.Lines.Sum(x => x.Price * x.Quantity) > 1000m
```

## 5.3 Arbitrarily nested composition

The expansion engine must recursively expand until the resulting tree has no recognized library invocation markers.

Example chain:

```text
Spec -> Proj -> Proj -> Spec
```

must be supported when type-compatible.

Detect cycles where practical. A self-referential expression graph must fail deterministically with a meaningful exception rather than causing infinite recursion/stack overflow.

---

# 6. Generic composition API (`Then` / composition algebra)

Implement typed composition as a first-class feature after core invocation expansion works.

## Required overloads

Conceptually:

```csharp
Proj<TIn, TNext> Then<TNext>(Proj<TOut, TNext> next);
```

and:

```csharp
Spec<TIn> Then(Spec<TOut> next);
```

for `Proj<TIn,TOut>`.

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
Proj<Order, Customer> customer = ...;
Proj<Customer, string> name = ...;

Proj<Order, string> customerName = customer.Then(name);
```

```csharp
Proj<Order, Customer> customer = ...;
Spec<Customer> active = ...;

Spec<Order> orderWithActiveCustomer = customer.Then(active);
```

The resulting expressions must be direct composed expression trees, not delegate invocation expressions unsupported by common LINQ providers.

Do not add a large functional-programming API beyond what is necessary for clear expression composition.

---

# 7. Expression expansion engine

Implement one generalized expansion engine.

Suggested internal abstraction:

```csharp
internal interface IExpandableExpression
{
    LambdaExpression GetExpressionUntyped();
    LambdaExpression GetExpandedExpressionUntyped();
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
- direct `new SomeSpec(...)` / `new SomeProj(...)` cases already supported by current libraries;
- method-group scenarios generated by C# for `Any(spec.Invoke)` / `Select(proj.Invoke)`;
- closure member access chains where safely resolvable.

Avoid assuming closure storage is always a single `ConstantExpression -> FieldInfo` pair.

Implement a safe member-value evaluator for constant/closure-rooted member chains rather than compiling arbitrary user expression subtrees whenever possible.

Do **not** execute arbitrary parameter-dependent expression code at expansion time.

## No `Expression.Invoke` in final provider-facing trees

The final expanded tree must inline composition. It must not rely on `InvocationExpression`/`Expression.Invoke` as the core mechanism, because provider compatibility is a central goal.

---

# 8. `Spec<T>` API

Preserve the semantic specification type.

Required public API shape:

```csharp
public abstract class Spec<T> : Expr<T, bool>
{
    public abstract override Expression<Func<T, bool>> GetExpression();

    public static Spec<T> Create(Expression<Func<T, bool>> expression);

    public static Spec<T> True();
    public static Spec<T> False();

    public bool IsSatisfiedBy(T candidate);

    public Spec<T> And(Spec<T> spec);
    public Spec<T> And(Expression<Func<T, bool>> expression);

    public Spec<T> Or(Spec<T> spec);
    public Spec<T> Or(Expression<Func<T, bool>> expression);

    public Spec<T> Not();

    public static Spec<T> operator &(Spec<T> left, Spec<T> right);
    public static Spec<T> operator |(Spec<T> left, Spec<T> right);
    public static Spec<T> operator !(Spec<T> spec);
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

# 9. `Proj<TIn,TOut>` API

Preserve the semantic projection type.

Required core API:

```csharp
public abstract class Proj<TIn, TOut> : Expr<TIn, TOut>
{
    public abstract override Expression<Func<TIn, TOut>> GetExpression();

    public static Proj<TIn, TOut> Create(Expression<Func<TIn, TOut>> expression);

    public TOut Map(TIn value);

    public TOut? MapIfNotNull(TIn? value);

    public Proj<TIn, TOut> MergeBindings(Proj<TIn, TOut> other);
    public Proj<TIn, TOut> MergeBindings(Expression<Func<TIn, TOut>> other);

    public Expression<Action<TIn, TOut>> GetMapToExistingExpression();
    public Action<TIn, TOut> GetMapToExistingAction();
    public void MapToExisting(TIn source, ref TOut? destination);
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

Existing behavior currently recursively updates nested member initializers. Preserve this only where destination nested instances are non-null and semantics are explicit.

Do not accidentally dereference an existing nested destination that can be null.

Define and test behavior for:

- existing nested object present;
- destination nested object null;
- source-side conditional producing null;
- nested projection expanded through `Invoke`.

If automatic destination-object construction is not implemented, throw a clear exception rather than allowing an opaque `NullReferenceException` during a compiled mapper.

## Unsupported projection bodies

Throw `NotSupportedException` with a descriptive message for unsupported shapes instead of producing malformed expression trees.

---

# 13. Queryable and Enumerable integration

Preserve direct ergonomic usage.

## IQueryable

```csharp
IQueryable<T> Where<T>(this IQueryable<T> source, Spec<T> spec)
```

must call:

```csharp
source.Where(spec.GetExpandedExpression())
```

and:

```csharp
IQueryable<TResult> Select<TSource,TResult>(
    this IQueryable<TSource> source,
    Proj<TSource,TResult> projection)
```

must call:

```csharp
source.Select(projection.GetExpandedExpression())
```

## IEnumerable

Equivalent overloads should execute compiled delegates.

Keep extension class names/namespaces non-conflicting. Avoid placing generic classes named simply `Queryable` / `Enumerable` in the global namespace. Use descriptive static classes such as:

```csharp
public static class SpecQueryableExtensions
public static class ProjQueryableExtensions
public static class SpecEnumerableExtensions
public static class ProjEnumerableExtensions
```

all under `Raffinert.Expressions` or `Raffinert.Expressions.Extensions`.

---

# 14. Expression templates / structural adaptation

Do not leave the current feature permanently coupled only to specifications.

The long-term design should be generalized as **`ExpressionTemplate`**.

Implement this after core aggregation and cross-composition are stable.

## Goal

Allow an expression defined against a sample structural shape to adapt to another type that exposes compatible members.

Example concept:

```csharp
var template = ExpressionTemplate<Product>.Create(
    p => new { p.Name, p.Price },
    x => x.Price > 10m && x.Name != null);

Spec<InventoryItem> adapted = template.AdaptSpec<InventoryItem>();
```

A generalized result-selector form may later support non-boolean outputs.

## Minimum compatibility path

If generalization would create excessive scope for the first implementation, preserve `SpecTemplate` as a compatibility facade implemented on top of a new internal template engine.

Do not duplicate two separate template engines.

## Correctness fixes required

Fix existing issues during migration:

1. Missing destination members must actually be detected.
2. Field members validated as compatible must not later be rewritten using property-only APIs.
3. Use `Expression.PropertyOrField` or resolved `MemberInfo` appropriately.
4. Validate readable member access.
5. Handle duplicate public member names safely; do not assume `GetMembers(...).ToDictionary(x => x.Name)` can never conflict.
6. Validate type equality/assignability deliberately rather than relying on reflection object-type equality alone.
7. Preserve nested lambda scopes.

---

# 15. Analyzer migration

Create/rename analyzer package:

`Raffinert.Expressions.Analyzers`

Migrate the intent of existing `SpecTemplateCreateAnalyzer` and `SpecTemplateAdaptAnalyzer`.

## Required analyzer goals

- reject unsupported template shape at compile time where statically detectable;
- detect missing target members for `Adapt...<TTarget>()` where type is statically known;
- validate property/field type compatibility;
- produce actionable diagnostic text;
- include tests for both positive and negative cases.

Suggested diagnostic IDs:

```text
REX001 Unsupported expression-template shape
REX002 Target type is missing a required member
REX003 Target member has incompatible type
REX004 Unsupported composition target (only if needed)
```

Do not emit diagnostics for valid normal expression usage.

Analyzers must not be required for runtime correctness.

---

# 16. Debugging experience

Preserve custom debugger support.

`Spec` and `Proj` should expose a useful `DebuggerDisplay` showing the **expanded expression**, because that is what the LINQ provider receives.

Avoid duplicating almost-identical debugger infrastructure where generic helpers can be shared.

The debugger proxy should make at least these available:

- Original expression
- Expanded expression
- Compiled/runtime representation where useful

Do not add runtime dependencies solely for pretty-printing expressions.

---

# 17. Caching rules

Cache only deterministic results owned by an immutable expression wrapper instance.

Reasonable caches:

- compiled delegate;
- expanded expression;
- map-to-existing expression/action.

Do not globally cache expression trees keyed by arbitrary objects unless there is a demonstrated need.

Document an important invariant:

> Implementations of `GetExpression()` are assumed stable for the lifetime of a `Spec` / `Proj` instance once expansion or compilation has been requested.

If a subclass returns a materially different expression on each call, cached behavior is undefined/not supported.

---

# 18. EF/LINQ provider compatibility principles

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

# 19. Backward compatibility strategy

This is a new package and namespace, so binary compatibility with old NuGets is not required, but source migration should be deliberately easy.

## Keep names

Keep:

```text
Spec<T>
Proj<TIn,TOut>
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
Expr<TIn,TOut>          # if public architecture chooses it
Invoke
Then
ExpressionTemplate      # staged if needed
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

# 20. API naming conventions

Use semantic types for user code:

```csharp
Spec<Order>
Proj<Order, OrderDto>
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

# 21. Required tests

The implementation is not complete until these scenarios are covered.

## 21.1 Core expression tests

- parameter replacement;
- nested lambda parameter shadowing;
- captured constant values;
- captured spec/proj field;
- direct `new SpecSubclass(...)` invocation;
- direct `new ProjSubclass(...)` invocation;
- nested expansion depth > 2;
- cycle behavior;
- expanded tree contains no library `Invoke` marker calls.

## 21.2 Spec tests

- inline `Create`;
- subclass;
- `True` / `False`;
- `And`;
- `Or`;
- `Not`;
- operators;
- `IEnumerable.Where(spec)`;
- `IQueryable.Where(spec)`;
- nested `Spec.Invoke`;
- compatibility `IsSatisfiedBy` expansion;
- method group in `Any`.

## 21.3 Proj tests

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

## 21.4 Cross-composition tests

Mandatory:

```text
Spec inside Proj
Proj inside Spec
Spec -> Proj -> Spec nested chain
Proj -> Proj -> Proj nested chain
Proj.Then(Proj)
Proj.Then(Spec)
```

Prove both:

1. runtime compiled execution;
2. provider-facing expanded expression.

## 21.5 EF Core integration tests

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

## 21.6 Template tests

- anonymous object shape;
- explicit template class/member-init;
- property target;
- field target;
- missing member;
- wrong member type;
- readonly/unreadable member rejection where applicable;
- structural adaptation used with EF query.

---

# 22. Test style

Prefer behavioral tests over exact `Expression.ToString()` comparisons.

Exact expression-string tests may be retained for a small number of expansion-shape assertions, but they must not be the only correctness proof.

Where structure matters, inspect `ExpressionType`, member names, parameter identity, and absence of marker calls.

Use clear Arrange/Act/Assert structure.

Avoid brittle tests tied to compiler-generated closure class names.

---

# 23. Performance expectations

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
- `Spec` composed expression expansion;
- `Proj` nested expression expansion;
- compiled runtime execution after warm-up.

Do not optimize by sacrificing API clarity or correctness before profiling.

---

# 24. Exception/error design

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

# 25. README requirements

Rewrite the new root README around the actual niche, not around "yet another specification library".

Suggested opening:

> **Raffinert.Expressions** is a lightweight expression-composition library for reusable predicates and projections. Compose normal C# expression objects and hand the resulting pure expression tree to EF Core or any other LINQ provider — without a custom query provider or special query interception.

The first example should demonstrate **cross-composition**, because that differentiates the aggregated library.

Example:

```csharp
var total = Proj<Order, decimal>.Create(o =>
    o.Lines.Sum(x => x.Price * x.Quantity));

var expensive = Spec<Order>.Create(o =>
    total.Invoke(o) > 1000m);

var projection = Proj<Order, OrderDto>.Create(o => new OrderDto
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
9. Structural expression templates
10. EF Core/provider compatibility
11. Runtime execution
12. Migration from `Raffinert.Spec` / `Raffinert.Proj`

Explicitly state:

- no EF dependency;
- no custom query provider;
- no `AsExpandable()` requirement;
- final expressions are ordinary expression trees.

---

# 26. NuGet metadata

Suggested package description:

> Lightweight composable expression trees for reusable specifications, projections, structural templates, and LINQ/EF Core query logic.

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

# 27. Implementation phases

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

## Phase 3 — `Spec`

- migrate `Spec<T>`;
- boolean composition;
- queryable/enumerable extensions;
- `Invoke` + `IsSatisfiedBy` compatibility;
- existing behavioral tests.

## Phase 4 — `Proj`

- migrate `Proj<TIn,TOut>`;
- `Invoke` + `Map` compatibility;
- null-safe projection;
- queryable/enumerable extensions;
- nested projection tests.

## Phase 5 — Cross-composition

- `Spec` inside `Proj`;
- `Proj` inside `Spec`;
- mixed deep nesting;
- method groups;
- EF integration tests.

This phase is a release blocker.

## Phase 6 — `Then`

- `Proj -> Proj`;
- `Proj -> Spec`;
- tests and documentation.

## Phase 7 — Projection advanced features

- hardened `MergeBindings`;
- explicit conflict behavior;
- hardened `MapToExisting`.

## Phase 8 — Templates/analyzers

- migrate template engine;
- fix current correctness defects;
- generalize toward `ExpressionTemplate`;
- migrate analyzers and tests.

## Phase 9 — Documentation and cleanup

- new README;
- migration guide;
- XML documentation on public APIs;
- package metadata;
- remove dead duplicated visitors/utilities;
- run formatting and all tests.

---

# 28. Acceptance criteria

The work is accepted only when all of the following are true:

- [ ] One shared expansion engine serves `Spec` and `Proj`.
- [ ] `Spec<T>` remains a first-class API.
- [ ] `Proj<TIn,TOut>` remains a first-class API.
- [ ] `Invoke` is available as common composition API.
- [ ] `IsSatisfiedBy` remains available.
- [ ] `Map` remains available.
- [ ] `Spec` can be used inside `Proj` and is inlined.
- [ ] `Proj` can be used inside `Spec` and is inlined.
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
- [ ] Analyzer tests pass.
- [ ] EF Core integration tests cover mixed `Spec`/`Proj` reuse.
- [ ] Runtime package has no EF Core dependency.
- [ ] Runtime package still targets `netstandard2.0` unless a documented blocker required change.
- [ ] Public APIs have XML documentation.
- [ ] README leads with expression composition/cross-reuse rather than Specification Pattern history.
- [ ] `dotnet test` passes for the entire solution.
- [ ] `dotnet pack` succeeds for runtime and analyzer packages.

---

# 29. Non-goals for the first release

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

# 30. Design guardrails for Codex

When there is ambiguity, follow these priorities in order:

1. Correct expression semantics.
2. LINQ-provider friendliness.
3. Strong static typing.
4. Simple public API.
5. Preservation of `Spec` and `Proj` semantic vocabulary.
6. Reuse of one shared expression core.
7. Backward source familiarity.
8. Performance.
9. Cleverness.

Do not solve a local issue by duplicating expansion logic.

Do not add public abstractions simply because an internal implementation needs them.

Do not make `Expr<TIn,TOut>` public unless it provides a useful supported user story beyond inheritance plumbing. If kept internal, still maintain the same conceptual architecture.

Do not silently broaden the library into a mapping framework.

---

# 31. Expected final Codex deliverables

Codex should finish with:

1. Compiling `Raffinert.Expressions.sln`.
2. Runtime package project.
3. Analyzer package project.
4. Unit tests.
5. EF integration tests.
6. Analyzer tests.
7. README.
8. Migration document from both legacy libraries.
9. Changelog/release notes for initial aggregated release.
10. A short architecture document explaining:
   - invocation expansion;
   - parameter substitution;
   - cross-composition;
   - caching;
   - supported/unsupported expression shapes.
11. A final implementation report listing:
   - files changed;
   - deliberate API deviations from this specification;
   - remaining limitations;
   - `dotnet test` result;
   - `dotnet pack` result.

If an acceptance criterion cannot be implemented cleanly, Codex must **document the blocker and the smallest alternative**, not silently omit the feature.

---

# 32. Canonical target usage example

The implementation should make this style of code natural:

```csharp
using Raffinert.Expressions;

var total = Proj<Order, decimal>.Create(order =>
    order.Lines.Sum(line => line.Price * line.Quantity));

var expensive = Spec<Order>.Create(order =>
    total.Invoke(order) > 1000m);

var activeCustomer = Spec<Customer>.Create(customer =>
    customer.IsActive);

var customer = Proj<Order, Customer>.Create(order =>
    order.Customer);

var activeCustomerOrder = customer.Then(activeCustomer);

var projection = Proj<Order, OrderDto>.Create(order => new OrderDto
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
