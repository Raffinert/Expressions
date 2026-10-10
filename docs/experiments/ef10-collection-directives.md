# Computed scalars and explicit collection directives

- Base SHA: `63ad1527216570e00be0c8c6ee3230e8b62b337d` (completed scalar workstream).
- Earlier plan checkpoint: `c247403120a12c553f0028d56f4f8bb2f31382db`; subsequent scalar work was retained.
- Branch: `feature/ef10-explicit-collection-directives`. Final SHA and exact-head CI conclusions are recorded in the final PR description after this report is committed.
- Starting status: tracked clean; 12 untracked user planning/summary files preserved.
- Workstream A: implemented, original guarded scalar evaluator; its [report](ef10-computed-directives.md) records prior RED/GREEN and [five successful CI jobs](https://github.com/Raffinert/Expressions/actions/runs/38070102060) at the base SHA.
- Workstream B: implemented, independent small captured-collection validator/binder. No EF source copied.
- Changed files: collection helper and directive handler; shared acceptance tests and collection helper checks; package smoke; README, changelog, integration guide and experiment reports.

## Baseline and exact EF source

The completed base tree passed restore, Release build and solution tests: **265 passed**,
including 184 adapter tests. Separate real LocalDB: **75 passed**. Its exact-head CI logs
confirmed the same counts on Windows/Linux and real Windows LocalDB, plus both package
consumers. There were no failures/skips. The separate compiler-decoration PoC was retained
without importing it into either implementation.

Reviewed tagged EF Core **v10.0.11**
[relational EFExtensions](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/EFExtensions.cs),
[ParameterTranslationMode](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/ParameterTranslationMode.cs),
[funcletizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs),
[normalizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs)
and [SQL Server nullability processor](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Query/Internal/SqlServerSqlNullabilityProcessor.cs).
`MultipleParameters` is a C# extension member whose emitted generic method belongs to
`Microsoft.EntityFrameworkCore.EFExtensions`, not `EF`. Its body must not be called.
The existing funcletizer/normalizer extract one native parameter and select distinct
translation modes. SQL Server owns parameter expansion, padding/bucketing and limit fallbacks.

| Aspect | Native EF | Restricted adapter |
| --- | --- | --- |
| Extraction | Earlier model/provider-filtered evaluation | Existing late-wrapper preparation |
| Scalar evaluation | Broad client-evaluatable expressions | Separate approved numeric grammar |
| Collection evaluation | Broad native extraction | Closed approved captured object only |
| Binding | One logical native parameter | Same, with original static CLR type |
| Mode and physical SQL | Normalizer/provider | Original directive retained; no adapter splitting/JSON |
| Names and errors | EF policy | Existing metadata-only names and sanitized capture failures |

Chosen implementation: original independent binder. No copied EF fragment or MIT notice
addition was necessary. No private API, compiler decorator, second extraction pass,
EF1001 suppression or warnings-as-errors relaxation was introduced.

## Exact accepted scope

Scalar grammar remains short/int/long and nullable built-in addition/subtraction/
multiplication/conversion, including checked forms, as documented in the scalar report.
It still rejects collections and arbitrary methods/constructors.

Collection directives: **EF.Constant, EF.Parameter, EF.MultipleParameters**.
Approved declared collection types: **int[], List<int>, int?[], string[]** only.
Operands: readable closed captured field/non-indexed property chains rooted in closure
constants or static fields, or already-native query parameters. Chains are validated
without reads and bounded to 64 members; no receiver casts or constructor roots.
Runtime values must be null or exactly the declared approved type, excluding list subclasses.
Captured null collections passed independent native controls before acceptance.

Unsupported: literal/default/null expression operands; collection creation; arbitrary
methods, LINQ computations, indexers, nested directives and row-dependent arrays;
interface-typed/lazy enumerables, IQueryable and other element/container types.
Bare hidden collections retain their direct-operator restriction. Direct operators
still expand before native extraction and retain native EF's broader support.

An approved capture is read once per occurrence/execution, including cache hits.
Its whole object is added once to QueryContext under one exact-typed native parameter.
Raffinert never enumerates, serializes, splits, pads or buckets collection elements.
Native parameters keep their identity without rebinding. Missing ordinary preparation
context fails before capture reads. Getter errors are sanitized without an inner exception.
Approved getters can execute user code; keep them deterministic and free of side effects.

## C1: native baseline and embedded RED

New tests were placed in the existing shared `ExplicitEfParameterizationTests.cs`, linked
unchanged into LocalDB. All collection Contains calls use explicit Enumerable.Contains
to avoid .NET 10 span overload selection on both native and embedded sides.

| Provider | Native mode/type controls | Focused shared RED |
| --- | --- | --- |
| SQLite | 12 passed, 0 failed/skipped | 70 passed / 21 failed / 0 skipped |
| Real LocalDB | 12 passed, 0 failed/skipped | 70 passed / 21 failed / 0 skipped |

Collection RED commit: `221818b`. All scalar regressions stayed green. The 12 embedded
mode/type positives, three privacy cases, three getter cases and mixed composition failed
because the baseline rejected collection captures. Two new-array/row negatives found
the unrecognized MultipleParameters call traversed a scalar getter before rejection.
Other unsupported collection cases already failed safely. Native nested-directive and
row-dependent collection failures were independently asserted before SQL.
Logs/TRX are retained locally under ignored artifacts and test-project TestResults paths.
The new helper tests first failed compilation because the helper did not exist; all 11
passed after adding it, before directive-handler integration.

Native SQL Server Parameter/string[] initially failed with a collation conflict between
the JSON element collation and the fixture column's default collation. No fixture change
was made. Explicit binary collation passed native controls: Latin1_General_BIN2 on SQL
Server, BINARY on SQLite. Embedded string tests keep that Collate call in ordinary LINQ
outside the late-expanded Condition<string>, so EF evaluates its provider function before
extraction. Provider-function evaluation inside arbitrary late wrappers is not broadened.
String-array support therefore does not promise compatibility with every model collation.

## C2: final real-provider GREEN

Final Release solution: **318 passed**, 0 failed/skipped:
62 core + 5 QuerySyntax + 14 original integration + **237 adapter**.
Separate real LocalDB: **117 passed**, 0 failed/skipped.
Both compile the same **94 shared directive cases**. Adapter-only helpers add four scalar
and eleven collection validation checks; LocalDB retains eight fixture safety checks.

All 12 collection mode/type combinations passed native-vs-embedded checks across replacement,
same-list mutation, two-value A/B/A, empty/single/multiple/duplicate values, a 12-element
collection, captured null collections and nullable elements. Native physical parameter
values matched embedded values exactly; SQL comparison normalizes parameter identifiers
and metadata-derived collection-table aliases, not values or translation modes.

| Mode / type | Embedded compilations (each provider) | Executed SQL shapes (SQLite / LocalDB) |
| --- | ---: | --- |
| Constant / array, list, nullable, string | 1 each | 6 each / 6 each |
| Parameter / array, list, string | 1 each | 2 each / 2 each |
| Parameter / nullable | 1 each | 3 / 3 |
| MultipleParameters / array, list, string | 1 each | 5 each / 5 each |
| MultipleParameters / nullable | 1 each | 6 / 6 |

These are measured EF compilations and executed SQL shapes, not database plan-cache metrics.
Native null optimization and padding own physical parameter counts; no fixed count across
cardinalities is assumed. Synthetic two-string privacy tests observed **0** physical
parameters for Constant, **1** for Parameter and **2** for MultipleParameters on each
provider. Parameter uses native JSON-like collection SQL; MultipleParameters uses element
parameters without JSON for tested sizes. Constants intentionally appear in SQL.

Getter tests observe three reads for three executions/cache hits, one failing read and
one subsequent recovery read (five total). A repeated mixed occurrence test observes
two reads per execution over three executions, with distinct logical names. Unsupported
method/new-array/append/nested/row/queryable/interface operands issue no SQL and read
zero getters. Unit tests cover read-free approved/rejected captures, supported types,
context rejection and retained native parameter identity.

Mixed collection/scalar/outer native captures have unique bindings and current results.
Actual native-name collision forces ids suffix 1; metadata-only naming remains stable.
Projection and wrapper reassignment match native controls. Both AddDbContextPool and
AddPooledDbContextFactory (pool size 1) reuse the same context over six leases on each
provider, with fresh arrays and recovery after failed translation/cancellation.
Existing context/service/interceptor/scalar/compiled-query and 25-value cache suites pass.

The existing ToQueryString guard recognizes actual physical parameters in both bound
modes after expansion. Private strings remain absent from SQL and diagnostics; constants
deliberately render values. Empty/null optimization may remove all physical parameters.
The guard was not modified. Sensitive/custom logging remains outside this guarantee.

## Build, package and remote evidence

- Restore and Release solution build passed, **0 warnings/errors**.
- Solution and separate LocalDB format verification passed; git diff --check passed.
- All three 1.2.0 nupkg/snupkg builds passed into artifacts/local-feed.
- A GUID-named fresh consumer cache restored exclusively from that feed for Raffinert
  packages, with no ProjectReferences. Metadata confirmed the feed; restored adapter
  SHA256 matched newly packed bytes. Smoke passed on EF10.0.11/runtime10.0.12.
- Smoke covers computed parameter/constant and all three collection modes for captured
  arrays and mutable List<int>, current native-matching physical bindings/results,
  changing sizes/duplicates, one embedded compilation and diagnostics.
- Exact-head collection CI will be recorded in the final PR description/checks after
  committing this report; no prospective remote result is asserted here.

Frameworks remain netstandard2.0 core, netstandard2.1 QuerySyntax, net10.0 adapter.
Version remains 1.2.0; EF Core/Relational dependency ranges remain [10.0.11,11.0.0).
No production SQL Server dependency, fixture cleanup policy/prefix change, global core
evaluator widening, merge, publication, tag or force push occurred.

Recommendation: accept the separate narrow collection change after its exact-head checks
pass. Scalar workstream remains independently validated. Deferred collection forms and
provider collation/function limitations are explicit above and in the integration guide.
