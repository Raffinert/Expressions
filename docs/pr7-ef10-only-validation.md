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
