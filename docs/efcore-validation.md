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
