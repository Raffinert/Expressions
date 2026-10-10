# PR #7 runtime parameter lifting validation

## Phase A — baseline

HEAD before work: `e12cf480fb008158e280f339aecc276ce5887089`.
Files changed: this report only. Existing untracked plans and pr_summary.md preserved.
Hypothesis: the reviewed implementation remains reproducible locally.
RED test: N/A (baseline).
Implementation: none. Retain feature/efcore-integration following the user's prior
branch-switching concern; no new branch or unrelated commits.
GREEN test: `dotnet restore Raffinert.Expressions.slnx`, Release build, and
`dotnet test Raffinert.Expressions.slnx -c Release --no-build` passed: 173 tests,
zero failed/skipped (62 core, 5 QuerySyntax, 14 existing SQLite, 92 adapter).
Compatibility checked: Windows, EF 7.0.20 / .NET 6.0.36, EF 8.0.31 and 9.0.20 /
.NET 8.0.31, EF 10.0.11 / .NET 10.0.12; 92 adapter tests per version passed.
SDK 10.0.401. Baseline formatting verification failed with WHITESPACE at
EfQueryExpansion.cs lines 39–40; these existing errors will be fixed with that file.
SQL evidence: existing tests establish literals for embedded captures and parameters
for direct operators. Known risk: two independent expansion passes.
Remote baseline: live PR HEAD matches; GitHub API reports completed/successful
[CI run 38056931052](https://github.com/Raffinert/Expressions/actions/runs/38056931052).
Commit: baseline report committed before implementation (see git log).

## Phase B — public runtime contracts

HEAD before work: same baseline. Files changed: this report.
Hypothesis: public native EF parameter nodes and QueryContext write APIs support
late binding before cache lookup. RED/GREEN test: N/A (source research).
Implementation: none. Compatibility checked: exact upstream source tags below.

| EF version | Native parameter node | Public QueryContext write API | PoC |
| --- | --- | --- | --- |
| 7.0.20 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Pending |
| 8.0.31 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Pending |
| 9.0.20 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Pending |
| 10.0.11 | public QueryParameterExpression(string, Type), Extension node | Parameters dictionary | Pending |

Inspected QueryCompiler, QueryContext, and RelationalSqlTranslatingExpressionVisitor
at each tag under https://github.com/dotnet/efcore/tree/v7.0.20 (also v8.0.31,
v9.0.20, v10.0.11). EF 10's public QueryParameterExpression is verified from
[source](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/QueryParameterExpression.cs).
All ordinary paths create context → extract → key/cache → compile on miss → execute.
Explicit compiled delegates skip ordinary key preparation.
SQL evidence: not yet established for late parameters. Stop condition: failure to
translate or bind native parameters on miss or hit. Commit: included with baseline report.

## Phase C — acceptance RED

HEAD before work: c3b690c. Files changed: RuntimeParameterLiftingTests.cs.
Hypothesis: embedded captures must bind as native execution parameters and reuse shapes.
RED test: `dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests
-c Release --filter FullyQualifiedName~RuntimeParameterLiftingTests`.
Secure string assertion failed (sentinel found in SQL); cache assertion failed because
DbCommand.Parameters was empty. Prototype initially had two exact-type test setup
failures; corrected to assignable expression-node assertions.
Implementation: none. GREEN test: pending production implementation.
Compatibility checked: EF 10.0.11 / Windows. SQL evidence: embedded WHERE contains
synthetic string literal; captured integer has zero DbParameters. Known risk: current
mode must be replaced, not kept as fallback. Commit: acceptance tests included with PoC.

## Phase D — isolated native-parameter PoC

HEAD before work: c3b690c. Files changed: same test file and this report.
Hypothesis: late QueryParameterExpression + public QueryContext.Parameters bindings
work on real relational compilation and cache hits.
RED test: test setup corrected as above; no native-parameter translation failure.
Implementation: test-only public EF service decorators, core expansion through the
existing Condition.GetExpandedExpression, one deliberately narrow Where reconstruction.
This is not wired into the production adapter and is not an Invoke expansion engine.
GREEN test: `dotnet test ... -c Release --filter FullyQualifiedName~NativeLateParameterPrototype`:
2 passed / 0 failed / 0 skipped on EF 10.0.11 / Windows.
SQL evidence: WHERE column = @__raffinert_prototype_0; strings and nullable integers
are bound; changed values return changed rows. One compilation, three preparations,
one interception; null selects the row with null CustomerId correctly.
Known risks: general capture discovery, lifecycle, interceptor composition and diagnostic
SQL rendering still need production implementation/tests. Commit: PoC checkpoint in git log.
