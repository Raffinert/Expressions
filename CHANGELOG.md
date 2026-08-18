# Changelog

## Unreleased

- Changed `MapToExisting` to preserve, clear, and refill existing mutable collection members instead of replacing them.
- Added support for collection-initializer bindings, null/empty collection behavior, missing writable collections, and aliased source/destination collections.
- Kept AutoMapper-compatible replacement behavior for arrays, writable `IEnumerable<T>` members, and known read-only collection wrappers.

## 1.0.0 - 2026-08-17

- Aggregated reusable specification and projection APIs in `Raffinert.Expressions`.
- Renamed `Spec<T>` and `Proj<TIn,TOut>` to `Specification<T>` and `Projection<TSource,TResult>`.
- Renamed `Expr<TIn,TOut>` to `ComposableExpression<TSource,TResult>` and clarified the internal expression-expansion contract.
- Added a shared expression expansion engine and canonical `Invoke` composition API.
- Added mixed `Specification`/`Projection` composition and typed `Then` composition.
- Added a single canonical `Invoke` API, shared `InvokeOrDefault` null/default lifting, LINQ extensions, method-group expansion, and debugger views.
- Removed the legacy `IsSatisfiedBy`, `Map`, and `MapIfNotNull` aliases in favor of the canonical methods.
- Changed constant `Specification<T>.True` and `Specification<T>.False` factories into cached static properties.
- Added deterministic binding conflict policies and safer map-to-existing behavior.
- Added direct structural source adaptation for specifications and source/result adaptation for projections.
- Added unit and EF Core SQLite integration coverage.
