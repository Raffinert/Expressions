# PR #10 — Delegate Getter Review and Release-Hardening Experiment

Plan: `docs/Raffinert_PR10_Delegate_Getter_Release_Hardening_Agent_Plan.md`

## 1. Working-copy protection and fresh baseline (2026-10-10)

### Repository state at start

```text
git status --short
?? docs/Raffinert_EF10_Computed_Directive_Operands_Implementation_Plan.md
?? docs/Raffinert_EF10_Computed_and_Collection_Directives_Implementation_Plan.md
?? docs/Raffinert_EF10_IQueryCompiler_Native_Extraction_Migration_Plan.md
?? docs/Raffinert_EF10_Native_Funcletizer_PreExtraction_PoC_Plan.md
?? docs/Raffinert_EFCore10_Release_Hardening_Implementation_Plan.md
?? docs/Raffinert_EFCore_Integration_Implementation_Plan.md
?? docs/Raffinert_Expressions_PR7_Review_and_Fix_Plan.md
?? docs/Raffinert_PR10_Delegate_Getter_Release_Hardening_Agent_Plan.md
?? docs/Raffinert_PR7_EF10_Only_Simplification_Agent_Plan.md
?? docs/Raffinert_PR7_EF_Style_Parameter_Naming_Plan.md
?? docs/Raffinert_PR7_Runtime_Parameter_Lifting_Implementation_Plan.md
?? docs/Raffinert_PR7_SQL_Server_LocalDB_Integration_Plan.md
?? docs/Raffinert_PR7_Updated_Action_Plan_With_QueryExecutionState.md
?? pr_summary.md

git branch --show-current
experiment/ef10-query-compiler-native-extraction

git rev-parse HEAD
2ea102d0274f009d10b79e5d40116a3c701e356a

git log -1 --oneline
2ea102d (HEAD -> experiment/ef10-query-compiler-native-extraction, origin/experiment/ef10-query-compiler-native-extraction) test: complete cross-provider pooled native compiler validation

git diff --check
(no output — clean)
```

**HEAD is exactly the review checkpoint `2ea102d`**, so no reconciliation of newer commits was needed. All untracked files are plan documents and `pr_summary.md`; they are protected (no `git clean -fdx`).

### dotnet --info (summary)

- .NET SDK 10.0.401, MSBuild 18.9.11
- Runtime: Windows 10.0.26200, win-x64
- Runtimes installed include Microsoft.NETCore.App 10.0.12

### Topic branch

Created `review/pr10-delegate-getter-hardening` from the current PR head `2ea102d`. The pre-existing worktree at `C:/Play/Raffinert.Expressions.EF10-PoC` (branch `experiment/ef10-native-parameter-extraction`) was left untouched.

### Baseline test results (actual, measured 2026-10-10)

```text
dotnet restore Raffinert.Expressions.slnx            -> Restore complete
dotnet build Raffinert.Expressions.slnx -c Release   -> Build succeeded (all 8 projects)
dotnet test Raffinert.Expressions.slnx -c Release    -> total: 267, failed: 0, succeeded: 267, skipped: 0
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests/...csproj -c Release
                                                     -> total: 80, failed: 0, succeeded: 80, skipped: 0 (real Windows LocalDB)
```

Baseline is green and matches the recorded review-checkpoint reference of **267 solution / 80 real Windows LocalDB**. No baseline failure to diagnose.

## 2. RED evidence and design boundary

Core RED: both new tests failed. Merely projecting `holder.Callback` read its getter once at ExpressionExpander.cs:103. A throwing getter produced InvalidOperationException with ApplicationException inner, through SafeValueEvaluator.cs:64. Synthetic messages only; logs: artifacts/pr10-core-red.log.

SQLite native-valid carried projection `Orders.OrderBy(x => x.Id).Select(x => holder.Callback)` returned current delegates and identical SQL/bindings, but native read once and registered read twice. The suggested direct invocation `holder.Callback(x.Id)` was rejected by native EF with ArgumentException (nullable relational projection argument); it is not a parity contract. Log: artifacts/pr10-sqlite-red.log.

Design before production edits: distinguish a delegate value being carried/projected from a callback operand. A delegate property is evaluated only as the target of InvocationExpression or a delegate-typed method argument; direct marker calls retain their existing target resolution. Captured delegate fields and constants retain expansion. This preserves Enumerable method-group/captured-delegate operators without eagerly classifying an opaque projected property. A property used as an actual callback still requires reading its value to identify a Raffinert target; this inherent ambiguity is documented, not claimed to be universally solved. No structural pre-scan, evaluator, query state or EF adapter replacement is needed.

Before changing VisitUnary, test ordinary method-group receiver getters: only a definite Raffinert marker method should trigger target evaluation. A method name of CreateDelegate alone does not establish that its represented method is a marker.

## 3. Confirmed native impact, narrow correction and compatibility

**Conclusion: CONFIRMED AND FIXED**, for carried delegate properties and ordinary method-group receivers.

The final four-case reproducer ran against the exact baseline core source before restoring the candidate. Both SQLite and real LocalDB reported **0 passed / 4 failed**. Two success cases projected `holder.Callback` and `holder.Target.Invoke`: native read once; registered read twice. Throwing Callback: native retried twice; expansion read once and sanitized the exception before EF. Throwing Target method group: both read once, but registered lost the ApplicationException inner retained by native. Logs: artifacts/pr10-final-{sqlite,localdb}-red.log. The unsupported suggested direct delegate invocation was not converted into an invented native contract.

Production changes are confined to ExpressionExpander:

- Before: VisitMember resolved every delegate-valued member before knowing its target. After: retained field/constant resolution; property resolution occurs at InvocationExpression targets and delegate-typed method arguments only. Merely projected opaque properties remain unchanged.
- VisitUnary now checks the represented MethodInfo is an exact Raffinert invocation marker before resolving its method-group receiver. Ordinary methods coincidentally named Invoke do not trigger receiver evaluation.
- No adapter changes, new evaluator, arbitrary subtree compilation, mutable cache, per-query state or warning suppression.

Core positive controls preserve the original NestedMethodGroupsAndCapturedDelegatesExpand test, real property callbacks both as invocation targets and Enumerable arguments (two callback occurrences, two reads), concrete/interface marker method groups (one target read), nested wrappers, InvokeOrDefault and unrelated Invoke.

Seven shared provider cases were added: four native/registered carried-delegate controls (property/method group, successful/throwing) and three marker callbacks (method group/captured field/opaque property). All pass on SQLite and private LocalDB. Successful carried getters are read **1 vs 1** on first execution and both A -> B -> A cache hits; failures retain native counts and inner types, send no SQL and recover. Both contexts compile the tested asynchronous carried shape once and execute three commands each. ToQueryString getter counts and rendered SQL match native. A fully translatable ordinary LINQ control matches rows, SQL and bindings. Callback queries use server EXISTS and current physical bindings, observe wrapper/delegate reassignment, and read the opaque marker callback once per execution.

### Scope and unavoidable ambiguity

A carried opaque delegate property is not classified as a marker during expansion. Its provider/runtime owns its evaluation, even if its returned delegate happens to target Raffinert. Real callbacks still require classification: an opaque delegate property invoked or passed as a delegate-typed argument must be read to discover a marker target. No claim is made that every ordinary opaque callback has zero early reads. This context distinction preserves the existing tested callback API without promising impossible runtime classification. Captured fields/constants retain their established expansion semantics. No deliberately unsupported opaque-property API was invented to disable callback support.

## 4. Release validation

Baseline: 267 solution / 80 LocalDB, zero failures/skips. Final counts and exact-head remote evidence are recorded below after validation; final SHA/run/job conclusions belong in PR #10 to avoid a self-referential report commit loop.

- Native EF owns everything after expansion, including evaluation, extraction, names, bindings, caching and translation. The approved native ToQueryString contract remains unchanged: it may render parameter values with sensitive-data logging disabled.
- Existing compiled sync/async closed-wrapper and scalar argument scope, precompile no-marker forwarding/marker rejection, DI disposal/order, six-lease pooling/factory failure/cancellation recovery, 25 values plus repeat and provider directives/functions pass in the complete suites.
- Core remains netstandard2.0/EF-independent; QuerySyntax netstandard2.1/Core-only; adapter net10.0, Core plus EF Core/Relational [10.0.11,11.0.0). No dependency/global warning policy changed.
- Only the existing local EF1001 boundary/test scope and EF9100 forwarding scope remain. No new diagnostics suppressed; TreatWarningsAsErrors stays enabled.
- NativeAOT, real precompiled-code generation, custom mapped HasDbFunction, other providers/EF versions remain uncertified.

Historical review-checkpoint CI [38074436607](https://github.com/Raffinert/Expressions/actions/runs/38074436607) was independently queried: exact SHA 2ea102d0274f009d10b79e5d40116a3c701e356a, all five individual jobs completed success. This does not validate new hardening commits.

### Actual final local results

| Check | Result |
| --- | --- |
| Solution restore / Release build | PASS; zero warnings/errors |
| Full solution tests | 280 passed / 0 failed / 0 skipped: 68 core + 5 QuerySyntax + 14 original integration + 193 adapter |
| Real Windows LocalDB | 87 passed / 0 failed / 0 skipped: 23 existing + 21 directive + 43 native acceptance |
| Focused core | Initial RED 2 failed, separate ordinary method-group RED 1 failed; all final core tests pass |
| Shared provider RED / GREEN | Baseline 4 failed per provider; final full suites include all seven new shared cases passing |
| Solution / LocalDB format | Both exit 0; generic workspace-loading warning retained. Historical artifacts/native-baseline-format.log completes with 0 of 93 files formatted; the baseline experiment already records the same default-verbosity warning |
| Diff check | PASS |
| Packages | All three nupkg/snupkg built; actual nuspec/library inspection confirms original TFMs and dependencies |
| Windows isolated package consumer | PASS; new GUID cache, local-feed metadata, packed Core DLL hash matches built DLL, EF 10.0.11.0/runtime 10.0.12 |
| Linux isolated consumer / final remote jobs | PENDING until exact final-head CI, maintained in PR validation |

The packaged consumer now checks carried delegate/native getter counts, ordinary method-group receivers and real marker method-group/captured-field server expansion with A -> B -> A. It has no ProjectReference. Ignored logs are under artifacts/pr10-*; only focused source/tests/documentation are committed, never user plan files. The current topic branch is review/pr10-delegate-getter-hardening; its commit is pushed as a normal fast-forward to the existing PR #10 head, without merge/amend/force push or a second PR.

### Final CI and decision

Local gates R0/R1/D1 and provider/architecture/release regression checks pass. Final SHA and all five job conclusions are recorded in [PR #10](https://github.com/Raffinert/Expressions/pull/10) after CI. This checked-in report does not claim success for a run that had not completed at commit time. Candidate Ready for Review requires that final run, not the historical checkpoint. Remaining limitations are listed above; no publish/tag/merge is performed.
