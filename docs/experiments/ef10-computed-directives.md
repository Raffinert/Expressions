# Computed EF directive operands

- Base SHA: `c247403120a12c553f0028d56f4f8bb2f31382db`.
- Branch: `feature/ef10-computed-directive-operands`. Final scalar SHA: `63ad1527216570e00be0c8c6ee3230e8b62b337d`, [draft PR #8](https://github.com/Raffinert/Expressions/pull/8).
- Starting status: tracked clean; 11 untracked user planning/summary files preserved.
- Status: implemented; local acceptance gates passed.
- Strategy: original small structural validator followed by guarded expression interpretation (plan option B). No EF source copied, no third-party notice required.
- Files: adapter helper and directive handler; shared directive acceptance tests and adapter validator tests; isolated package consumer; README, changelog, integration guide and this report.

## Baseline and source decision

`dotnet restore Raffinert.Expressions.slnx` passed. Baseline Release solution tests:
230 passed (62 core, 5 QuerySyntax, 14 original integration, 149 adapter).
Separate real Windows LocalDB tests: 44 passed. Zero failures/skips.
The prior compiler-decoration experiment remains in its separate experiment worktree/branch.

Reviewed exact EF Core **v10.0.11**, including
[funcletizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs)
`VisitMethodCall`, `ProcessEvaluatableRoot`, `Evaluate`/`EvaluateCore` and
`IsGenerallyEvaluatable`; [compiler ordering](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs);
[directive normalization](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs);
and [MIT license](https://github.com/dotnet/efcore/blob/v10.0.11/LICENSE.txt).
EF extraction consults its model/provider evaluation filter and has broad fallback
evaluation. Its directive operands are extracted into native parameter nodes before
cache lookup; normalization subsequently selects the forced translation mode.
That filter and compiler are not called by this helper. The restricted numeric grammar
is independent and uses ordinary expression APIs plus the existing public EF10 bindings.
No compiler decoration, private EF reflection, second funcletization, EF1001 bypass,
warning relaxation or global core-evaluator change was needed.

| Aspect | Native EF | Restricted adapter |
| --- | --- | --- |
| Timing | Extraction before cache lookup | Existing late-wrapper preparation before cache lookup |
| Evaluation | Broad filtered client-evaluatable subtrees | Explicit bounded numeric grammar |
| Result | Native query parameter node | One native node/binding for the final result |
| Mode | EF normalization | Original directive retained for EF normalization |
| Naming | EF extraction naming | Existing metadata-only generator, `computed` path |
| Errors | EF diagnostic policy | Sanitized error without retained inner exception |

## Exact grammar

Existing direct scalar literals/defaults/captures/native parameter operands remain unchanged.
Computed operands permit **short, int, long and nullable forms**, numeric literals/defaults,
field/non-indexed readable property chains rooted in closure constants or static fields,
and built-in Add/AddChecked, Subtract/SubtractChecked, Multiply/MultiplyChecked,
Convert/ConvertChecked with no method and numeric operand/result types.
Arithmetic conversion delegates are disallowed. Receiver conversions in computed member
chains are excluded. Validation has a depth-64/256-visited-node budget and never reads values.

Unsupported: other numeric types (including decimal arithmetic), division/modulo,
unary negation, arbitrary calls/static properties, constructors, indexers/array access,
invocation, mutation, conditionals, nested directives, custom operators/conversions,
TypeAs, row parameters, unknown extension nodes and non-numeric computed results.
Validation must succeed for the entire operand before interpretation; rejected syntax,
including an unselected conditional branch, cannot cause a getter read.
Existing direct decimal/other scalar captures retain their prior contract.

This is not native-EF evaluation parity or a sandbox. Approved captured properties
may execute user code. Keep getters deterministic, non-reentrant and free of side effects.
The result is evaluated once per occurrence per execution, with its original exact type.
Repeated occurrences receive separate results/parameters; children are never lifted first.
Compilation shares preparation without a second getter evaluation. Missing ordinary
preparation context fails before computed evaluation, preserving compiled/standalone limits.

## RED before GREEN

Before production changes, the focused shared suite ran against each real provider:

| Provider | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| SQLite | 21 | 26 | 0 |
| LocalDB | 21 | 26 | 0 |

Acceptance test commit: `c3a9ea0`. Native A/B/A controls ran before embedded arithmetic,
conversion, nullable, parameter/constant and unchecked-overflow assertions; their controls
passed. Checked native overflows raised the expected native error. Embedded positives
then failed with the baseline unsupported-operand diagnostic. Seven negative/recovery
cases first passed zero-getter/zero-SQL rejection checks and then failed at the unsupported
valid computed recovery query. Privacy/mixed composition cases likewise failed at the
first embedded computed query. The old computed-rejection test now uses an arbitrary
method rather than newly supported addition; it remains green without reading its getter.

Evidence retained locally under ignored `artifacts/computed-red-{sqlite,localdb}.log`
and test-project `TestResults/computed-red-*.trx`. Validator tests first failed compilation
because the helper did not exist; after adding it, all four passed before integration.
The first integrated SQLite focused run passed 51 cases; additional collision/type/recovery
checks extended the shared suite to 52 cases plus four adapter validator checks.

## Final local evidence

- Solution: **265 passed**, 0 failed/skipped: 62 core + 5 QuerySyntax + 14 original integration + **184 adapter**.
- Separate real LocalDB: **75 passed**, 0 failed/skipped: 23 original/safety + **52 shared directive cases**.
- Release solution build: **0 warnings, 0 errors** with TreatWarningsAsErrors retained.
- Solution and separate LocalDB formatting verification passed. `dotnet diff` is not used; `git diff --check` passed.
- SDK 10.0.401/runtime 10.0.12, EF/provider 10.0.11 on this Windows host.

Key behavioral evidence, identically asserted through each provider:

- `ComputedParameterMatchesNativeAcrossChanges`: synthetic threshold A/B/A controls match;
  final results bind 1100 / 10100 / 1100, one embedded compilation, one SQL shape,
  no numeric captured literals in parameter SQL, stable `__raffinert_computed_0`.
- `ComputedConstantMatchesNativeAcrossChanges`: current literals 1000 / 10000 / 1000,
  current native-matching ordered results and identical native SQL, no DbParameters.
  Recorded counts: one native compilation; two embedded compilations including the
  separate synchronous ToQueryString check. No one-compilation assertion for constants.
- `ComputedNumericOperationsMatchNative`: all six arithmetic forms, ordinary long
  conversion, checked short conversion, short final result, checked long arithmetic
  and static-field capture. Checked add/subtract/multiply/conversion overflow tests
  fail before SQL, sanitize without inner exceptions and recover after changing values.
  Unchecked addition and narrowing conversion match native values.
- Nullable arithmetic and nullable long conversion match native value/null/value in
  parameter and constant modes; provider null optimization is preserved.
- Nested conditions, mixed computed parameter/simple constant, projection and outer-wrapper
  reassignment match native controls. Repeated computed occurrences read two getters
  per execution across three cache-hit executions, with distinct deterministic names.
- Synthetic private strings remain separately bound and absent from executed SQL/errors.
  Parameter ToQueryString fails safely; constant-only rendering remains available.
  Getter failures retain no inner exception and a subsequent valid query succeeds.
- Invalid method/row/constructor/custom conversion/operator/conditional/static-property/
  nested-directive operands reject before getters/SQL; valid queries recover.
  Unit validation checks also reject arrays, invocation, mutation, TypeAs, decimal,
  division/modulo/negation and oversized trees without reading getters.
- Outer EF name collision: the native captured identifier must itself be
  `__raffinert_computed_0` (EF10 did not add that prefix to `raffinert_computed`).
  Corrected the test's initial native naming assumption; the generator then allocates
  `__raffinert_computed_1` and current bindings reuse one compilation on both providers.
- Computed preparation recovers after failed server translation and cancellation.
  Existing full suites preserve 25-value cache reuse, basic names, context ownership,
  pools/factories, service/interceptor composition and compiled-query limitations.

## Packages and CI

All three 1.2.0 nupkg/snupkg builds succeeded into `artifacts/local-feed`.
The consumer has no ProjectReferences and maps Raffinert packages exclusively to that feed.
Restored into a new GUID-named cache, executed smoke successfully on EF10.0.11/runtime10.0.12,
verified restored adapter metadata names local-feed and its SHA256 matches newly packed bytes.
Smoke includes computed parameter/constant A/B/A, fresh results/bindings/literals,
parameter compilation/shape stability and diagnostics, alongside previous package regressions.
Frameworks/dependency ranges/version and the separate LocalDB workflow/fixture guard remain intact.
[Exact-head scalar CI](https://github.com/Raffinert/Expressions/actions/runs/38070102060)
passed all five jobs: Windows/Linux verification, Windows/Linux isolated package smoke
and Windows LocalDB. Actual logs show 265 tests per solution run (184 adapter),
75 LocalDB tests and both consumer successes. This is the scalar checkpoint;
the separate collection report records its follow-up implementation and checks.

## Scope decisions and recommendation

The initial computed type set is intentionally limited to short/int/long and nullable forms,
which have native controls on both providers. Other numeric types, decimal arithmetic,
division/modulo/negation and receiver conversions are deferred rather than assumed supported.
The LocalDB fixture and cleanup policy, core evaluator, execution-state/decorators and
diagnostic guard were not changed. No release/publish/merge/tag/force push occurred.
Recommendation: accept the narrow scalar change; exact-head remote checks passed.
