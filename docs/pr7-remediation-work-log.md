# PR #7 remediation work log

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

Further phase results are recorded below as they execute.
