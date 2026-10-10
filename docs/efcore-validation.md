# EF Core integration validation

## Baseline

Inspected on 2026-10-10 at commit `8c8c9ad`, on `main`. Existing untracked
planning documents and `pr_summary.md` were preserved.

- SDK: 10.0.401, selected by the existing `global.json` (10.0.400/latestPatch).
- `dotnet restore Raffinert.Expressions.slnx`: passed.
- `dotnet build Raffinert.Expressions.slnx --no-restore`: passed, zero warnings/errors.
- `dotnet test Raffinert.Expressions.slnx --no-build`: 64 passed, zero failed/skipped
  (45 core, 5 QuerySyntax, 14 SQLite integration).
- Core targets netstandard2.0; QuerySyntax targets netstandard2.1 and references only core.
- Core's internal expander has generic and owner-aware entry points, using one visitor.
- QuerySyntax expands at operator boundaries and retains the original provider.
- Existing SQLite integration tests pin EF Core 10.0.11 on net10.0.

## API inspection

The installed EF Core 10.0.11 XML documentation and the
[EF Core 7.0.20 public interface source](https://github.com/dotnet/efcore/blob/v7.0.20/src/EFCore/Diagnostics/IQueryExpressionInterceptor.cs)
confirm `Expression QueryCompilationStarting(Expression, QueryExpressionEventData)`.
All ten predicate-consuming async terminal methods are available in the baseline API.

EF's query compiler extracts parameters before looking up the compiled query cache
and invoking compilation. See the source for
[EF 7](https://github.com/dotnet/efcore/blob/v7.0.20/src/EFCore/Query/Internal/QueryCompiler.cs)
and [EF 10](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/Query/Internal/QueryCompiler.cs).
This ordering must be covered by executable cache regression tests.

## Required adjustment to the proposed interceptor

The first EF 10 SQLite regression failed with `Unable to resolve expression instance`
using only `IQueryExpressionInterceptor`: the captured condition had already become an
EF query parameter. Preventing evaluation through a filter plugin was also insufficient:
EF parameterized the enclosing closure instead.

The implementation therefore decorates two **public** EF extension interfaces:
`IQueryContextFactory` to make extracted wrapper values available within the current
context scope, and `ICompiledQueryCacheKeyGenerator` to use the expanded query for the
provider's existing key generation. The interceptor uses the same normalization and
core expansion engine. This addresses both lost wrapper instances and stale cached
conditions without wrapping the LINQ provider, replacing translation preprocessing,
or calling any EF private/internal API.

EF 10 renamed the public `QueryContext.ParameterValues` property to `Parameters` and
EF 9 introduced public `QueryParameterExpression`. The adapter resolves these public
members at runtime to keep the single EF 7-compiled assembly compatible. Compatibility
must be verified by the runtime matrix, not inferred from reflection alone.

Scalar closure members introduced by expansion are snapshotted because EF's parameter
extraction has already run. They participate in the expanded cache key; changing them
produces a new key. Scalars already present in ordinary LINQ retain EF parameterization.
Wrapper structure remains immutable as required by core. Property getters can run
during expansion and must return stable values throughout a query execution.

## Local compatibility matrix

All matrix projects link the same complete adapter integration test source. Production
always compiles against EF 7.0.20 on net6.0; only the consumer provider changes.

| EF Core / SQLite | Test target | Runtime | Passed | Failed / skipped |
| --- | --- | --- | --- | --- |
| 7.0.20 | net6.0 | 6.0.36 | 58 | 0 / 0 |
| 8.0.31 | net8.0 | 8.0.31 | 58 | 0 / 0 |
| 9.0.20 | net8.0 | 8.0.31 | 58 | 0 / 0 |
| 10.0.11 | net10.0 | 10.0.12 | 58 | 0 / 0 |

Commands:

```shell
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef7/Ef7.csproj
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef8/Ef8.csproj
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.CompatibilityTests/Ef9/Ef9.csproj
dotnet test tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests.csproj
```

The EF 7 leg required installing the retired .NET 6.0.36 runtime into a temporary
local dotnet directory; it executed on .NET 6, without rolling forward to .NET 8/10.
The adapter DLL SHA256 was identical across the production and EF 7/8/9 outputs:
`E2107D5AC2E63712D11B8C9B2F51784EF20CDF9C366D22CFF960B74078550027`.
This hash records the pre-documentation checkpoint; later source/version changes
produce a new hash.

CI now defines the same four explicit version legs on Windows and Ubuntu 22.04,
uploading TRX results per version/OS. Remote CI has not been run in this session.

## Checkpoints

- Project boundary: `dotnet build src/Raffinert.Expressions.EntityFrameworkCore/Raffinert.Expressions.EntityFrameworkCore.csproj` passed with zero warnings/errors.
- Whole-root expansion: `dotnet test Raffinert.Expressions.slnx --no-restore` passed 76 tests (57 core, 5 QuerySyntax, 14 existing integration).
- Interceptor/registration/cache checkpoint: adapter test project passed 5 SQLite tests.
- Async operators: adapter test project passed 36 tests, including all ten operators without interception.
- Composition/safety: adapter test project passed 58 tests; full solution passed 134.
- Normal xUnit class parallelization exercises the shared singleton interceptor across separate context scopes. Each test owns its open SQLite connection; no context is used concurrently.

## Safety and verified limits

- The EF adapter calls the existing core engine; no second invocation expander exists.
- No query provider, translation preprocessor or private EF API is replaced or called.
- Normalization only snapshots supported scalar members and folds null/scalar defaults introduced after parameter extraction. Ordinary queries retain provider parameterization and cache-key semantics.
- Core evaluator getter/constructor behavior is unchanged and tested, including inner exceptions. Arbitrary row-dependent factories fail expansion; no delegate compilation or client filtering is used by the adapter.
- Execution tracking uses a context-scoped weak reference; the singleton interceptor retains no query, context, wrapper or parameter values. Expanded roots are not cached by the adapter.
- Wrapper reassignment in the same query/closure, changing scalar closure values inside a stable wrapper, repeated execution and shared-options cross-context execution all pass.
- Optional relationships and nullable/nonnullable value inputs execute SQLite null/default logic; SQL assertions require CASE/LEFT JOIN where appropriate.
- Cancellation tokens reach the command interceptor unchanged; pre-cancellation raises OperationCanceledException. EF terminal exceptions are preserved.
- Stable closed wrappers work in EF compiled sync/async queries with scalar delegate parameters. Wrapper delegate parameters fail before SQL; reassigned closed wrappers remain fixed at compilation, matching the documented restriction.
- Only SQLite has been tested. Configure the provider before registration; manually supplied internal service providers and replacement of the decorated services are outside this integration's tested configuration.
