# Architecture

## Shared expression abstraction

`IExpr<TSource,out TResult>` is the covariant public consumption contract used by the LINQ extensions.
`Expr<TSource,TResult>` is the **single public representation** for reusable predicates and value/result expressions.
It owns expansion and compiled-delegate caching. Forward composition and Boolean combinators accept the covariant `IExpr<TSource,out TResult>` contract; source/result structural adaptation remains on the concrete type, while member-initializer binding and mapping operations live in `MemberInitializationExtensions`.
`Expr<TSource>.Create(...)` infers the result type; `Expr<TSource,TResult>.Create(...)` is explicitly typed.
Boolean composition (`And`, `Or`, `Not`) is exposed for `IExpr<TSource,bool>` through extensions.
Mapping-only caches are attached on demand through a `ConditionalWeakTable`, so ordinary predicates do not allocate mapping state. The mapping APIs require a reference-type destination; other expression-shape requirements (such as member-initializer roots for merging or result adaptation) are checked at runtime. The engine continues to use the internal `IExpressionExpansionSource` contract.

Custom semantic wrappers should normally inherit `Expr<TSource,TResult>`, which provides invocation-marker expansion, cycle detection, and per-instance caching. Implementing `IExpr<TSource,TResult>` directly supports LINQ consumption and direct Boolean/forward composition, but does not enable the engine to recognize those external implementations as nested `Invoke` markers.

Implementations of `GetExpression()` are assumed stable for the lifetime of a wrapper after its first expansion or compilation request.

`Expr<T>.Start()` returns an immutable starting condition. Its first `And` or `Or` returns the supplied
condition without composing a Boolean seed. An unchanged starter uses its configured result when empty,
which defaults to `false`. Ordinary `True` and `False` conditions retain standard Boolean composition.

## Invocation expansion

`ExpressionExpander` recognizes only two methods on Raffinert expression objects: `Invoke` and `InvokeOrDefault`.

Targets may be constants, captured/static field or property chains, or directly constructed expression objects. Resolution never evaluates parameter-dependent subtrees. Once resolved, the engine expands the referenced lambda, substitutes its sole parameter with the visited call argument, and recursively visits the result.

Method-group delegate nodes whose target is a Raffinert wrapper are replaced with the corresponding expanded lambda. Unrelated methods named `Invoke` or `Map` are ignored.

A reference-identity stack detects cycles and produces an `InvalidOperationException`. Recognized calls that cannot be resolved fail rather than leaking a partially expanded marker to a provider.

## Parameter substitution

`ReplaceExpressionVisitor` replaces one exact expression node with an arbitrary expression. When the source is a parameter declared by a nested lambda, that lambda is treated as a scope boundary. Composition therefore preserves deliberately shadowed nested scopes.

The engine never introduces `Expression.Invoke`.

## Cross-composition and `Then`

Because all semantic wrappers implement the same internal contract, nested expansion does not distinguish between conditions and projections. A Boolean output can be inserted into a member initializer, and a scalar/object projection can be inserted into a condition.

`Then` composes already-expanded typed lambdas with direct substitution. `Expr<A,B>.Then(Expr<B,C>)` yields `Expr<A,C>`; `Expr<A,B>.Then(Expr<B, bool>)` yields `Expr<A, bool>`.

## Null-safe invocation

`InvokeOrDefault` is rewritten to `argument == default ? default(TOut) : expandedBody` for nullable inputs. It is defined on the shared expression base, so it can explicitly produce defaults such as `false` for conditions and `0` for value projections. Normal invocation has no implicit null semantics.

## Projection transformations

`MergeBindings` normalizes member initializers and supported conditional branches into member-keyed assignments. Constructors must be compatible and parameterless. Conflicts follow the selected policy.

`MapToExisting` accepts member-initializer roots and supported conditional branches. Scalar assignments become destination assignments. Nested member initializers recursively update existing nested instances and create missing writable instances from the projection. Missing read-only nested instances fail with a descriptive exception.

Mutable collection assignments follow clear-and-refill semantics. Existing `ICollection<T>` and `IList` instances are preserved, cleared, and populated with the projected elements. A null projected collection clears an existing mutable collection to empty, while a missing writable collection is created. Arrays, writable members exposed only as `IEnumerable<T>`, and known read-only wrappers are replaced. Collection-initializer bindings update getter-only mutable collections. Existing elements are not matched or updated by key.

## Structural adaptation

Existing conditions and projections act as their own structural definitions. `StructuralExpressionAdapter`
rewrites parameter-rooted public property and field paths against a new source type. Projection result adaptation
reconstructs parameterless member initializers against a new result type and recursively handles nested member
initializers and null/default conditional branches. The resulting wrapper contains an ordinary typed expression;
there is no public template abstraction or runtime adaptation during query execution.

## Supported and unsupported shapes

Provider-facing expansion supports any ordinary expression nodes inside a reusable lambda. The restriction applies only to resolving the wrapper object: arbitrary parameter-dependent code is not executed during expansion.

Advanced projection transforms intentionally support a smaller set:

- merge: parameterless `MemberInitExpression` and compatible conditionals;
- update existing: member initializer roots and supported conditional branches;
- result adaptation: parameterless member initializers, including nested initializers and compatible conditionals.

Unsupported shapes fail descriptively rather than being partially transformed.
