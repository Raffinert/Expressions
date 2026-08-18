# Changelog

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
