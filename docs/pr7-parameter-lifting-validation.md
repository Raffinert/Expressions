# PR #7 runtime parameter lifting validation

> Historical checkpoint report. Current support is .NET 10 / EF Core 10.x only;
> EF7/8/9 are no longer supported. Counts and CI below belong to earlier commits.
> See [current EF10 validation](pr7-ef10-only-validation.md) for 228 solution tests,
> 42 LocalDB tests and the final migration evidence.


The phase records below describe the historical runtime-lifting implementation through
`cf75e37`. Current parameter-naming validation (209 solution tests, 128 adapter tests
per EF major) is recorded in [the naming report](pr7-parameter-naming-validation.md).

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
| 7.0.20 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Miss/hit/string/int/null passed |
| 8.0.31 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Miss/hit/string/int/null passed |
| 9.0.20 | ParameterExpression, name starts with `__` | AddParameter; ParameterValues | Miss/hit/string/int/null passed |
| 10.0.11 | public QueryParameterExpression(string, Type), Extension node | Parameters dictionary | Miss/hit/string/int/null passed |

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

## Phase F — EF 7 compatibility checkpoint

HEAD before work: 73efe48. Files changed: this report. Hypothesis: EF 7 native
ParameterExpression + AddParameter supports the same preparation and secure SQL.
RED test: N/A; no version-specific defect reproduced. Implementation: no change.
GREEN test: `dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef7/Ef7.csproj -c Release`:
105 passed / 0 failed / 0 skipped on Windows, EF 7.0.20 / runtime 6.0.36.
SQL evidence: same privacy, 25-value cache, null, pooled, compiled restrictions,
interceptor-order and getter assertions all pass. Known risk: other EF legs pending.
Commit: EF 7 checkpoint in git log.

## Phase F — EF 8 compatibility checkpoint

HEAD before work: 0e02436. Files changed: this report. Hypothesis: EF 8's native
ParameterExpression + AddParameter behaves identically on misses and hits.
RED test: N/A; no version-specific defect. Implementation: no change.
GREEN test: `dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef8/Ef8.csproj -c Release`:
105 passed / 0 failed / 0 skipped on Windows, EF 8.0.31 / runtime 8.0.31.
SQL evidence: all shared runtime/SQL/privacy/cache assertions pass.
Known risk: EF 9 pending. Commit: EF 8 checkpoint in git log.

## Phase F — EF 9 compatibility checkpoint

HEAD before work: 7a63abb. Files changed: this report. Hypothesis: EF 9 still uses
native ParameterExpression + AddParameter, without an EF 10 type dependency.
RED test: N/A; no version-specific defect. Implementation: no change.
GREEN test: `dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef9/Ef9.csproj -c Release`:
105 passed / 0 failed / 0 skipped on Windows, EF 9.0.20 / runtime 8.0.31.
SQL evidence: all shared privacy/cache/null/getter/pooling assertions pass.
Known risk: Linux/final-head CI not yet run. Commit: EF 9 checkpoint in git log.

## Phase G — full regression, privacy and ownership

HEAD before work: 29da7d3. Files changed: RuntimeParameterLiftingTests.cs and this report.
Hypothesis: one preparation provides identical key/compile structure, fresh bindings,
safe diagnostics and no stale values across pooling, contexts, failures or cache hits.
RED test: the destructive-interceptor assertion initially failed on EF 7–9 because
another test had already cached the same shape: compilation/interception was bypassed.
Isolated EF service caches in both interceptor-order scenarios and asserted callback
counts, then reran every version. This was a test-isolation error, not a provider failure.
Implementation: extra positive/negative tests; no further production algorithm changes.
GREEN test: Release solution 195 passed / 0 failed / 0 skipped; 114 adapter tests on
each EF 7.0.20, 8.0.31, 9.0.20 and 10.0.11 leg. Commands are the Phase A/F commands
rerun after the shared test additions. Formatting verification also passed.
Compatibility checked: Windows with the exact runtimes recorded above; Linux pending CI.
SQL evidence: bound synthetic string absent from CommandText and default diagnostic logs;
ToQueryString rejects rendering before exposing binds. User getter exception containing
the synthetic value is sanitized (including inner exception); unsupported Uri capture
fails without value serialization or SQL. DateTime, TimeSpan, bool and double tests add
to the Guid/decimal/enum/date/time/string/int/nullable coverage. Both sync and async
compiled runtime captures fail safely, while supported scalar delegate parameters work.
The test-only observing provider-key decorator and following interceptor assert the
same prepared Expression instance and inspect constants: no captured string/wrapper.
Both interceptor orders preserve Take filters on misses and hits; destructive earlier
rewrites fail safely while ordinary queries remain valid. Names survive an intentional
outer EF parameter-prefix collision. Existing nested/composed/correlated/default/method
group/API/QuerySyntax tests remain green. Pooling/factory leases reuse scoped services
without stale parameters; sequential deferred queries and independent contexts remain
covered. No forced GC, concurrent operations or untested synchronization primitives.
Known risks: side-effecting/reentrant getters remain unsupported; distinct repeated
scalar occurrences are intentionally not deduplicated. No general AOT/provider claim.
Commit: regression checkpoint in git log.

### Phase G follow-up — unsupported captures before getter evaluation

HEAD before work: 2a17619. Files changed: EfQueryExpansion.cs and runtime tests.
Hypothesis: unsupported compiled runtime captures should be rejected without executing
a potentially side-effecting getter. RED: code inspection identified evaluation before
rejection; no new SQL failure claimed. Implementation: check type/mode before evaluation.
GREEN: focused runtime suite 23 passed / 0 failed / 0 skipped on EF 10.0.11 / Windows,
including a throwing captured getter with zero reads in explicit compilation.
SQL evidence: zero commands on unsupported compiled getter. Full matrix rerun follows.
Known risks: arbitrary EF extraction outside adapter preparation retains EF behavior.
Commit: fail-fast hardening checkpoint in git log.

## Phase H — documentation, migration and release validation

HEAD before work: 9d0c75d (runtime implementation), with release documentation edits.
Files changed: README.md, CHANGELOG.md, docs/efcore-integration.md,
docs/efcore-validation.md, docs/pr7-remediation-work-log.md, adapter registration XML
remarks, PackageSmoke/Program.cs and this report. Unrelated planning files preserved.
Hypothesis: the shipped package preserves secure binding/cache semantics and clearly
states migration/diagnostic restrictions on every advertised EF runtime.
RED test: N/A (release checks); earlier literal/getter assertions were explicitly
inverted only after the secure acceptance tests passed. No contradictory green legacy test.
Implementation: replace constant-snapshot release claims; document DbParameter.Value,
sensitive/custom logging, explicit constants, compiled/standalone restrictions,
ToQueryString safe failure, supported pooling and interceptor order. Historical logs
are identified as historical. Isolated package consumer now checks privacy, native
bindings, 25+repeat values, one compilation/shape and sanitized ToQueryString rejection.
GREEN test: restore and Release build passed, zero warnings/errors. Full solution:
196 passed / 0 failed / 0 skipped (62 core + 5 QuerySyntax + 14 existing + 115 adapter).
Final shared suite: 115 passed per EF major, zero failed/skipped. Formatting verification
and git diff --check passed. All three 1.2.0 NuGet + symbol packages built; local feed
artifacts/pr7-packages and release copies artifacts/package contain the current builds.
Compatibility checked: Windows EF 7.0.20 / runtime 6.0.36; EF 8.0.31 and 9.0.20 /
runtime 8.0.31; EF 10.0.11 / runtime 10.0.12, SDK 10.0.401. Packed consumers use
fresh task-specific temporary caches, source-mapped local nupkg files and no ProjectReferences.
SQL evidence: each packed consumer asserts synthetic secret absent from command text,
present in DbParameter.Value; changed captures reuse one compilation/SQL shape.
Nuspec inspection: core has no runtime EF dependency; QuerySyntax depends only on core;
adapter depends on core 1.2.0, EF Core 7.0.20 and EF Relational 7.0.20, not QuerySyntax.
Known risks / stop condition: only SQLite is verified. Hidden collections/external
embedded implementations require direct operators. Unsupported types and runtime
captures in explicitly compiled/standalone wrappers fail before SQL; compiled scalar
delegate parameters remain supported. AOT and arbitrary provider/extensions unverified.
Values are still accessible to sensitive logging/bind profiling/custom telemetry.
Side effects/reentrant getters and same-context concurrency remain unsupported.
Remote verification: release/source checkpoint `8cfaf21e3e40565be583bf5a699ee05847f373a4`
passed [CI run 38060364691](https://github.com/Raffinert/Expressions/actions/runs/38060364691):
completed/successful, all 10 jobs, including EF 7/8/9/10 tests and isolated package
consumers on Windows and Linux. This evidence-only documentation commit does not change
source or tests. Its follow-up final-head checks are visible on
[PR #7 checks](https://github.com/Raffinert/Expressions/pull/7/checks).
No merge or package publication is part of this work. Commit: release checkpoint in git log.

Decision: GO for the tested SQLite/EF runtime contract with the documented restrictions.
The source/release checkpoint is verified across both operating systems, with no failed
or skipped tests. Final evidence-only commit checks are tracked separately above.
