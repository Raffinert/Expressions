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
