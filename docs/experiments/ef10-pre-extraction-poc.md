# EF10 pre-extraction experiment

## Baseline and scope

Starting HEAD: `c247403120a12c553f0028d56f4f8bb2f31382db`.
Experiment branch: `experiment/ef10-native-parameter-extraction`, isolated worktree
`C:/Play/Raffinert.Expressions.EF10-PoC`. The main working tree/branch and ten
untracked user planning/summary files are preserved. SDK10.0.401/runtime10.0.12,
Windows10.0.26200, SQLite/SQL Server providers10.0.11, LocalDB17.0.4025.3.
Fresh baseline: solution230 (149 adapter SQLite) and LocalDB44 passed, no failures
or skips. Starting [CI](https://github.com/Raffinert/Expressions/actions/runs/38067882635)
matches the starting SHA and all five jobs passed.

## Verified pinned EF10 ordering

Read the exact v10.0.11 sources, not main. Ordinary execution is:

```text
IQueryCompiler.Execute / ExecuteAsync
→ QueryCompiler.ExecuteCore
→ IQueryContextFactory.Create; set cancellation token
→ original ExtractParameters(query, QueryContext.Parameters)
→ native ExpressionTreeFuncletizer, once
→ provider cache-key generator sees extracted tree
→ compiled cache lookup; compilation/interceptors on miss
→ compiled delegate executes with that QueryContext
```

Source: [QueryCompiler lines60–88](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L60-L88).
Compiled sync/async creation extracts with parameterize=false, bypassing ordinary
cache-key preparation ([lines101–125](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L101-L125)).
Experimental PrecompileQuery uses precompiledQuery=true and parameterize=true
([lines152–163](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L152-L163)).
[IQueryCompiler](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/IQueryCompiler.cs)
has five entry points including experimental PrecompileQuery, and explicitly
describes an unstable internal infrastructure contract.
[Services](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Infrastructure/EntityFrameworkServicesBuilder.cs#L104)
register it scoped; providers add their services before default core services.
[Funcletizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs#L934-L976)
validates directive operand evaluatability and processes them natively; existing
QueryParameterExpression nodes are non-evaluatable ([line687](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs#L687)).
Its expression equality dictionary deduplicates parameters; native naming avoids
collisions. The [normalizer](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs#L120-L136)
chooses directive translation mode from native parameter operands. This experiment
must expand the raw tree before the original compiler, not run another funcletizer.

## Acceptance evidence

Further observed results will be recorded here. No production migration is authorized
by theoretical call ordering alone; privacy and lifecycle gates must also pass.

### Initial RED

Native direct EF controls execute first and return IDs2/3/4 at threshold1000.
Both embedded computed directive modes then fail with the existing sanitized
NotSupportedException before any embedded SQL. Focused result: SQLite0passed/2failed,
LocalDB0passed/2failed, no skips; TRX saved per provider. This is the expected current
late-expansion limitation, not an unrelated fixture failure. Tests retain their
native-result expectations for the experiment.

## Feasibility stop: EF1001 under the required build policy

Added a minimal 28-line compiler decorator, forwarding all five interface methods.
Ordinary Execute/ExecuteAsync call the core inliner before the original compiler;
compiled methods forward unchanged (not runtime-certified). PrecompileQuery retains
[Experimental("EF9100")] to propagate EF's experimental contract rather than bypass
its diagnostic. No EF internals, evaluator, reflection or second funcletizer is invoked.

`dotnet build src/Raffinert.Expressions.EntityFrameworkCore/Raffinert.Expressions.EntityFrameworkCore.csproj -c Release --no-restore`
failed with **7 EF1001 errors, 0 warnings**. Diagnostics identify IQueryCompiler as
unstable internal infrastructure at the interface declaration, constructor and all
five original-method calls. TreatWarningsAsErrors remains true. No NoWarn, pragma,
analyzer exclusion, command-line warning override or alternate package was used.

This hits the plan's section4.3 stop condition: implementing the decorator would
require bypassing the internal-API diagnostic. Stopped before registration or any
runtime replacement. The prototype is retained as
[RaffinertQueryCompiler.cs](RaffinertQueryCompiler.cs), outside all production/test
project compile globs. To reproduce the diagnostic, include it in the adapter's
Compile items; remove that item afterward. The working adapter source is unchanged.

The RED tests remain strict and intentionally failing on this experiment branch.
Their native-result expectations were not changed, no skips were added, and no
production rejection was weakened. They are linked into LocalDB exactly as written.
This branch is an experiment, not a release candidate with green acceptance gates.

## Gate status and comparison

| Topic | Current production | Isolated proposal / observed status |
| --- | --- | --- |
| Computed directives | Sanitized rejection in late wrappers; direct native initial-A controls pass | RED2 per provider; prototype cannot pass required build policy |
| Evaluation | Existing scalar RuntimeCaptureVisitor | Intended native funcletizer; not executed |
| Naming/deduplication | Deterministic __raffinert_ names | Intended native EF; not runtime-verified |
| Cache-key input | Execution-bound prepared tree | Proposed native post-extraction tree; source order verified only |
| Query-string privacy | Working prefix guard; baseline tests green | Provenance redesign not attempted; cannot claim safety |
| Compiler contract | Existing public services/native nodes | Internal IQueryCompiler; 7 EF1001 build errors |
| Compiled/precompiled paths | Existing restricted contract passes baseline | All methods present; forwarding sketch not runtime-certified |
| Services/lines removed | None | 0 production lines/services removed; 1 unregistered 28-line sketch |

- Gate1: **BLOCKED**, no decorator execution or computed embedded GREEN proof.
- Gate2: **NOT RUN**, no PoC query-string mechanism or leak-free claim.
- Gate3: **BLOCKED at compilation**; DI ordering, lifetime/disposal, compiled,
  precompiled, pooling, recovery and other-interceptor behavior are not certified.
- Conditional production migration: **NOT PERFORMED**.
- Recommendation: **NO-GO under the current plan/build constraints**, not proof that
  native pre-extraction is impossible at runtime. Advancing requires a separately
  authorized, narrowly scoped internal-API diagnostic policy and then all runtime
  gates, especially provenance-based ToQueryString privacy. An alternative public-API
  computed-operand feature would require its own evaluator/security/cache scope.

## Delivery and verification limits

Baseline production SHA remains c247403120a12c553f0028d56f4f8bb2f31382db in the main
working tree, branch feature/efcore-integration. All existing state, lifting, naming,
cache/service decorators, guard and public APIs remain. Core and QuerySyntax unchanged;
net10 adapter version1.2.0 and bounded EF dependencies retained. No production SQL
Server dependency, custom query provider, generic evaluator or new registration API.

Baseline full suites:230 solution/149adapter and44 LocalDB, 0failures/skips.
Experimental focused suites:0passed/2failed per provider (expected computed-directive
RED), no skips; initial native control at A passed before each embedded failure.
A → B → A, 25-value counts, getter/deduplication, projection/reassignment, privacy,
ordinary/compiled behavior and pooling for the new compiler path are **not tested**.
Representative baseline tests do enforce 25+repeat/one compilation, current
__raffinert_threshold_0 bindings, getter-once-per-occurrence and safe ToQueryString.
Those baseline passes are not evidence for the unexecuted prototype.

No experiment package consumer or exact-experiment-HEAD CI result is claimed: the
explicit stop occurred before these release/runtime gates. The linked baseline CI
is green only for the starting SHA. No PR merge, publication, force push, historical
rewrite or untracked user-file mutation. Final experiment SHA is supplied in delivery
because a report cannot embed its own commit hash.

After retaining the sketch outside compile globs, the adapter builds again with
0warnings/0errors. New shared-test formatting checks pass on both project contexts,
and git diff --check passes. Whole-solution format verification reports whitespace
in the unchanged examples/LinqKitComparison/PureDotNetExamples.cs in this fresh
worktree; no unrelated example edits were made and no full-format GREEN is claimed.
The main worktree stays at the starting SHA with exactly its ten untracked user files.
`git diff c247403 -- src` in the experiment is empty: all production source retained.
Experiment delivery consists of this report, the uncompiled compiler sketch, the
strict RED shared tests and their LocalDB link; no production registration changed.
