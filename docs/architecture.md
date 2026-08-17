# Architecture

## Shared expression abstraction

`ComposableExpression<TSource,TResult>` owns expansion and compiled-delegate caching. `Specification<T>` and `Projection<TSource,TResult>` add domain vocabulary while sharing that implementation and the internal `IExpressionExpansionSource` contract.

Implementations of `GetExpression()` are assumed stable for the lifetime of a wrapper after its first expansion or compilation request.

## Invocation expansion

`ExpressionExpander` recognizes only two methods on Raffinert expression objects: `Invoke` and `InvokeOrDefault`.

Targets may be constants, captured/static field or property chains, or directly constructed expression objects. Resolution never evaluates parameter-dependent subtrees. Once resolved, the engine expands the referenced lambda, substitutes its sole parameter with the visited call argument, and recursively visits the result.

Method-group delegate nodes whose target is a Raffinert wrapper are replaced with the corresponding expanded lambda. Unrelated methods named `Invoke` or `Map` are ignored.

A reference-identity stack detects cycles and produces an `InvalidOperationException`. Recognized calls that cannot be resolved fail rather than leaking a partially expanded marker to a provider.

## Parameter substitution

`ReplaceExpressionVisitor` replaces one exact expression node with an arbitrary expression. When the source is a parameter declared by a nested lambda, that lambda is treated as a scope boundary. Composition therefore preserves deliberately shadowed nested scopes.

The engine never introduces `Expression.Invoke`.

## Cross-composition and `Then`

Because all semantic wrappers implement the same internal contract, nested expansion does not distinguish between predicates and projections. A Boolean output can be inserted into a member initializer, and a scalar/object projection can be inserted into a predicate.

`Then` composes already-expanded typed lambdas with direct substitution. `Projection<A,B>.Then(Projection<B,C>)` yields `Projection<A,C>`; `Projection<A,B>.Then(Specification<B>)` yields `Specification<A>`.

## Null-safe invocation

`InvokeOrDefault` is rewritten to `argument == default ? default(TOut) : expandedBody` for nullable inputs. It is defined on the shared expression base, so it can explicitly produce defaults such as `false` for specifications and `0` for value projections. Normal invocation has no implicit null semantics.

## Projection transformations

`MergeBindings` normalizes member initializers and supported conditional branches into member-keyed assignments. Constructors must be compatible and parameterless. Conflicts follow the selected policy.

`MapToExisting` accepts member-initializer roots and supported conditional branches. Scalar assignments become destination assignments. Nested member initializers recursively update existing nested instances after an explicit null guard. Automatic nested construction is intentionally outside scope.

## Structural templates

One template engine backs `ExpressionTemplate` and the `SpecificationTemplate` facade. It validates direct readable sample-member selections, then rewrites accesses on the template parameter to uniquely named compatible public target properties or fields. Missing, ambiguous, unreadable, and incompatible members fail before query execution.

## Supported and unsupported shapes

Provider-facing expansion supports any ordinary expression nodes inside a reusable lambda. The restriction applies only to resolving the wrapper object: arbitrary parameter-dependent code is not executed during expansion.

Advanced projection transforms intentionally support a smaller set:

- merge: parameterless `MemberInitExpression` and compatible conditionals;
- update existing: member initializer roots and supported conditional branches;
- templates: anonymous/object shapes made of direct member reads.

Unsupported shapes fail descriptively rather than being partially transformed.
