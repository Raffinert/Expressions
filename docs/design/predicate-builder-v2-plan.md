# PredicateBuilder<T> implementation plan (PR #17, v2)

**Status:** COMPLETED — implemented in PR #17 and validated by the [five-job CI run](https://github.com/Raffinert/Expressions/actions/runs/41186373590) (Linux/Windows Verify, EF10 package-smoke tests on both platforms, and SQL Server LocalDB). This document records the implementation contract and acceptance criteria.
**Target:** the current PR #17 branch, `feat/unified-composable-expressions-v2`.
**Constraint:** retain a single canonical result type: `Expr<T, bool>` implementing `IExpr<T, bool>`. Do **not** introduce a `Condition<T>`, `Predicate<T>`, or `BoolExpr<T>` runtime wrapper.

## 1. Goal and boundaries

Replace the special Boolean "starter" on `Expr<T>` with an explicitly mutable, scoped builder, while retaining ordinary immutable expression composition:

```csharp
var predicate = PredicateBuilder<Order>
    .Start()
    .And(o => o.IsActive)
    .And(o => o.Total > 1000m)
    .Or(o => o.IsPriority)
    .Build(); // Balanced by default

Expr<Order, bool> alwaysTrue = Expr<Order>.Create(_ => true);
Expr<Order, bool> alwaysFalse = Expr<Order>.Create(_ => false);

var direct = Expr<Order>.Create(o => o.IsActive)
    .And(o => o.Total > 1000m); // This existing fluent API stays.
```

**In scope:** builder factory and fluent API, defined empty-state semantics, default-enabled associative balancing with an explicit opt-out, interoperability with native/external/variant `IExpr`, tests and migration docs, retiring `Expr<T>.Start/True/False` and the old `BooleanStartExpression<T>` special case; do not reintroduce Boolean constant factories on the builder.

**Out of scope:** changing `Expr<TSource,TResult>`, introducing structural global caches or new concurrency machinery, automatically balancing existing `IExpr.And/Or` calls, broad expression optimizers (EF-specific or otherwise), or changes to `MapToExisting`.

## 2. Public API — minimal v2 surface

Create `src/Raffinert.Expressions/Predicates/PredicateBuilder.cs` in namespace `Raffinert.Expressions`:

```csharp
public sealed class PredicateBuilder<T>
{
    public static PredicateBuilder<T> Start(bool defaultWhenEmpty = false);
    public PredicateBuilder<T> And(IExpr<T, bool> predicate);
    public PredicateBuilder<T> And(Expression<Func<T, bool>> predicate);
    public PredicateBuilder<T> Or(IExpr<T, bool> predicate);
    public PredicateBuilder<T> Or(Expression<Func<T, bool>> predicate);
    public PredicateBuilder<T> Not();

    public Expr<T, bool> Build(bool balanced = true);
}
```

Include `using System.Linq.Expressions;` and XML docs. Public `And/Or/Not` mutate the **builder**, return `this`, and are *not* thread-safe. The resulting `Expr<T,bool>` is an immutable snapshot and uses its existing caching/thread-safety contract. Each static factory returns a **new builder**, never a shared mutable singleton.

No implicit conversion to `Expr`, `Build` on `IExpr`, or generic builder base classes in this PR. A `From(...)` factory and richer `BuildOptions` can be revisited only if a concrete consumer requires them.

## 3. State and Boolean semantics — write tests before implementation

A builder has (a) a current composition tree and (b) an empty fallback value. `Start()` means **empty**, not a `false` operand. For standalone constant predicates, use `Expr<T>.Create(_ => true/false)` without the builder.

| Sequence | Expected expression / semantics |
|---|---|
| `Start().Build()` | Constant `false` |
| `Start(true).Build()` | Constant `true` |
| `Start().And(A).Build()` | `A`, no Boolean seed |
| `Start().Or(A).Build()` | `A`, no Boolean seed |
| `Start(true).Or(A).Build()` | `A`, despite empty fallback `true` |
| `Start().Not().Build()` | `!false`, i.e. `true` |
| `Start(true).Not().Build()` | `!true`, i.e. `false` |
| `Start().And(A).Or(B).And(D).Build()` | `(A || B) && D` (left-to-right fluent composition) |

Fluent calls are composed in call order; the builder does not reinterpret `And` and `Or` according to C# operator precedence.

`Not()` on an empty starter **materializes the configured fallback** as a constant, negates it, and makes the builder non-empty; subsequent `And/Or` must no longer skip the new left operand. `Not()` on a non-empty tree wraps the entire current tree, not only its last condition.

Null `IExpr` and null lambda operands throw `ArgumentNullException` at the corresponding `And/Or` call (not deferred until `Build`). `Build()` never changes builder state. Later `And/Or/Not` calls must not change previously built `Expr` instances. Repeated `Build` calls are allowed; **no reference-identity or builder-level caching guarantee** in v2.

A builder instance must not be reused concurrently or mutated concurrently with `Build`. This is intentional; only the finished `Expr` needs safe concurrent reading under its existing contract.

## 4. Internal design (avoid eager expansion and quadratic chains)

Use a small **private composition AST** rather than repeatedly composing `System.Linq.Expressions` on every `And/Or`:

- `Leaf`: holds a raw `Expression<Func<T,bool>>` or an `IExpr<T,bool>` reference.
- `Constant`: holds `true/false` seed or materialized empty fallback.
- `And`, `Or`: binary nodes representing *short-circuiting* operations.
- `Not`: unary node wrapping the entire current subtree.

Each fluent operation appends/wraps in O(1) without calling `GetExpandedExpression()` or `Compile()`. Therefore building a chain with 1,024+ clauses does not repeatedly traverse its growing prefix.

`Build()`:

1. Snapshot the current root (or create a standalone constant root if empty).
2. Use a single `ParameterExpression` of type `T` for the resulting lambda.
3. Lower each leaf exactly once during that build. For native `Expr<T,bool>`, obtain `GetExpression()` without eager nested expansion (as today's `BooleanExpressionExtensions.GetComposableExpression` does). For external `IExpr` and contravariant views, use `ExprAdapter.GetExpandedExpression` with shape validation and safe type adaptation. Normalize parameters with the existing `ReplaceExpressionVisitor` (respect nested lambda scopes).
4. Emit `Expression.AndAlso`, `Expression.OrElse`, and `Expression.Not`; do not use `Expression.Invoke`, `Expression.Compile`, or executable `Func<T,bool>` in the tree.
5. Return `Expr<T, bool>.Create(Expression.Lambda<Func<T, bool>>(body, parameter))`. Never close over mutable builder fields; later builder mutations cannot affect the result.

Keep AST traversal/build **linear** in node count; use an explicit stack where necessary to handle long unbalanced chains without self-inflicted stack overflow. Tests must also verify that nested native `Invoke` expansion remains lazy until the resulting `Expr` is expanded/evaluated, in line with PR #17's performance regression fix.

Do not accidentally put a wrapper's private expression shape into a global cache, mutate stored leaf expressions, or eagerly rewrite a chain on every append.

## 5. Default balancing — enabled unless explicitly disabled

`Build()` is equivalent to `Build(balanced: true)` and emits a balanced tree wherever doing so is semantics-preserving. `Build(balanced: false)` faithfully emits the left-associative AST. The Boolean parameter is named `balanced` (not `balance`) and defaults to `true`.

`Build(balanced: true)` balances **only contiguous groups of builder-owned identical associative operators** (`AndAlso` with `AndAlso`; `OrElse` with `OrElse`). Flatten such a group into an ordered list of its operands and rebuild as a near-balanced binary tree, with no reordering, distribution, simplification, or duplication.

For example:

```text
Unbalanced: (((A && B) && C) && D)
Balanced:   ((A && B) && (C && D))

Mixed:       (((A && B) || C) && D)
Allowed:     rebalance the (A && B) subgroup only;
             never transform into ((A || C) && (B || C)) or reorder terms.
```

Treat mixed `And/Or`, `Not`, and **opaque leaf** boundaries as barriers. Do not balance inside externally supplied expressions, nested lambda bodies or nested `Invoke` results. `Expression.AndAlso/OrElse` retain left-to-right short-circuit behavior for Boolean operands; test this with side-effect counting in in-memory delegates (while keeping production predicates pure).

For a homogeneous chain with N leaves, aim for output depth O(log N) with balancing versus O(N) without; no dependency on expression-tree string formatting. A balanced tree may improve expansion/visitor depth, **not necessarily EF SQL runtime**. Balance by default as a design choice for the builder; verify EF provider compatibility and performance, and retain `Build(balanced: false)` for an exact left-associative shape when consumers need it.

### Reuse assessment: PR #15 (reviewed 2026-10-31)

**Source:** [PR #15 — Add opt-in Condition<T>.Balance()](https://github.com/Raffinert/Expressions/pull/15), head `541feec50b55337bd7e51619c3b24cfa8e15ca5e` (open and not merged).
- [`BooleanExpressionBalancer.cs`](https://github.com/Raffinert/Expressions/blob/541feec50b55337bd7e51619c3b24cfa8e15ca5e/src/Raffinert.Expressions/Conditions/BooleanExpressionBalancer.cs)
- [`ConditionBalanceTests.cs`](https://github.com/Raffinert/Expressions/blob/541feec50b55337bd7e51619c3b24cfa8e15ca5e/tests/Raffinert.Expressions.UnitTests/ConditionBalanceTests.cs)

**Reuse the algorithms, not the v1 API.** PR #15 already implements:
- `Collect`: iterative in-order flattening of maximal homogeneous `AndAlso`/`OrElse` groups while preserving operand order;
- `Build`: midpoint division to create a balanced tree with O(log N) Boolean depth; recursion here is shallow after collecting operands;
- an iterative bottom-up traversal through alternating groups and built-in Boolean `Not`, without crossing `And`/`Or` boundaries;
- `CanReassociate`'s safety restrictions: built-in `AndAlso`/`OrElse`, `Method == null`, non-lifted, Boolean operands and result. Treat other operators as opaque.

**Required adaptation for `PredicateBuilder<T>`:**
1. Prefer porting the ordered collect/rebuild and bottom-up stack approach to the builder's **private composition AST** before lowering to `System.Linq.Expressions`. A leaf containing an `IExpr<T,bool>` or a supplied lambda must be an **atomic, opaque boundary**, even if its body internally contains built-in `AndAlso`/`OrElse`. PR #15's expression-level `Balance(body)` would otherwise rebalance inside user-supplied leaves and violate the builder's stated scope.
2. Preserve the PR #17 approach to native `Expr<T,bool>` leaf handling: use `GetExpression()` and avoid eager `GetExpandedExpression()` / `Compile()`. Use `ExprAdapter` for external and contravariant sources; keep all parameter rebinding scope-safe. Do **not** port the v1 `Condition<T>`, `Condition<T>.Balance()`, operators, or `BooleanExpressionComposer` duplicate.
3. Keep `Build(balanced: false)` exactly left-associative, and make both `Build()` and `Build(balanced: true)` use the ported algorithm. An unbalanced mixed-operator chain may remain deep by design; iteration during lowering is required either way.
4. Reuse PR #15 tests conceptually: 1,024 AND/OR clauses; mixed and nested groups; built-in `Not`; 2,048 alternating nodes; short-circuit order; identity/snapshot and repeat-build behavior; idempotent tree shape. Rebind them to `PredicateBuilder<T>` and compare balanced versus unbalanced `Build` results. Add **opaque-leaf**, external/variant `IExpr`, 4,096-node and EF Core integration regressions that PR #15 does not cover.
5. Avoid copying two failing test assertions from PR #15 verbatim: `Assert.IsType<BinaryExpression>(...)` incorrectly demands that abstract `BinaryExpression` be the *exact* runtime type. On .NET the returned node is `LogicalBinaryExpression`. Use `Assert.IsAssignableFrom<BinaryExpression>(...)` or `Assert.IsType<BinaryExpression>` only if exact runtime type truly matches (it does not here).

**Verification caveat:** PR #15's [last CI run](https://github.com/Raffinert/Expressions/actions/runs/39630538423) failed on both platforms in `ConditionBalanceTests.PreservesMixedGroupsAsOpaqueOperands` and `BalancesNestedGroupsWithoutCrossingAndOrOrNotBoundaries` because of the above exact-type assertions. Other integration tests passed; SQL Server LocalDB had a separate restore failure. Therefore PR #15 is a **source of reusable logic and test scenarios, not a CI-validated implementation ready for cherry-pick**.

**Decision:** PB-03 should port and adapt PR #15's proven structure, with corrected tests and PR #17's stronger leaf/type boundaries; no dependency on merging PR #15 first.

## 6. Integration and compatibility

- Existing immutable `IExpr<T,bool>.And/Or/Not` extension methods remain public and unchanged in behavior.
- `PredicateBuilder<T>` accepts native `Expr`, external `IExpr`, and contravariant `IExpr<Base,bool>` viewed as `IExpr<Derived,bool>`. The emitted tree must type-check and be consumable by `Enumerable`, `Queryable` and the EF Core adapter.
- Keep the single result type: `Build()` returns `Expr<T,bool>`, which can be passed to `Where`, `AnyAsync`, `Then`, nested `Invoke`, etc.
- Remove the Boolean-specific `Start/True/False` members from `src/Raffinert.Expressions/Core/ExprFactory.cs` and delete the internal `BooleanStartExpression<T>`. Remove its special branches in `BooleanExpressionExtensions.And/Or`; after migration, every `Expr<T,bool>` is an ordinary Boolean operand. `Expr<T>.Create` remains a general expression factory.
- Migrate former `Expr<T>.True/False` singletons to explicit `Expr<T>.Create(_ => true/false)` constants. These create new wrappers per call unless cached by consumers. `PredicateBuilder<T>` deliberately exposes no `True/False` factories.
- No source compatibility shims: this PR is already a breaking v2.0 API. Do not touch the historical design documents except their explicit current-API notices.

## 7. Implementation sequence (small commits on the same PR branch)

### PB-01 — Contract and tests (red stage)

Add `tests/Raffinert.Expressions.UnitTests/PredicateBuilderTests.cs` with tests for all state-table rows, null validation, `Not`, returned builder identity, snapshot independence, empty builds, native and external `IExpr`, and 1,024-operand builds. The new tests may fail until PB-02; commit only when CI-ready if the branch policy requires green commits.

### PB-02 — Builder, AST and lowering

Create `src/Raffinert.Expressions/Predicates/PredicateBuilder.cs`; private AST may be factored into `PredicateBuilderNodes.cs` if size warrants it. Implement O(1) appends and unbalanced `Build()`, parameter rebinding, safe contravariance and no eager `Invoke` expansion. Keep ordinary `Expr` caches unchanged. Pass PB-01 tests before moving on.

### PB-03 — Default-on balancing

Port and adapt the iterative homogeneous-group collection, bottom-up traversal and ordered midpoint rebuild from [PR #15](https://github.com/Raffinert/Expressions/pull/15) **onto the private builder AST**. Keep opaque leaves opaque, and enable this behavior via the default `Build()` / `Build(balanced: true)`, with `Build(balanced: false)` preserving the unbalanced shape. PB-02 may initially lower unbalanced trees internally, but PB-03 must be complete before declaring the public API done. Tests: N = 1, 2, 3, 127, 1,024, 4,096; equivalent in-memory results, identical exception/side-effect order, short-circuit skip counts, mixed operator groups and `Not` boundaries, structural depth reduction. Avoid recursion-based stack overflows for long chains.

### PB-04 — Retire legacy Boolean factory and migrate consumers

Remove `Expr<T>.Start`, `Expr<T>.True/False`, and `BooleanStartExpression<T>`. Remove seed-specific fast paths from `BooleanExpressionExtensions`; do not change the behavior of ordinary composed `Expr` expressions.

Migrate existing call sites, especially:

- `tests/Raffinert.Expressions.UnitTests/ConditionTests.cs`
- `tests/Raffinert.Expressions.UnitTests/UnifiedExpressionTests.cs`
- `tests/Raffinert.Expressions.UnitTests/VarianceExpressionTests.cs`
- `tests/Raffinert.Expressions.UnitTests/WholeQueryExpansionTests.cs`
- `tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/AsyncConditionTests.cs`
- `examples/LinqKitComparison/RaffinertExamples.cs`
- `README.md`, `docs/architecture.md`, `docs/migration.md`, `CHANGELOG.md`, the `.agents` skill docs.

Change identity assertions tied to the old immutable starter into semantic/snapshot assertions. Preserve long-chain and EF regression coverage. Search all source, tests, examples and current docs for old factory references and stale `BooleanStartExpression`.

### PB-05 — Provider verification and final review

Add SQLite integration tests in `tests/Raffinert.Expressions.EntityFrameworkCore.IntegrationTests/CompositionTests.cs` (or a dedicated file) for direct `Where(builder.Build())`, nested `Invoke`, balanced mixed operations, dynamic scalar captures and contravariant inputs. Ensure generated SQL contains no library `Invoke` markers and keeps EF-native parameterization.

Run the PR's existing Linux/Windows `dotnet format --verify-no-changes`, solution tests and pack, both EF10 isolated package-smoke jobs, and real Windows SQL Server LocalDB tests. Do not merge before all required jobs pass. If practical, compare allocation/build time and expansion depth for 128/1,024/4,096 predicates using BenchmarkDotNet; record measurements rather than asserting unstable performance thresholds in unit tests.

## 8. Definition of done / non-goals checklist

- [x] Public `PredicateBuilder<T>` API implemented and documented.
- [x] `Start/Not` and both empty fallback values covered; standalone `true/false` expressions use `Expr<T>.Create`.
- [x] `Build()` produces an immutable, independent `Expr<T,bool>` snapshot.
- [x] Native, external and contravariant `IExpr` accepted without unnecessary eager expansion.
- [x] Default `Build()` uses balancing; `Build(balanced: false)` preserves left-associative shape.
- [x] Balanced and unbalanced output semantically identical, with left-to-right short-circuit order.
- [x] PR #15's iterative balancing scenarios ported with corrected `BinaryExpression` assertions and opaque-leaf + EF regressions; no legacy `Condition<T>` API added.
- [x] No `Expression.Invoke` or compiled delegate inserted into EF-facing trees.
- [x] Old `Expr<T>` Boolean factory and sentinel special-case removed.
- [x] Examples, unit/integration tests, changelog, migration/architecture/README/skills updated.
- [x] Entire CI suite green (including SQL Server LocalDB).
- [x] PR description matches implemented functionality; this planning document updated to COMPLETED.

**Important:** Do not tackle the speculative lock-free cache redesign, global expression optimizer registration, or builder-level concurrent mutation in this task. They are separate design decisions.
