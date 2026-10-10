# PR #7 EF10-only validation

## Phase 1 — inventory and baseline

Starting HEAD: `3711497019fe6a24522060b23ba14f815c590a68`, matching the plan,
on feature/efcore-integration. Existing untracked planning documents and
pr_summary.md are preserved. Read adapter/state/naming/services, canonical SQLite
tests, LocalDB fixture/tests, compatibility projects, package smoke, CI and docs.
The active cross-version surface is the adapter net6.0/EF7 references, reflected
EfRuntimeParameters and test prototype, EF7–9 test projects/friend assemblies,
four-version workflow and conditional package-smoke properties. Core remains
netstandard2.0; QuerySyntax remains netstandard2.1.

SDK 10.0.401, .NET runtime 10.0.12, Windows 10.0.26200. Restore, solution test and
format verification passed before production edits: 209 tests (62 core + 5
QuerySyntax + 14 existing integration + 128 adapter), zero failed/skipped.
Separate LocalDB suite: 23 passed, zero failed/skipped, real current-user engine
17.0.4025.3. No production files changed in this phase.

Checkpoint commits and subsequent phase evidence are recorded below as work proceeds.

## Phase 2 — explicit directive RED evidence

Phase 1 checkpoint: `0f9f45d` (docs: establish EF10-only migration baseline).
Added one shared behavioral test source, compiled separately with provider-specific
fixture/type aliases in the SQLite and LocalDB projects; no test-assembly dependency.
At this checkpoint both providers run 13 explicit-directive cases: **9 passed,
4 failed, 0 skipped**. Native direct EF controls passed before the failing embedded
assertions. RED logs/TRX remain in temporary/test-results directories.

| Case | Direct EF10 SQLite / LocalDB | Embedded SQLite / LocalDB baseline |
| --- | --- | --- |
| Automatic capture A → B → A | GREEN / GREEN | GREEN / GREEN; existing 25-value tests remain green |
| Captured EF.Constant | GREEN / GREEN | GREEN / GREEN; current literal SQL on A → B → A |
| Captured EF.Parameter | GREEN / GREEN | GREEN / GREEN; current bindings |
| Literal EF.Parameter | GREEN / GREEN | RED / RED: InvalidCastException on ConstantExpression |
| Literal EF.Constant | GREEN / GREEN | RED / RED: InvalidCastException on ConstantExpression |
| Mixed modes / nested wrapper / reassignment | GREEN / GREEN | GREEN / GREEN |
| Nullable EF.Parameter / EF.Constant | GREEN / GREEN | GREEN / GREEN, value → null → value |
| Direct wrapper operators | GREEN / GREEN | GREEN / GREEN, before EF extraction |
| Parameter string privacy / constant-only ToQueryString | GREEN / GREEN | GREEN / GREEN |
| Row-dependent operands, both modes | Native rejects before SQL | RED / RED: InvalidCastException instead of sanitized operand error |

Initial mixed/reassignment test incorrectly replaced an inner wrapper after the
outer wrapper had cached its expanded structure. Corrected the harness to replace
the captured outer wrapper, preserving core's documented stable-expression contract.
It then passed without a production change. No assertions about actual directive
semantics were relaxed. Literal/invalid-operand failures reproduce on real SQL.

## Phase 3 — adapter retarget

Phase 2 checkpoint: `9238fb9` (test: specify EF10 explicit parameterization behavior).
Changed only the adapter project: net10.0, EF Core/Relational 10.0.11; version stays
1.2.0 and all package/symbol/readme/core references remain. Restore and adapter
Release build passed with zero warnings/errors. Old compatibility projects are
not retargeted; removal follows after native APIs/directive checks. Core and
QuerySyntax project files are unchanged. HEAD before this phase: `9238fb9`.

## Phase 4 — direct EF10 public contracts

Phase 3 checkpoint: `9bca3cb`. HEAD before this phase: `9bca3cb`.
EfRuntimeParameters now uses QueryContext.Parameters, native QueryParameterExpression
construction/name access and dictionary Add directly. Removed Major, ValuesProperty,
ParameterType, ParameterConstructor, AddParameterMethod, NameProperty and all version
switching/GetProperty/GetConstructor/Assembly.GetType reflection from this adapter.
ParameterExpression name recognition remains for general expression parameters.
Adapter Release build: zero warnings/errors. All 50 runtime/naming/cache/execution
regressions passed, zero failed/skipped. Source search found no version-probing hits.
QueryExecutionState, core expansion and naming are unchanged.

## Phase 5 — proven directive fixes and declared scope

Phase 4 checkpoint: `df01c8e`. HEAD before this phase: `df01c8e`.
Read EF 10.0.11 [funcletizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs),
[normalizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs),
and [public parameter node](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/QueryParameterExpression.cs).
No internal API is invoked/copied. A narrow VisitMethodCall recognizes exact generic
EF marker identities obtained from expression metadata, preserves marker calls and
lets native normalization select Constant/Parameter mode. Existing lifted captures
need no mode rewrite. Scalar literal/default operands receive a native parameter
and QueryContext binding first, using fallback name __raffinert_p_0.

First focused fix: all original 13 cases passed on each provider. Added six cases:
four nested directive combinations, computed operand rejection without getter reads,
and directive getter count/sanitized failure. Native EF rejects nested directive
operands before SQL; tests now assert that native rejection and the adapter's
sanitized rejection, rather than inventing outer-mode semantics. Nested *wrapper*
composition remains supported and green. The computed operand test reproduced one
unwanted getter read before rejection; preflight validation now rejects unsupported
operand shapes before traversal. Final focused result: **19 passed on SQLite and
19 on real LocalDB**, zero failed/skipped; formatting passes.

The entire Phase 2 matrix is GREEN to its declared scope: supported cases match
native controls; row-dependent/nested directive operands fail before SQL. Captured
member chains, scalar literals/defaults are supported. Computed expressions,
conversion-wrapped operands and arbitrary method/constructor operands inside markers
are conservatively rejected; use direct EF operators for native pre-extraction support.
No generic evaluator, client filtering, literal fallback or alternate cache was added.

Observed A → B → A compilation totals: two combined native/embedded compilations
for automatic, EF.Parameter and EF.Constant on each provider (one per query form).
Parameter/automatic SQL uses __raffinert_threshold_0 and current values; forced
constant SQL has no DbParameters and matches native SQL for every execution.
Constant-only ToQueryString renders intentional constants. Any actual lifted bound
parameter remains protected. Test output contains shapes/metadata, never bound values.

## Phase 6 — obsolete compatibility removal and package lane

Phase 5 checkpoint: `cac201d`. HEAD before this phase: `cac201d`.
Removed the EF7/8/9 project files and compatibility-only props, plus their three
friend-assembly declarations. The test-only parameter prototype now uses direct
EF10 nodes/storage; legitimate FieldInfo capture metadata remains. Existing cache
tests now print names rather than bound values. Eight prototype/cache tests passed.
The canonical solution and LocalDB separation are unchanged.

CI retains Windows/Linux verify and strict Windows LocalDB; the obsolete matrix
is replaced by two EF10 isolated-package smoke jobs. PackageSmoke unconditionally
targets net10.0/SQLite 10.0.11. Source mapping is retained and CI overrides the
private cache with a fresh runner-temp path, used consistently for restore/run.
Local core/adapter packs and a fresh-cache nupkg consumer passed on Windows:
EF10.0.11/runtime10.0.12. Smoke asserts 25-value cache reuse, readable bindings,
automatic A → B → A, captured/literal directives and private EF.Parameter binding.
Active source/test/CI search finds no compatibility/version-probing references.
Linux/Windows CI conclusions will be recorded only after new-head runs complete.

## Phase 7 — complete real LocalDB regression

Phase 6 checkpoint: `4e8c391`. HEAD before this phase: `4e8c391`.
The full separate SQL Server suite passed: **42 passed, 0 failed, 0 skipped**
(23 existing cases + 19 explicit-directive cases), with TRX recorded locally.
Provider 10.0.11, .NET runtime 10.0.12, current-user MSSQLLocalDB SQL Server
2025 CU3 engine 17.0.4025.3. Separate project formatting passed. The safety
fixture and production provider dependencies are unchanged; LocalDB stays outside
.slnx. CI engine/job evidence will be recorded after the actual new-head run.

## Phase 8 — full regression

Phase 7 checkpoint: `47188c7`. HEAD before this phase: `47188c7`.
Release restore/build passed with zero build warnings/errors. Full solution test:
**228 passed, 0 failed, 0 skipped** (62 core, 5 QuerySyntax, 14 existing integration,
147 adapter SQLite). Increase from 209 is the 19 explicit-directive cases.
Solution formatter verification and git diff --check passed. The formatter emitted
its existing workspace-load warning but completed successfully with no edits.
The complete separate LocalDB 42-case pass is recorded immediately above.
Existing regression classes cover runtime bindings/cache identity, getter counts,
naming collisions/limits, pooling/services/interceptors, async terminals,
compiled controls, cancellation/recovery and privacy; no redundant tests added.

## Phase 9 — packages and current documentation

Phase 8 checkpoint: `226c1febaeec55f05bfac7e6408f177625980595`.
Current README/changelog/integration guide/index, package description and registration
XML docs now require .NET10/EF10.x, minimum 10.0.11. EF7/8/9 are unsupported;
historical reports retain their accurate old counts with prominent history notices.
Azure SQL/other server versions and collations are not certified. Standalone,
compiled-query, unsupported-capture, service/interceptor/pooling and logging limits
remain documented. Runtime lifting, transparent registration and privacy guard remain.

All three Release nupkg/snupkg packs passed. Inspected actual ZIP/nuspec contents:

| Package 1.2.0 | Library TFM | Actual nuspec dependency IDs / lower bounds |
| --- | --- | --- |
| Core | netstandard2.0 | None |
| QuerySyntax | netstandard2.1 | Raffinert.Expressions 1.2.0 |
| EF adapter | net10.0 | Raffinert.Expressions 1.2.0; Microsoft.EntityFrameworkCore and Relational 10.0.11 |

All contain README, XML docs/release metadata and have matching snupkg. Adapter
contains no SQL Server or QuerySyntax dependency. Future EF11 is not claimed.
A second GUID-named fresh temporary cache restored and ran the newly packed
nupkg-only consumer successfully: EF10.0.11/runtime10.0.12 on Windows.
Solution format verification and git diff --check passed after documentation/XML edits.
No unrelated packages upgraded or planning documents changed.

### Final directive matrix (both SQLite and real LocalDB)

| Case | Before fix | Final native control / embedded |
| --- | --- | --- |
| Automatic capture, 25 values + repeat, A → B → A | GREEN | GREEN / GREEN; one compilation/shape per automatic query |
| Captured EF.Constant and EF.Parameter | GREEN | GREEN / GREEN; A → B → A current values |
| Literal EF.Constant and EF.Parameter | RED: invalid cast | GREEN / GREEN |
| Mixed modes, nested wrappers, captured outer-wrapper replacement | GREEN after harness correction | GREEN / GREEN |
| Nullable modes, value → null → value | GREEN | GREEN / GREEN |
| Direct wrapper overloads | GREEN | GREEN / GREEN |
| String parameter privacy, constant-only ToQueryString | GREEN | GREEN / GREEN |
| Row-dependent operands (2 modes) | Embedded invalid cast | Native rejection / sanitized embedded rejection before SQL |
| Nested directive operands (4 combinations) | Native already rejects | Native rejection / sanitized embedded rejection before SQL |
| Computed operand with getter | Embedded read getter before rejection | Narrow unsupported scope: rejection without reading getter |
| Supported getter count and failure diagnostic | Added regression | One read per occurrence/execution; sanitized failure |

19 focused tests pass per provider. These assertions compare execution/results,
real DbCommand bindings and logical names; unsupported cases are not advertised
as native parity. Arbitrary computed/conversion/method/constructor directive operands
remain outside the supported late-expansion contract. Native pre-extraction via
direct wrapper operators remains available. No evaluator or client filtering added.

Executed SQL Server shape from TRX (SQLite uses quoted identifiers):

```sql
SELECT [o].[Id]
FROM [Orders] AS [o]
WHERE [o].[TotalCents] > @__raffinert_threshold_0
ORDER BY [o].[Id]
```

Automatic/captured parameter modes bind current synthetic values in DbParameter,
not SQL. Literal EF.Parameter binds __raffinert_p_0; mixed query additionally binds
__raffinert_maximum_0. EF.Constant uses the intentional numeric SQL constant,
no DbParameters, and matches native SQL on every A → B → A execution. Each captured
mode observed two combined native/embedded compilations (one each), on both providers.
Automatic 25-value tests assert one compilation/one SQL shape. Diagnostic protection
blocks actual lifted parameter rendering before values can be formatted; explicit
constants are intentionally outside that guarantee. No bound values are logged.

Final EfRuntimeParameters uses QueryContext.Parameters, dictionary Add,
new QueryParameterExpression(name, type), QueryParameterExpression.Name and generic
ParameterExpression.Name. All version adapter reflection fields listed in Phase 4
are removed. Legitimate expression MemberInfo metadata is retained.
