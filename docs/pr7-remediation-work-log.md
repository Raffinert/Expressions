# PR #7 remediation work log

The cache/literal policy in this historical remediation was subsequently replaced by
[runtime parameter lifting](pr7-parameter-lifting-validation.md).

## Baseline and decisions

- Initial PR head and reviewed SHA: `9658e5c83203105fe6b28d49047f5f3ee9214551` (identical).
- Work branch: `fix/pr7-review`. Untracked planning files preserved.
- SDK 10.0.401; global.json selects 10.0.400/latestPatch. Installed runtimes: 8.0.31, 9.0.3, 10.0.12; temporary .NET 6.0.36 installation retained for EF 7.
- Restore, Release build (zero warnings/errors), full solution tests (136: 57 core, 5 QuerySyntax, 14 existing integration, 60 adapter), formatting verification: passed.
- GitHub run [38043704241](https://github.com/Raffinert/Expressions/actions/runs/38043704241): verified through the GitHub API; all ten jobs succeeded at the initial head.
- Policy B selected: opt-in constant-snapshot interception. Full generic parameterization is outside this change; no cache-key-only workaround or new EF internal hooks.

## Phase 1: interface invocation

- Red: both core interface/cast cases retained `Invoke`; both SQLite tests failed translation before SQL (interface reassignment and nested interface composition).
- Fix: recognize the actual `ComposableExpression<,>` and `IComposableExpression<,>` method contracts; resolve the receiver with the existing evaluator. Restore EF parameters with the interface static type only when their runtime value is a compatible native wrapper.
- Native wrappers accessed through interfaces are supported. External implementations must use direct operators; marker expansion rejects them descriptively.
- Hidden/unrelated `Invoke` methods remain untouched; interface cycles still fail. These additional safety tests passed immediately after the fix; no further fix was introduced for them.
- Focused tests: 3 passed. Entire core: 62 passed. Entire adapter: 62 passed.

- Phase 1 matrix: 62 adapter tests passed on each of EF 7/8/9/10.

## Phase 2: cache policy evidence

- Diagnostic tests passed immediately; this confirms the existing performance trade-off, not a wrong-result defect. No production cache change was made.
- 25 changing values (100 through 124), then repeat 124: snapshot mode has 25 compilations / 25 SQL shapes / no threshold parameters; normal EF, outer scalar, direct async operator each have 1 compilation / 1 SQL shape / current threshold parameter.
- Same-value repetition reuses compilation; different wrapper structures at the same value return different correct IDs. Existing reassignment, outer parameter, and DateTime.UtcNow server-function tests retained.
- Focused diagnostics: 5 passed; entire adapter: 67 passed.

Observed SQL (EF 10; parameter names are provider/version-specific):

```sql
-- Snapshot, threshold 100 then 124; parameters [] in each execution
SELECT COUNT(*) FROM "Orders" AS "o" WHERE "o"."TotalCents" > 100;
SELECT COUNT(*) FROM "Orders" AS "o" WHERE "o"."TotalCents" > 124;

-- Recommended direct operator or ordinary EF; parameters [100] then [124]
SELECT COUNT(*) FROM "Orders" AS "o" WHERE "o"."TotalCents" > @threshold;
```

## Phases 3 and 4: capture and service regressions

- Red: DateOnly and TimeOnly captures failed SQL translation because scalar normalization omitted their types. Added them to the existing scalar snapshot allowlist; both now select ID 2 then ID 4 using literal values and no parameters.
- Red: hidden array/list tests expected a descriptive unsupported exception but received generic EF translation errors. Added explicit collection rejection during late normalization, with guidance to direct operators. Direct array reassignment and list-content mutation return IDs [1,2] then [4] through normal EF extraction. Explicit `Enumerable.Contains` avoids C# 14 choosing the span overload; no span translator was added.
- Immediate passes (no production changes): string, Guid, decimal, DateTimeOffset, enum, nullable value-to-null captures; interface wrapper holder property; shared-options interleaved contexts with separate closures; translation failure followed by success; standalone interceptor restriction; forwarding decorators in both registration orders.
- Scalar SQL is recorded in test output: string `'Desk'` -> `'Hidden'`; Guid `'00000002-0000-0000-0000-000000000000'` -> corresponding 4; decimal `'3.0'` -> `'6.0'`; DateTimeOffset `'2020-01-02 00:00:00+00:00'` -> day 4; enum 2 -> 4; nullable `= 1` -> `IS NULL`. Parameters are empty for these snapshots.
- Capture-focused: 11 passed. Service-focused: 5 passed. Entire adapter after both phases: 83 passed.
- Existing InvokeOrDefault reference/nullable-value tests, fixed/parameterized compiled query tests, provider translation failures and cancellation tests remain in the full suite.
- Service descriptor assertions verify IQueryContextFactory and ICompiledQueryCacheKeyGenerator are scoped; QueryExecutionState is scoped when installed. Default provider type descriptors and forwarding factory descriptors are exercised. Arbitrary instance descriptors, manually supplied internal providers, and extensions that discard decorated services are unsupported.
- Invariants: the singleton interceptor stores no execution state; the scoped QueryExecutionState has only a weak reference to the execution QueryContext. Shared-options contexts resolve distinct decorated services. Operations on one DbContext remain sequential under EF's normal concurrency rules.

## Phases 5 and 6: release validation

- Full solution: 164 passed (62 core, 5 QuerySyntax, 14 existing integration, 83 adapter), no failures/skips. Release restore/build passed with zero build warnings/errors.
- EF matrix: 83 passed on each of 7.0.20/net6.0/runtime6.0.36, 8.0.31/net8.0/runtime8.0.31, 9.0.20/net8.0/runtime8.0.31, 10.0.11/net10.0/runtime10.0.12. Compilation-count assertions run in every leg.
- Same EF 7-built adapter DLL in all four outputs: SHA256 `F110120F7A424916CBBFB412FEA5B875C29A192D8A93123282B40AB19933EB03` at local source validation (commit metadata changes can alter this).
- All four isolated package consumers passed using locally packed 1.2.0 `.nupkg` files, no ProjectReferences, private package caches and local-source mapping. They verify interface expansion/reassignment, direct scalar captures, external interface direct operators, lambda/no-predicate/condition AnyAsync overloads and typed-null rejection.
- Packaging: core has no dependencies; adapter depends only on core 1.2.0 and EF Core 7.0.20; QuerySyntax depends only on core 1.2.0. QuerySyntax source/API is unchanged. All three packages and symbol packages built.
- Formatting verification covers the solution and the separate package consumer. `git diff --check` passed.
- CI now runs package consumers as well as the existing full compatibility suite on both operating systems. Original-head CI was successful; the completion report records the final SHA and its new CI result.

## Changed files

- `.github/workflows/ci.yml`, `CHANGELOG.md`, `README.md`
- `docs/efcore-integration.md`, `docs/efcore-validation.md`, `docs/pr7-remediation-work-log.md`
- `src/Raffinert.Expressions/Core/ExpressionExpander.cs`
- `src/Raffinert.Expressions.EntityFrameworkCore/EfQueryExpansion.cs`, `QueryExecutionState.cs`, `RaffinertDbContextOptionsBuilderExtensions.cs`
- `tests/Raffinert.Expressions.UnitTests/WholeQueryExpansionTests.cs`
- `tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/AsyncConditionTests.cs`, `CachePolicyTests.cs`, `CaptureRegressionTests.cs`, `InterceptorTests.cs`, `ServiceCompositionTests.cs`, `SqliteFixture.cs`
- `tests/Raffinert.Expressions.EntityFrameworkCore.PackageSmoke/PackageSmoke.csproj`, `Program.cs`, `NuGet.Config`

## Remaining limitations and recommendation

Remote remediation source validation: `70d612b37b23558701dee3330b2e6c832455bad1`,
[run 38045212951](https://github.com/Raffinert/Expressions/actions/runs/38045212951):
all ten jobs succeeded, including package consumers on Windows and Linux. The subsequent
documentation-only commit and its CI status are identified in the completion report.

Constant-snapshot interception can fragment EF and database plan caches; use outer scalar
parameters or direct operators for hot paths. Hidden captured collections and embedded
external implementations require direct operators. Compiled wrappers must remain fixed;
wrapper-valued compiled delegate parameters remain unsupported. Only SQLite is verified;
manual internal service providers and service replacements that discard decorators are unsupported.

Recommendation: **merge with documented limitations**, conditional on all final-head CI jobs
passing. No merge, GitHub comments or package publication is performed by this remediation.
