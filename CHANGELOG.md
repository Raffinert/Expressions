# Changelog

## Raffinert.Expressions.EntityFrameworkCore 1.2.1 - 2026-10-21

- Added configurable, ordered post-expansion expression transformations through `UseRaffinertExpressions(options => options.Transform(...))`.
- Custom transformers run after Raffinert invocation expansion and before EF Core's native parameter extraction for ordinary synchronous and asynchronous queries.
- Added SQLite integration coverage using the optional third-party `Linq.Expression.Optimizer`, including transformation order, configuration isolation, and invalid transformer results.
- The Core and QuerySyntax package APIs and versions are unchanged.

## 1.2.0

- Added the optional `Raffinert.Expressions.EntityFrameworkCore` package for .NET 10 and EF Core/Relational >= 10.0.11 and < 11.0.0.
- Added composable-condition async overloads for Any, All, Count, LongCount, First, FirstOrDefault, Single, SingleOrDefault, Last and LastOrDefault, with cancellation support and no registration requirement.
- Added opt-in `UseRaffinertExpressions()` expansion for conditions, projections, nested invocations, typed interfaces and Raffinert delegate callbacks in ordinary EF LINQ queries.
- Supports native EF parameterization and caching, current captured values, computed operands, `EF.Parameter`, `EF.Constant` and `EF.MultipleParameters`.
- Supports stable closed wrappers and scalar parameters in EF compiled queries. Runtime captures in compiled wrapper bodies and Raffinert invocation expansion in precompiled queries are unsupported.
- Follows native `ToQueryString()` behavior, which can include parameter values even with sensitive-data logging disabled.
- Added SQLite and real Windows SQL Server LocalDB integration coverage and isolated package consumers on Windows and Linux.
- Updated Core and QuerySyntax package versions together to 1.2.0; their provider-independent APIs and target frameworks are unchanged.


## Raffinert.Expressions.QuerySyntax 1.1.0 - 2026-10-05

- Bumped the satellite package alongside `Raffinert.Expressions` so the packaged dependency points to the 1.1.0 core release. The query-syntax API is unchanged.

## 1.1.0 - 2026-10-05

- Added `Condition<T>.Start()` for dynamic condition chains without a Boolean seed in the first composed expression. Empty chains return `false` by default or `true` with `defaultWhenEmpty: true`.
- Added unit and EF Core SQLite coverage for starter conditions.

## Raffinert.Expressions.QuerySyntax 1.0.3 - 2026-08-25

- Added the opt-in `AsRaffinertQuery()` facade for expanding reusable conditions and projections throughout C# LINQ query syntax without replacing the underlying provider.
- Added full query-expression-pattern support, including multiple `from` clauses, joins, group joins, ordering, grouping, continuations, and explicit range types.
- Added provider-independent `ToListAsync` and `ToArrayAsync` forwarding on `netstandard2.1`.
- Added query-syntax unit tests and EF Core SQLite translation and async tests.

## 1.0.3 - 2026-08-25

- Added the internal expression-expansion seam used by `Raffinert.Expressions.QuerySyntax` while keeping the core package public API focused on expression composition and LINQ method-style extensions.
- Kept the core package on `netstandard2.0` with no new runtime dependency.
- Added queryable and enumerable composable-expression overloads for condition terminals, ordering, grouping, and flattening.
- Added unit coverage for the full extension surface and EF Core SQLite coverage for expression expansion and translation.

## 1.0.1 - 2026-08-24

- Added `Projection<TSource>.Create(...)` for inferring projection result types from the expression, including anonymous types.
- Updated unit and EF Core integration coverage to use the inferred projection factory.

## 1.0.0 - 2026-08-18

- Aggregated reusable condition and projection APIs in `Raffinert.Expressions`.
- Renamed `Spec<T>` and `Proj<TIn,TOut>` to `Condition<T>` and `Projection<TSource,TResult>`.
- Renamed `Expr<TIn,TOut>` to `ComposableExpression<TSource,TResult>` and clarified the internal expression-expansion contract.
- Added a shared expression expansion engine and canonical `Invoke` composition API.
- Added mixed `Condition`/`Projection` composition and typed `Then` composition.
- Added a single canonical `Invoke` API, shared `InvokeOrDefault` null/default lifting, LINQ extensions, method-group expansion, and debugger views.
- Removed the legacy `IsSatisfiedBy`, `Map`, and `MapIfNotNull` aliases in favor of the canonical methods.
- Changed constant `Condition<T>.True` and `Condition<T>.False` factories into cached static properties.
- Added deterministic binding conflict policies and safer map-to-existing behavior.
- Added direct structural source adaptation for conditions and source/result adaptation for projections.
- Added `MapToExisting` clear-and-refill semantics for mutable collections, collection-initializer bindings, null/empty handling, missing writable collections, and aliased source/destination collections.
- Kept replacement behavior for arrays, writable `IEnumerable<T>` members, and known read-only collection wrappers.
- Added unit and EF Core SQLite integration coverage.
