# Implementation report

## Delivered

- `Raffinert.Expressions.slnx` with a `netstandard2.0` runtime package and analyzer package.
- One shared `Expr<TIn,TOut>` core, invocation expansion engine, safe closure/member target resolution, scoped parameter substitution, cycle detection, and per-instance caches.
- Complete semantic `Spec<T>` and `Proj<TIn,TOut>` APIs, canonical `Invoke`/`InvokeOrDefault`, LINQ extensions, mixed cross-composition, method-group expansion, null-safe/default invocation, and `Then`.
- Deterministic `MergeBindings`, safer `MapToExisting`, structural `ExpressionTemplate`, and the `SpecTemplate` compatibility facade.
- Analyzer diagnostics REX001, REX002, and REX003.
- Unit, analyzer, and EF Core SQLite integration test projects.
- Package metadata, README, migration guide, changelog, architecture notes, and MIT license.

## Files added or changed

Build and packaging:

- `Raffinert.Expressions.slnx`
- `Directory.Build.props`
- `src/Raffinert.Expressions/Raffinert.Expressions.csproj`
- `src/Raffinert.Expressions.Analyzers/Raffinert.Expressions.Analyzers.csproj`
- all three test project files

Runtime:

- `src/Raffinert.Expressions/Core/*`
- `src/Raffinert.Expressions/Specifications/*`
- `src/Raffinert.Expressions/Projections/*`
- `src/Raffinert.Expressions/Templates/*`
- `src/Raffinert.Expressions/Extensions/*`
- `src/Raffinert.Expressions/Debugging/*`

Analyzer and tests:

- `src/Raffinert.Expressions.Analyzers/ExpressionTemplateAnalyzer.cs`
- `tests/Raffinert.Expressions.UnitTests/*`
- `tests/Raffinert.Expressions.IntegrationTests/*`
- `tests/Raffinert.Expressions.Analyzers.Tests/*`

Documentation:

- `README.md`
- `MIGRATION.md`
- `docs/architecture.md`
- `CHANGELOG.md`
- `IMPLEMENTATION_REPORT.md`
- `LICENSE`
- `.gitignore`

## Deliberate API/design choices

- `Expr<TIn,TOut>` is public because it is a useful supported base for new semantic expression wrappers, while ordinary `Spec`/`Proj` users do not need to reference it.
- Compatibility aliases `IsSatisfiedBy`, `Map`, and `MapIfNotNull` are deliberately omitted. `Invoke` is the sole normal invocation API and `InvokeOrDefault` is the explicit null-input/default-output API.
- `ExpressionTemplate` initially produces specifications (Boolean results). A generalized arbitrary result selector was intentionally left out as allowed by the staged compatibility path.
- REX004 was not added because no separate unsupported-composition diagnostic is needed by the implemented API.
- Legacy binary compatibility packages were not created; they are explicitly outside the MVP.
- `MapToExisting` does not automatically construct missing nested destination objects. It throws an explicit `InvalidOperationException`.

## Remaining limitations

- `MergeBindings` requires parameterless member-initializer construction and simple member assignments. Supported conditional branches must bind compatible member sets.
- A root `MapToExisting` conditional branch that returns null throws at runtime because `Action<TIn,TOut>` cannot replace the caller's existing root reference. Nested null branches assign null normally.
- Template shapes are intentionally limited to direct sample-member reads with preserved names. Arbitrary computed structural shapes are rejected.
- The analyzer follows direct `Create(...).Adapt...()` chains and locally declared field/property/local initializers; runtime validation remains authoritative for more dynamic construction patterns.

## Verification

Run on 2026-08-17:

- `dotnet test Raffinert.Expressions.slnx -c Release`: passed, 38 tests (28 unit, 6 EF Core integration, 4 analyzer).
- `dotnet format Raffinert.Expressions.slnx --verify-no-changes --no-restore`: passed.
- `dotnet pack` for `Raffinert.Expressions`: passed and produced both `.nupkg` and portable-PDB `.snupkg` packages.
- `dotnet pack` for `Raffinert.Expressions.Analyzers`: passed.
- Package contents verified: runtime DLL/XML documentation and analyzer DLL are in the expected NuGet paths.
