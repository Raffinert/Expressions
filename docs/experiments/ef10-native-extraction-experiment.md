# EF10 native parameter extraction experiment

## Decision and checkpoints

**Decision: MIGRATE**, under the owner's additional native-parameterization and native-diagnostics decision.
The former diagnostic privacy policy is deliberately superseded, not silently relaxed.

- Baseline: `feature/efcore-integration`, `c247403120a12c553f0028d56f4f8bb2f31382db`.
- Experiment: `experiment/ef10-query-compiler-native-extraction`, created in the same workspace with `git switch -c`.
- No-op checkpoint: `60d25bd` (230 solution / 44 LocalDB passed).
- Initial RED controls: `3b4d1e4` (24 cases per provider: 3 passed, 21 failed).
- Candidate acceptance checkpoint: `3e52a4a` (267 solution / 74 LocalDB passed before removing obsolete production infrastructure).
- Migration implementation: `ddeac907bd7ed3ef7ac34ac62c2f87189b928e2a`.
- Final experiment/report HEAD and exact-head CI are recorded in the PR validation section after the final report commit.

The starting checkout was the actual reviewed baseline, not the separate scalar/collection feature branches.
Those branches and the earlier PoC worktree were not reset or deleted. All 13 untracked user planning/summary
files were preserved and excluded from commits. No merge, NuGet publication, tag or force push occurred.

## Environment and baseline

Windows 10.0.26200, win-x64; SDK 10.0.401, runtime 10.0.12. global.json requests
10.0.400 with latestPatch; the installed SDK satisfies that policy. Actual restored EF Core,
Relational, SQLite and SQL Server packages are **10.0.11**. LocalDB is the private current-user
MSSQLLocalDB instance. No remote/shared database was used.

Commands observed before experimentation:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
git log -1 --oneline
dotnet --info
dotnet restore Raffinert.Expressions.slnx
dotnet build Raffinert.Expressions.slnx -c Release --no-restore
dotnet test Raffinert.Expressions.slnx -c Release --no-build --no-restore
dotnet format Raffinert.Expressions.slnx --no-restore --verify-no-changes
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests/Raffinert.Expressions.EntityFrameworkCore.SqlServerTests.csproj -c Release
```

Baseline: 230 solution tests (62 core, 5 QuerySyntax, 14 original integration, 149 adapter),
44 LocalDB; zero failures/skips. Release build: zero warnings/errors. Format exits zero.
The formatter prints its generic workspace-loading warning at default verbosity on both
baseline and candidate; diagnostic baseline logging completed with zero formatted files and
no specific warning/error details. This is not a new compiler diagnostic from the migration.

## Tagged EF10 source and call order (S1)

Sources were read from the exact **v10.0.11** tag, not main. No EF source was copied into
Raffinert. The adapter does not construct QueryCompiler or call its ExtractParameters,
private methods or ExpressionTreeFuncletizer.

| Path | Actual EF call order and source |
| --- | --- |
| Ordinary sync | Raffinert core expansion -> original Execute -> ExecuteCore creates QueryContext (72), sets token (74), ExtractParameters (76), GenerateCacheKey (81), compile on miss (83), invoke delegate (86). [QueryCompiler.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L58-L86) |
| Ordinary async | ExecuteAsync (67-68) uses the same ExecuteCore with async=true; token is forwarded into QueryContext unchanged. Execution/enumeration remains the original provider's result. [QueryCompiler.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L67-L86) |
| Compiled sync/async creation | Original CreateCompiledQuery (95-101) / CreateCompiledAsyncQuery (110-116) create a context and extract with compiledQuery=true, parameterize=false (155-162), then compile. The retained public interceptor expands closed wrappers at compilation; runtime captures introduced inside them are rejected before getter evaluation. [QueryCompiler.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L95-L162) |
| Compiled execution | EnsureExecutor (87-99) resolves IQueryCompiler once and rewrites delegate parameters to native query parameters (103-113). Each ExecuteCore (53-80) creates a fresh QueryContext, sets cancellation and binds delegate arguments before invoking the cached executor. [CompiledQueryBase.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/CompiledQueryBase.cs#L53-L113) |
| Experimental PrecompileQuery | Interface member has Experimental at 58. Native implementation (138-146) extracts with parameterize=true, precompiledQuery=true, then calls CompileQueryExpression. Adapter forwards an unchanged no-marker tree once; any detected Raffinert expansion on this path throws an early supported-scope error. [IQueryCompiler.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/IQueryCompiler.cs#L58-L59), [QueryCompiler.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs#L138-L146) |
| Interception | QueryCompilationStarting is called in CreateQueryExecutorExpression (206), before preprocess/translate but after ordinary native extraction/cache lookup. It is too late to introduce captures for native extraction. The registered compatibility interceptor is a no-op once ordinary wrappers have already expanded. [QueryCompilationContext.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/QueryCompilationContext.cs#L204-L223) |
| ToQueryString | Extension (46-49) executes the query provider for IEnumerable, obtains IQueryingEnumerable, then renders. SingleQueryingEnumerable (125-146) constructs the DbCommand using native execution parameters and passes it to the relational query-string factory. Native formatting includes parameter values; provider-specific formatting is retained. [EntityFrameworkQueryableExtensions.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs#L46-L49), [SingleQueryingEnumerable.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Query/Internal/SingleQueryingEnumerable.cs#L125-L146), [RelationalQueryStringFactory.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Query/Internal/RelationalQueryStringFactory.cs#L25-L42) |
| Service composition | Options extensions ApplyServices in registration order (184-188). EF lists IQueryCompiler as Scoped (108) and installs its default with TryAdd (272). Raffinert requires one scoped descriptor, retains its type/factory under a private DI key, then decorates the unkeyed service. Original service ownership stays in DI; no recursive self-resolution or double disposal. [ServiceProviderCache.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Internal/ServiceProviderCache.cs#L180-L203), [EntityFrameworkServicesBuilder.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Infrastructure/EntityFrameworkServicesBuilder.cs#L108) |

Directive extraction: [ExpressionTreeFuncletizer.cs, 935-974](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs#L935-L974).
Directive normalization expects native query parameter operands:
[QueryableMethodNormalizingExpressionVisitor.cs, 120-143](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryableMethodNormalizingExpressionVisitor.cs#L120-L143).
Collection directive declaration is in [EFExtensions.cs, 33-35](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/EFExtensions.cs#L33-L35);
native modes are [ParameterTranslationMode.cs](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/ParameterTranslationMode.cs).

## Diagnostics and explicit approvals

The first no-op build, with only the authorized EF1001 exception, produced exactly
**one EF9100 error**, at NativeExtractionQueryCompiler.cs(23,9), calling the experimental
PrecompileQuery member. Production was restored before proceeding. The owner then explicitly
approved EF9100 only inside that forwarding implementation, with no global-policy change.
A subsequent no-op build and all baseline tests passed.

Final local exceptions:

- `NativeExtractionQueryCompiler.cs`, EF1001 pragma around the compiler adapter only:
  its IQueryCompiler declaration, service-type references/registrations and the five inner interface calls.
  These all depend on the single version-specific internal contract; no extractor implementation API is used.
- The same file's PrecompileQuery forwarding member only: EF9100. The native interface's Experimental
  annotation requires this even for unchanged forwarding; it is not an ordinary-LINQ diagnostic.
- `NativeCompilerContractTests.cs`, EF1001 around contract/descriptor tests and their fake/custom compilers.
  Its custom observing compiler's PrecompileQuery forwarding member has the same local EF9100 exception.
  Reflection tests invoke the experimental boundary without another diagnostic suppression at the call site.

TreatWarningsAsErrors, analyzers, project/solution NoWarn and global build policy remain unchanged.
No other diagnostic was suppressed. No NativeAOT/precompiled-code support claim is made.

## Native controls, RED findings and functional observations

Initial 24-case tests executed native and direct controls before the old embedded form on each provider.
Both SQLite and LocalDB observed **3 passed / 21 failed**. Of the failures, 19 exposed old behavioral
limitations (computed operands, collections, late provider functions/metadata, mixed/nested composition,
and a native-error-type mismatch); two exposed old custom parameter names in executed SQL rather than
wrong rows. Logs were kept under ignored artifacts/native-extraction-red-{sqlite,localdb}.log.

With minimal core expansion before delegation, **23/24** passed on each provider. The remaining
failure was deliberate diagnostic protection: native parameter names bypassed the old prefix guard,
so ToQueryString did not throw. This was an actual P1 failure under the initial privacy policy.
No replacement guard, TagWith marker, side-channel state, AsyncLocal or query-result wrapper was added.

The owner then explicitly chose native EF parameterization entirely and adopted native ToQueryString,
removing the old Raffinert diagnostic privacy guarantee. Tests were changed to verify that native rendering
can include values, not to pretend the old guarantee was preserved. Native getter exceptions after
expansion are forwarded unchanged; only wrapper-target resolution failures retain the core boundary's
sanitized error. This consciously changes the previous diagnostic policy.

The final shared native-acceptance suite has **34 cases on each provider**, all passing:

- Computed Parameter/Constant A -> B -> A, decimal conversion, nullable computation, client method,
  literal directives and automatic capture; actual command text and DbParameter values match controls.
- Six array/List<int> mode cases across replacement, same-list mutation, empty, duplicates, differing
  cardinalities and repeat; three nullable-array mode cases include null elements/null collection.
- Like literal/captured/computed patterns; Property and Collate literal/captured metadata on both providers.
  SQLite Glob and SQL Server DateDiffDay each execute against their real provider.
- Row-dependent and nested directives match native rejection before SQL; mixed nested conditions,
  projection directives and wrapper replacement produce current results.
- Twenty-five captures plus repeat: 26 commands, one EF compilation and one executed SQL shape.
- Native getter failure then recovery; native/rendering controls for automatic, explicit, computed and
  mixed projection secrets on cache miss/hit. Synthetic private values are not printed.

Successful method evaluation was observed nine times for three controls over three executions.
Successful property getters retain one read per tested execution. A failing reflected getter can be
retried by EF: the reentrant-failure test compares reads and nested command counts to a native control
instead of keeping the old assumption of one failed read. Native duplicate captures bind once in the
covered cases; different outer/inner values remain independent with distinct physical parameter names.

Additional regressions retain typed interfaces, wrapper delegates, unrelated Invoke methods, server
member translation, null/default relationships, all ten async terminals, direct operators, compilation
counts, wrapper reassignment, interleaved/shared-options and pooled/factory contexts, deferred execution,
interceptor order/rebuilds, cancellation and recovery. Recording factory removal no longer breaks
ordinary wrapper execution. Historical late-parameter prototype tests were rewritten as native cache-hit
binding tests; the obsolete nine custom-name-generator unit cases were removed with that implementation,
while actual collision/binding/getter/cache integration assertions remain.

## Gate ledger

| Gate | Status | Observed evidence |
| --- | --- | --- |
| B0 Baseline | PASS | Clean tracked baseline; Release 0 warnings/errors; 230 solution / 44 LocalDB |
| S1 Source/order | PASS | Exact-tag call map; original scoped provider descriptor retained; no direct extractor calls |
| F1 No-op | PASS after explicit EF9100 approval | 60d25bd; complete interface; 230 / 44 |
| R1 Native + RED | PASS | 3b4d1e4; 24 cases per provider, 3 pass / 21 fail; native/direct controls run first |
| E1 Early expansion | PASS | Final 34-case shared suite on both providers; core expansion only, no manual lifting |
| C1 Cache/current values | PASS | 26-value one-compilation/one-shape cases; A -> B -> A, reassignment, pooling and recovery |
| P1 Diagnostics | PASS under explicitly changed contract | Initial privacy failure recorded; native rendering behavior now approved and tested; prefix guard removed |
| Q1 Compiled/precompiled | PASS for documented scope | Stable sync/async closed wrappers and scalar delegate args; runtime captures fail before getters; no-marker precompile forwarding and wrapper/delegate early rejection; no AOT claim |
| D1 DI/public/providers | PASS | Ten compiler contract cases; type/factory scopes and single disposal; both compiler decorator orders; existing public-surface regressions |
| M1 Comparison | PASS | 376 -> 168 architecture source lines; native ownership; no ordinary lifting or parallel cache pipeline |
| V1 Final validation | LOCAL PASS; exact-head remote CI pending | 265 solution / 78 LocalDB; build/format/package/fresh Windows consumer verified; remote conclusions to be recorded after completion |

## Architecture comparison and changed files

| Measurement | Stable late-lifting baseline | Native compiler candidate |
| --- | --- | --- |
| Architecture files / physical source lines | Six files, 376 lines | Four files, 168 lines; 208 fewer (55% reduction), counting blank/comment lines consistently |
| Version-specific EF internal contracts | None | IQueryCompiler only; locally documented EF1001 plus its experimental member EF9100 |
| Ordinary evaluation/extraction/naming/binding | Custom scalar RuntimeCaptureVisitor, helper and name generator | Entirely original EF compiler |
| Cache preparation | Recording factory + scoped QueryExecutionState + decorated key generator | Native key generator/context/cache, with no preparation state |
| Tested native cases | Scalar capture/literal directives; computed/collections/functions restricted | Computed/client method, collection modes and provider intrinsics match controls |
| Diagnostics | Prefix guard blocks lifted values; native extraction errors were sanitized by late boundary | Explicitly approved native rendering/errors after expansion; no replacement privacy infrastructure |
| Compiled scope | Closed wrappers/scalar delegate params; captures rejected | Same baseline scope via retained public compatibility interceptor; native compiler creation forwarded |
| Provider/package evidence | 230 / 44 historical baseline | 265 / 78 current tests; fresh packed consumer and exact-head remote checks tracked separately |
| EF10 patch risk | Public contracts but custom extraction semantics | Much less custom code; new internal-interface maintenance risk requires patch revalidation |

Production changes:

- Add NativeExtractionQueryCompiler: scoped descriptor composition, ordinary early expansion, all five interface members.
- Remove QueryExecutionState, EfRuntimeParameters, RaffinertParameterNameGenerator, and EfQueryExpansion/RuntimeCaptureVisitor.
- Simplify RaffinertOptionsExtension to compiler registration; remove recording/cache/query-string decorators.
- Retain public RaffinertExpressionInterceptor and add ClosedWrapperExpansion solely for its compiled/standalone compatibility.
  It validates captures and normalizes CLR defaults; it never extracts names or binds values.
- Update XML/package descriptions, README, CHANGELOG and the consolidated efcore-integration guide.
- Add native shared acceptance and compiler/DI contract tests; update implementation-specific assertions to actual native behavior.
- Extend the isolated consumer with computed scalar, all collection modes and Like; CI packs all three consumer artifacts.

Core/QuerySyntax code, package identity/version 1.2.0, EF bounded ranges, public async operators and LocalDB
ownership/master/current-user/cleanup protections are unchanged. Missing/duplicate/non-scoped/instance
compiler descriptors are explicitly unsupported rather than silently overwritten.

## Packages and validation

All three nupkg and snupkg artifacts were built in artifacts/local-feed. Actual ZIP/nuspec inspection:

| Package | Library | Dependencies |
| --- | --- | --- |
| Core 1.2.0 | lib/netstandard2.0 | None |
| QuerySyntax 1.2.0 | lib/netstandard2.1 | Core 1.2.0 |
| EF adapter 1.2.0 | lib/net10.0 | Core 1.2.0; EF Core and Relational [10.0.11,11.0.0) |

No production SQL Server or QuerySyntax adapter dependency. Symbols, README and XML documentation
are included. The isolated consumer has no ProjectReferences and maps Raffinert packages to the neutral
local feed. A GUID-named fresh cache's .nupkg.metadata identifies that feed; its adapter DLL hash matches
the freshly built adapter. Windows consumer passed with EF 10.0.11.0/runtime 10.0.12.
Linux consumer verification is pending the exact-head CI run; no earlier head's result is substituted.

Final commands: solution restore/build/test/format; separate LocalDB restore/test/format; all three
Release packs; fresh-cache isolated restore/run; ZIP/nuspec/cache-metadata/hash inspection; git diff --check.
Solution totals: 62 + 5 + 14 + 184 = **265**. LocalDB: 23 + 21 + 34 = **78**. No failures/skips.

## Limitations and paths not executed

NativeAOT and actual EF precompiled-code generation are not certified. The precompile interface boundary
is tested using a recording compiler, separately from real sync/async compiled queries on the providers.
Custom mapped HasDbFunction is not tested because there is no real mapping fixture. Other providers,
SQL Server installations/collations and EF majors/patches are not certified. Native collection cardinality,
null behavior and client evaluation are provider/EF responsibilities, not universal Raffinert guarantees.
Exact-head remote Windows/Linux consumer and solution conclusions remain pending in this first report snapshot.
