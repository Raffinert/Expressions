# PredicateBuilder<T> implementation plan (PR #17, v2)

**Status:** PLANNED — this document is an implementation checklist, not a statement that the API exists.
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
    .Build(balance: true);

Expr<Order, bool> alwaysTrue = PredicateBuilder<Order>.True().Build();
Expr<Order, bool> alwaysFalse = PredicateBuilder<Order>.False().Build();

var direct = Expr<Order>.Create(o => o.IsActive)
    .And(o => o.Total > 1000m); // This existing fluent API stays.
```

**In scope:** builder factory and fluent API, defined empty-state semantics, opt-in associative balancing, interoperability with native/external/variant `IExpr`, tests and migration docs, retiring `Expr<T>.Start/True/False` and the old `BooleanStartExpression<T>` special case.

**Out of scope:** changing `Expr<TSource,TResult>`, introducing structural global caches or new concurrency machinery, automatically balancing existing `IExpr.And/Or` calls, broad expression optimizers (EF-specific or otherwise), or changes to `MapToExisting`.

## 2. Public API — implement exactly this minimal surface first

Create `src/Raffinert.Expressions/Predicates/PredicateBuilder.cs` in namespace `Raffinert.Expressions`:

```csharp
public sealed class PredicateBuilder<T>
{
    public static PredicateBuilder<T> Start(bool defaultWhenEmpty = false);
    public static PredicateBuilder<T> True();
    public static PredicateBuilder<T> False();

    public PredicateBuilder<T> And(IExpr<T, bool> predicate);
    public PredicateBuilder<T> And(Expression<Func<T, bool>> predicate);
    public PredicateBuilder<T> Or(IExpr<T, bool> predicate);
    public PredicateBuilder<T> Or(Expression<Func<T, bool>> predicate);
    public PredicateBuilder<T> Not();

    public Expr<T, bool> Build(bool balance = false);
}
```

Include `using System.Linq.Expressions;` and XML docs. Public `And/Or/Not` mutate the **builder**, return `this`, and are *not* thread-safe. The resulting `Expr<T,bool>` is an immutable snapshot and uses its existing caching/thread-safety contract. Each static factory returns a **new builder**, never a shared mutable singleton.

No implicit conversion to `Expr`, `Build` on `IExpr`, or generic builder base classes in this PR. A `From(...)` factory and richer `BuildOptions` can be revisited only if a concrete consumer requires them.

## 3. State and Boolean semantics — write tests before implementation

A builder has (a) a current composition tree and (b) an empty fallback value. `Start()` means **empty**, not a `false` operand. `True()` and `False()` mean **non-empty constant operands**.

| Sequence | Expected expression / semantics |
|---|---|
| `Start().Build()` | Constant `false` |
| `Start(true).Build()` | Constant `true` |
| `Start().And(A).Build()` | `A`, no Boolean seed |
| `Start().Or(A).Build()` | `A`, no Boolean seed |
| `Start(true).Or(A).Build()` | `A`, despite empty fallback `true` |
| `True().Build()` | Constant `true` |
| `False().Build()` | Constant `false` |
| `True().And(A).Build()` | `true && A` (ordinary `AndAlso`) |
| `False().Or(A).Build()` | `false || A` (ordinary `OrElse`) |
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

## 5. Optional balancing — included in this PR, opt-in only

`Build(balance: false)` faithfully emits the left-associative AST.

`Build(balance: true)` balances **only contiguous groups of builder-owned identical associative operators** (`AndAlso` with `AndAlso`; `OrElse` with `OrElse`). Flatten such a group into an ordered list of its operands and rebuild as a near-balanced binary tree, with no reordering, distribution, simplification, or duplication.

For example:

```text
Unbalanced: (((A && B) && C) && D)
Balanced:   ((A && B) && (C && D))

Mixed:       (((A && B) || C) && D)
Allowed:     rebalance the (A && B) subgroup only;
             never transform into ((A || C) && (B || C)) or reorder terms.
```

Treat mixed `And/Or`, `Not`, and **opaque leaf** boundaries as barriers. Do not balance inside externally supplied expressions, nested lambda bodies or nested `Invoke` results. `Expression.AndAlso/OrElse` retain left-to-right short-circuit behavior for Boolean operands; test this with side-effect counting in in-memory delegates (while keeping production predicates pure).

For a homogeneous chain with N leaves, aim for output depth O(log N) with balancing versus O(N) without; no dependency on expression-tree string formatting. A balanced tree may improve expansion/visitor depth, **not necessarily EF SQL runtime**. Keep default `balance: false` until measured benefits justify a different default.

## 6. Integration and compatibility

- Existing immutable `IExpr<T,bool>.And/Or/Not` extension methods remain public and unchanged in behavior.
- `PredicateBuilder<T>` accepts native `Expr`, external `IExpr`, and contravariant `IExpr<Base,bool>` viewed as `IExpr<Derived,bool>`. The emitted tree must type-check and be consumable by `Enumerable`, `Queryable` and the EF Core adapter.
- Keep the single result type: `Build()` returns `Expr<T,bool>`, which can be passed to `Where`, `AnyAsync`, `Then`, nested `Invoke`, etc.
- Remove the Boolean-specific `Start/True/False` members from `src/Raffinert.Expressions/Core/ExprFactory.cs` and delete the internal `BooleanStartExpression<T>`. Remove its special branches in `BooleanExpressionExtensions.And/Or`; after migration, every `Expr<T,bool>` is an ordinary Boolean operand. `Expr<T>.Create` remains a general expression factory.
- Migrating `Expr<T>.True` to `PredicateBuilder<T>.True().Build()` produces a new wrapper per call unless the caller stores it. **Do not promise singleton identity**; document the behavior change.
- No source compatibility shims: this PR is already a breaking v2.0 API. Do not touch the historical design documents except their explicit current-API notices.

## 7. Implementation sequence (small commits on the same PR branch)

### PB-01 — Contract and tests (red stage)

Add `tests/Raffinert.Expressions.UnitTests/PredicateBuilderTests.cs` with tests for all state-table rows, null validation, `Not`, returned builder identity, snapshot independence, empty builds, native and external `IExpr`, and 1,024-operand builds. The new tests may fail until PB-02; commit only when CI-ready if the branch policy requires green commits.

### PB-02 — Builder, AST and lowering

Create `src/Raffinert.Expressions/Predicates/PredicateBuilder.cs`; private AST may be factored into `PredicateBuilderNodes.cs` if size warrants it. Implement O(1) appends and unbalanced `Build()`, parameter rebinding, safe contravariance and no eager `Invoke` expansion. Keep ordinary `Expr` caches unchanged. Pass PB-01 tests before moving on.

### PB-03 — Opt-in balancing

Add the ordered associative-group flatten/rebuild algorithm behind `Build(balance: true)`. Tests: N = 1, 2, 3, 127, 1,024, 4,096; equivalent in-memory results, identical exception/side-effect order, short-circuit skip counts, mixed operator groups and `Not` boundaries, structural depth reduction. Avoid recursion-based stack overflows for long chains.

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

- [ ] Public `PredicateBuilder<T>` API implemented and documented.
- [ ] `Start/True/False/Not` edge semantics covered.
- [ ] `Build()` produces an immutable, independent `Expr<T,bool>` snapshot.
- [ ] Native, external and contravariant `IExpr` accepted without unnecessary eager expansion.
- [ ] Balanced and unbalanced output semantically identical, with left-to-right short-circuit order.
- [ ] No `Expression.Invoke` or compiled delegate inserted into EF-facing trees.
- [ ] Old `Expr<T>` Boolean factory and sentinel special-case removed.
- [ ] Examples, unit/integration tests, changelog, migration/architecture/README/skills updated.
- [ ] Entire CI suite green (including SQL Server LocalDB).
- [ ] PR description matches implemented functionality; this planning document updated to COMPLETED.

**Important:** Do not tackle the speculative lock-free cache redesign, global expression optimizer registration, or builder-level concurrent mutation in this task. They are separate design decisions.
