# Changelog

## 1.2.0 (unreleased)

- Added the optional `Raffinert.Expressions.EntityFrameworkCore` package, requiring .NET 10 and EF Core/Relational >= 10.0.11 and < 11.0.0. EF Core 7/8/9 are unsupported.
- Added async composable-condition overloads for Any, All, Count, LongCount, First, FirstOrDefault, Single, SingleOrDefault, Last and LastOrDefault. They expand before calling EF and need no interceptor.
- Added opt-in `UseRaffinertExpressions()` and `RaffinertExpressionInterceptor` for ordinary LINQ invocation markers, with public EF service integration for extracted wrapper values and expanded cache keys.
- Reused the core expansion engine through an internal whole-expression entry point. Core remains netstandard2.0 and has no EF dependency.
- Fixed embedded native wrapper invocation through `IComposableExpression<,>` interfaces/casts; unrelated methods named Invoke remain untouched.
- Lifted embedded scalar captures into native EF execution parameters, including DateOnly/TimeOnly and nullable values; cache keys and compilation share one prepared expression and changing thresholds reuse one cache shape.
- Added deterministic EF-style lifted parameter names derived from captured member paths, with bounded ASCII identifiers and case-insensitive collision avoidance. Runtime values remain bound separately.
- Prevented captured SQL literals, sanitized getter diagnostics and rejected ToQueryString rendering of lifted values. Verified pooled contexts/factories, deferred execution and interceptor composition. Explicit compiled wrappers with runtime captures fail safely; use scalar delegate parameters.
- Replaced version-dependent reflection with native EF10 QueryParameterExpression and QueryContext.Parameters APIs, retaining automatic runtime lifting.
- Preserved embedded EF.Constant / EF.Parameter semantics for scalar captures and literals in conditions and projections, with sanitized rejection of unsupported operands. Explicit constants intentionally appear in SQL.
- Added capture/service-composition regressions and isolated EF10 NuGet consumers on Windows and Linux; removed obsolete compatibility projects and matrix.
- Added EF10 SQLite and real Windows SQL Server LocalDB execution, cache, nullability, cancellation, terminal semantics and compiled-query coverage.
- Bumped QuerySyntax alongside core to align its package dependency; its API and behavior remain unchanged.


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
