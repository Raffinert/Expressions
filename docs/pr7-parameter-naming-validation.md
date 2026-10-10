# PR #7 EF-style parameter naming validation

> Historical checkpoint report. Current support is .NET 10 / EF Core 10.x only;
> EF7/8/9 are no longer supported. Counts and CI below belong to earlier commits.
> See [current EF10 validation](pr7-ef10-only-validation.md) for 228 solution tests,
> 42 LocalDB tests and the final migration evidence.


Initial HEAD: `cf75e37bf9a239e987198ba9398ca0a8a0a1fc85` on
`feature/efcore-integration`. Existing untracked plans and `pr_summary.md` are preserved.
The plan's scope is parameter naming only; no private EF APIs or public API changes.

## Baseline and RED → GREEN

Before production edits, restore, full solution tests and formatting verification
passed: 196 tests (62 core, 5 QuerySyntax, 14 existing SQLite, 115 adapter), zero
failed/skipped. SDK 10.0.401; Windows local environment.

RED: the focused ParameterNamingTests suite failed compilation with CS0246 because
RaffinertParameterNameGenerator did not exist. After adding the internal generator
and test-assembly access, all nine focused cases passed. Four additional SQLite
naming regressions cover sensitive strings/diagnostics, nested getter counts,
repeated occurrences and named nullable captures.

The first integration run exposed test assumptions about EF 10: expression ToString
does not render its public QueryParameterExpression.Name, and ordinary captured
parameter names differ from EF 7–9. Assertions now inspect the existing public
parameter-name adapter and the actual recorded outer name. Collision uniqueness
and exact prepared-expression identity remain asserted. All 50 focused naming,
runtime-lifting, cache-policy and execution-audit cases then passed.

## Contract and implementation review

One generator per RuntimeCaptureVisitor reads only original MemberExpression
metadata. Closure implementation links/conversions are ignored; static names need
no declaring type. ASCII normalization preserves case, collapses unsupported runs,
trims boundary underscores and falls back to `p`. Bases are conservatively truncated
to reserve 11 characters for underscore/Int32 suffix; full names never exceed 96.
Per-base suffixes and used-name sets compare OrdinalIgnoreCase. Truncated or
normalized collisions allocate distinct suffixes in traversal order.

Examples (logical names, before provider formatting):

| Capture / situation | Logical name |
| --- | --- |
| threshold | `__raffinert_threshold_0` |
| customerEmail | `__raffinert_customerEmail_0` |
| settings.MinPrice | `__raffinert_settings_MinPrice_0` |
| repeated threshold | `__raffinert_threshold_1` |
| threshold with existing `_0` | `__raffinert_threshold_1` |
| static StaticPrice | `__raffinert_StaticPrice_0` |
| unavailable metadata | `__raffinert_p_0` |

Source identifiers only enter names; capture values never do. No getters,
constructors, ToString, object identities or culture-dependent counters are used.
There is no persistent or static naming registry. This mimics an understandable
EF style, without copying or invoking EF's private naming implementation.
Different source capture names may create different cache keys. Same capture
metadata and changed values retain names and cache reuse.

EfRuntimeParameters.Prefix forwards to the single generator constant. The existing
SafeQueryStringFactory uses that prefix before provider rendering, so descriptive
and fallback lifted names are protected. Ordinary/direct-only diagnostics still
work. QueryExecutionState and EF parameter construction/registration are unchanged.

## Executed SQL and binding evidence

The existing cache-policy recorder executes this SQLite command shape:

```sql
SELECT COUNT(*)
FROM "Orders" AS "o"
WHERE "o"."TotalCents" > @__raffinert_threshold_0
```

The recorder separately asserts logical name `__raffinert_threshold_0` and current
DbParameter.Value for 25 thresholds (100–124) plus repeat 100. One compilation,
one SQL shape and identical names are observed. No captured value enters SQL.
The nested `settings.MinPrice` A → B → A test observes exactly three getter reads
and one compilation; repeated capture occurrences bind two distinct parameters.
Synthetic sensitive strings are checked only through assertions: absent from SQL,
default diagnostic messages and sanitized ToQueryString exceptions; present in
DbParameter.Value. No synthetic secrets are printed as evidence.

The mixed outer/lifted test records unique logical names and correct independent
values. EF 10's deliberately colliding outer name makes the lifted suffix `_1`;
EF 7–9 use their differently formatted outer name and retain `_0`.

## Local validation

| EF / SQLite | Runtime | Adapter tests | Packed consumer |
| --- | --- | --- | --- |
| 7.0.20 | 6.0.36 | 128 passed | Passed |
| 8.0.31 | 8.0.31 | 128 passed | Passed |
| 9.0.20 | 8.0.31 | 128 passed | Passed |
| 10.0.11 | 10.0.12 | 128 passed | Passed |

Each version runs the same test sources against the single EF 7-built adapter.
.NET 6 uses the existing temporary runtime installation; other runtimes are installed.
Full Release solution: 209 tests, zero failed/skipped (62 + 5 + 14 + 128).
Restore and build passed with zero warnings/errors; format verification and
git diff --check passed. Core and adapter 1.2.0 nupkg/snupkg built locally.
All four consumers restored into new temporary NuGet caches using source mapping
to the local feed, with no ProjectReferences, and checked stable readable names,
current bindings, cache reuse, SQL privacy and safe ToQueryString rejection.
Package version remains 1.2.0; no packages are published.

## Files changed and limits

Production: RaffinertParameterNameGenerator.cs, Properties/AssemblyInfo.cs,
EfQueryExpansion.cs, EfRuntimeParameters.cs. Tests: ParameterNamingTests.cs,
ParameterNamingIntegrationTests.cs, RuntimeParameterLiftingTests.cs, SqliteFixture.cs,
PackageSmoke/Program.cs. Documentation: README.md, CHANGELOG.md,
efcore-integration.md, pr7-parameter-lifting-validation.md and this report.

Only SQLite is verified; providers may format names differently. Explicit compiled
runtime captures, hidden collections, unsupported external embedded wrappers,
side-effect/reentrant getters, AOT and same-context concurrency retain their existing
limitations. Sensitive/custom parameter logging can expose bound values.
Historical runtime-lifting reports retain their original counts.

Remote final-head CI evidence is recorded in the PR description after completion;
see [current PR checks](https://github.com/Raffinert/Expressions/pull/7/checks).
No merge or GitHub comments are performed.
