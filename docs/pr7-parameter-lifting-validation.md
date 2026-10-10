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

## Phase E — execution-bound preparation and secure bindings

HEAD before work: 349e2ca. Files changed: EfRuntimeParameters.cs, EfQueryExpansion.cs,
QueryExecutionState.cs, RaffinertOptionsExtension.cs, RaffinertExpressionInterceptor.cs,
adapter csproj, core ExpressionExpander.cs, runtime/capture/cache/audit/interceptor tests.
Hypothesis: the provider key and compilation can share one normalized expression while
each execution binds current scalars without any captured SQL literals.
RED test: Phase C privacy/cache failures; the first full migration run passed 81 adapter
tests and failed 15 obsolete literal/snapshot/getter-count assertions. Correct result
assertions remained green. One new cache-isolation test initially lacked ORDER BY;
fixed its nondeterministic row-order expectation.
Implementation: public version-aware native EF nodes and writes; scoped weak context
plus ConditionalWeakTable keyed by the specific QueryContext. Compilation consumes
the matching prepared expression; a cache hit binds values before lookup. New Create
invalidates old preparation; miss removes it after consumption. The table is ephemeron
owned, not a persistent strong reference to execution or user captures. No-marker
queries do not store a preparation. Names are deterministic traversal ordinals, skipping
existing EF parameter names. Provider key generator/lifetimes/disposal remain in control.
Core now evaluates only delegate-typed members for method-group expansion, avoiding
discarded scalar getter reads. No public core API or QuerySyntax changes.
Captured getters and constructors have sanitized adapter errors without user inner
exceptions. Collections/unsupported types and compiled/standalone runtime captures
fail before SQL, never falling back to literals. Explicit developer constants and
server member chains remain unchanged. A public IRelationalQueryStringFactory decorator
rejects lifted debug rendering before it can format bound values; this requires adding
EF Relational 7.0.20 (core remains EF-independent, no QuerySyntax dependency).
GREEN test: full Release solution 186 passed / 0 failed / 0 skipped; adapter 105.
Compatibility checked: EF 10.0.11 / Windows at this checkpoint.
SQL evidence: string/int/Guid/decimal/date/time/enum captures are runtime parameters;
fixed thresholds yield one compilation/SQL shape for 25+repeat. Null parameters may
be optimized to IS NULL with no DbParameter by SQLite; normalized cache shape is stable.
Controlled getter now reads once per execution on miss and hit, binds 2 → 4 → 2,
and compiles once. Pool/factory poolSize=1 reuse the scoped factory across six leases
without stale values. Added Take filters survive both interceptor orders.
Known risks: destructive pre-Raffinert interceptor rewrites must fail rather than
discard changes; standalone/compiled late captures are intentionally rejected.
Commit: production checkpoint in git log.
