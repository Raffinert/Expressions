# Implementation report

## Delivered

- `Raffinert.Expressions.slnx` with a `netstandard2.0` runtime package.
- One shared `ComposableExpression<TSource,TResult>` core, invocation expansion engine, safe closure/member target resolution, scoped parameter substitution, cycle detection, and per-instance caches.
- Complete semantic `Specification<T>` and `Projection<TSource,TResult>` APIs, canonical `Invoke`/`InvokeOrDefault`, LINQ extensions, mixed cross-composition, method-group expansion, null-safe/default invocation, and `Then`.
- Deterministic `MergeBindings`, safer `MapToExisting`, structural `ExpressionTemplate`, and `SpecificationTemplate` APIs.
- Descriptive runtime validation for structural template creation and adaptation.
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
- `src/Raffinert.Expressions/Templates/*`
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
- `ExpressionTemplate` initially produces specifications (Boolean results). A generalized arbitrary result selector was intentionally left out as allowed by the staged compatibility path.
- A separate analyzer package is deliberately omitted; runtime template validation is authoritative and avoids incomplete or inconsistent compile-time diagnostics.
- Legacy binary compatibility packages were not created; they are explicitly outside the MVP.
- `MapToExisting` does not automatically construct missing nested destination objects. It throws an explicit `InvalidOperationException`.

## Remaining limitations

- `MergeBindings` requires parameterless member-initializer construction and simple member assignments. Supported conditional branches must bind compatible member sets.
- A root `MapToExisting` conditional branch that returns null throws at runtime because `Action<TIn,TOut>` cannot replace the caller's existing root reference. Nested null branches assign null normally.
- Template shapes are intentionally limited to direct sample-member reads with preserved names. Arbitrary computed structural shapes are rejected.
- Template-shape and target-member errors are reported when templates are constructed or adapted rather than at compile time.

## Verification

Run on 2026-08-17:

- `dotnet test Raffinert.Expressions.slnx -c Release`: passed, 34 tests (28 unit and 6 EF Core integration).
- `dotnet format Raffinert.Expressions.slnx --verify-no-changes --no-restore`: passed.
- `dotnet pack` for `Raffinert.Expressions`: passed and produced both `.nupkg` and portable-PDB `.snupkg` packages.
- Package contents verified: the runtime DLL and XML documentation are in the expected NuGet paths.
