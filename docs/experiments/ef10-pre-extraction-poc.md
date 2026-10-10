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
