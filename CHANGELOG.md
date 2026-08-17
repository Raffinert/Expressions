# Changelog

## 1.0.0 - 2026-08-17

- Aggregated reusable specification and projection APIs in `Raffinert.Expressions`.
- Added a shared expression expansion engine and canonical `Invoke` composition API.
- Added mixed `Spec`/`Proj` composition and typed `Then` composition.
- Added a single canonical `Invoke` API, shared `InvokeOrDefault` null/default lifting, LINQ extensions, method-group expansion, and debugger views.
- Removed the legacy `IsSatisfiedBy`, `Map`, and `MapIfNotNull` aliases in favor of the canonical methods.
- Changed constant `Spec<T>.True` and `Spec<T>.False` factories into cached static properties.
- Added deterministic binding conflict policies and safer map-to-existing behavior.
- Added generalized structural expression templates with a `SpecTemplate` compatibility facade.
- Added `Raffinert.Expressions.Analyzers` diagnostics REX001–REX003.
- Added unit, Roslyn analyzer, and EF Core SQLite integration coverage.
