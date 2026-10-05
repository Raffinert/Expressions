# LINQ extensibility recommendations

This review compares Raffinert.Expressions with LINQKit and the request in
[EF Core issue #15670](https://github.com/dotnet/efcore/issues/15670) for built-in support for dynamically extended queries.
The issue is open and in the backlog; it does not define an EF Core API to adopt.

## Current position

Raffinert.Expressions already supports reusable conditions and projections, `And`/`Or` composition,
nested `Invoke` expansion, and translation through EF Core. Its core LINQ overloads expand a wrapper
before calling the underlying `Queryable` operator. The optional `AsRaffinertQuery()` facade expands
query-syntax clauses while retaining the original provider. The repository's SQLite integration tests
cover both approaches, and the LINQKit comparison example exercises corresponding scenarios.

The principal difference is the expansion boundary. LINQKit's `AsExpandable()` can rewrite the complete
query through a provider wrapper. Raffinert expands expressions handed to its own APIs; an `Invoke`
inside a lambda passed directly to an ordinary provider-facing operator is not expanded. Provider-specific
operators can also leave the query-syntax facade.

## Recommended work, in priority order

### 1. Publish expansion for ordinary lambdas

Expose a public, typed `Expand<TDelegate>(this Expression<TDelegate> expression)` API backed by the
existing internal `ExpressionExpander`. This lets callers explicitly expand a lambda before passing it
to standard `Queryable` methods, EF Core async methods such as `AnyAsync`, or other provider APIs.

For example:

```csharp
Expression<Func<Product, bool>> predicate = product => condition.Invoke(product);
var products = db.Products.Where(predicate.Expand());
```

Preserve the current failure for unresolved invocation targets and cycle detection. Test nested markers,
captured wrappers, method groups, and a provider-facing EF Core query. This provides a useful escape hatch
without changing the core package's provider-independent design.

### 2. Consider an opt-in EF Core integration package for whole-query expansion

For users who need LINQKit-like coverage, an EF Core-specific package could implement
[`IQueryExpressionInterceptor`](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#query-expression-interception)
and expand Raffinert markers before EF compiles a query. Keep this separate from the `netstandard2.0`
core package and opt in through `DbContext` configuration. The interceptor should be stateless because
EF Core normally shares one interceptor instance across contexts.

Prototype this against queries with nested subqueries, standard LINQ operators, provider-specific
operators, asynchronous execution, and `ToQueryString()`. Document how it interacts with the existing
facade and direct overloads. This is the closest equivalent to the broad query rewriting requested in
EF Core issue #15670, but it has a larger compatibility and testing surface than explicit expansion.

### 3. Make `InvokeOrDefault` evaluate its input once

Implemented: nontrivial arguments are bound through a lambda invocation, so in-memory evaluation reads
the argument once. Regression tests cover side-effecting method and property arguments. SQLite
integration tests cover translation in both projections and predicates. Other LINQ providers may differ
in their support for translating the invocation node.

### 4. Add balanced composition for large dynamic predicate sets

`Condition.And` and `Condition.Or` combine two expressions at a time. Folding a large list creates a
deep tree. Consider `CombineAll` and `CombineAny` APIs that define the empty-set identities (`True`
and `False`) and build balanced trees. Test hundreds of terms for expansion, translation, and stack
depth. Benchmark before adding a general expression optimizer.

### 5. Measure query shape and parameterization

Extend the existing LINQKit comparison with measurements of generated SQL, expansion cost, and EF
query-cache behavior as captured values change. EF Core [caches queries by expression-tree shape](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#query-caching-and-parameterization),
so the goal is to confirm that composition retains stable shapes and parameterized values. Keep these
measurements separate from result-equivalence checks.

## Suggested sequence

Implement the public lambda expansion API and `InvokeOrDefault` correctness work first. They are
contained changes that improve the existing model. Use the comparison example to assess demand and
performance, then add the EF Core integration package if whole-query expansion remains a practical
need. Balanced predicate composition can follow as a focused convenience and scalability feature.

## Relevant project files

- `src/Raffinert.Expressions/Core/ExpressionExpander.cs` — internal expansion engine and `InvokeOrDefault` rewrite.
- `src/Raffinert.Expressions/Conditions/Condition.cs` — pairwise Boolean composition.
- `src/Raffinert.Expressions.QuerySyntax/RaffinertQueryable.cs` — query-syntax expansion facade.
- `tests/Raffinert.Expressions.IntegrationTests/EfCoreExpressionTests.cs` — SQLite translation coverage.
- `examples/LinqKitComparison/README.md` — side-by-side LINQKit scenarios.
