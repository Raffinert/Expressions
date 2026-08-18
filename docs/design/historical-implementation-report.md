# Implementation report

> [!NOTE]
> This is a historical delivery record from the initial implementation. It is retained for design archaeology, is not maintained as product documentation, and may not describe the current API or verification counts. In particular, it uses the former `Specification<T>` name; the current API is `Condition<T>`. See the [README](../../README.md), [changelog](../../CHANGELOG.md), [migration guide](../migration.md), and [architecture document](../architecture.md) for current behavior.

## Delivered

- `Raffinert.Expressions.slnx` with a `netstandard2.0` runtime package.
- One shared `ComposableExpression<TSource,TResult>` core, invocation expansion engine, safe closure/member target resolution, scoped parameter substitution, cycle detection, and per-instance caches.
- Complete semantic `Specification<T>` and `Projection<TSource,TResult>` APIs, canonical `Invoke`/`InvokeOrDefault`, LINQ extensions, mixed cross-composition, method-group expansion, null-safe/default invocation, and `Then`.
- Deterministic `MergeBindings`, safer `MapToExisting`, and direct structural adaptation APIs.
- Unit and EF Core SQLite integration test projects.
- Package metadata, README, migration guide, changelog, architecture notes, and MIT license.

## Files added or changed

Build and packaging:

- `Raffinert.Expressions.slnx`
- `Directory.Build.props`
- `src/Raffinert.Expressions/Raffinert.Expressions.csproj`
- both test project files

Runtime:

- `src/Raffinert.Expressions/Core/*`
- `src/Raffinert.Expressions/Specifications/*`
- `src/Raffinert.Expressions/Projections/*`
- `src/Raffinert.Expressions/Extensions/*`
- `src/Raffinert.Expressions/Debugging/*`

Tests:

- `tests/Raffinert.Expressions.UnitTests/*`
- `tests/Raffinert.Expressions.IntegrationTests/*`

Documentation:

- `README.md`
- `MIGRATION.md`
- `docs/architecture.md`
- `CHANGELOG.md`
- `IMPLEMENTATION_REPORT.md`
- `LICENSE`
- `.gitignore`

## Deliberate API/design choices

- `ComposableExpression<TSource,TResult>` is public because it is a useful supported base for new semantic expression wrappers, while ordinary `Specification`/`Projection` users do not need to reference it.
- Compatibility aliases `IsSatisfiedBy`, `Map`, and `MapIfNotNull` are deliberately omitted. `Invoke` is the sole normal invocation API and `InvokeOrDefault` is the explicit null-input/default-output API.
- Existing specifications and projections serve as their own structural definitions; no separate template abstraction is exposed.
- Legacy binary compatibility packages were not created; they are explicitly outside the MVP.
- `MapToExisting` preserves and updates existing nested destination objects, and constructs missing writable nested destinations from the projection. Existing mutable collections are preserved, cleared, and refilled; arrays and writable `IEnumerable<T>` members are replaced. Missing read-only nested destinations throw an explicit `InvalidOperationException`.

## Remaining limitations

- `MergeBindings` requires parameterless member-initializer construction and simple member assignments. Supported conditional branches must bind compatible member sets.
- A root `MapToExisting` conditional branch that returns null throws at runtime because `Action<TIn,TOut>` cannot replace the caller's existing root reference. Nested null branches assign null normally.
- Collection updates replace the contents wholesale; existing elements are not matched or updated by key.
- Structural source adaptation supports parameter-rooted public property and field paths. Result adaptation requires
  parameterless member initializers; source-specific method calls and constructor projections are rejected.

## Verification

Run on 2026-08-17:

- `dotnet test Raffinert.Expressions.slnx -c Release`: passed, 35 tests (29 unit and 6 EF Core integration).
- `dotnet format Raffinert.Expressions.slnx --verify-no-changes --no-restore`: passed.
- `dotnet pack` for `Raffinert.Expressions`: passed and produced both `.nupkg` and portable-PDB `.snupkg` packages.
- Package contents verified: the runtime DLL and XML documentation are in the expected NuGet paths.
