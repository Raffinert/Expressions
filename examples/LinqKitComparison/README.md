# LINQKit README examples: pure .NET, LINQKit, and Raffinert.Expressions

This .NET 10 console app runs equivalent EF Core 10 SQLite queries three ways:

1. **Pure .NET / EF Core** — ordinary lambdas with logic kept inline or duplicated. It intentionally contains no
   custom expression visitor, parameter rebinder, or home-grown expansion helper.
2. **LINQKit** — `AsExpandable`, `Invoke`, `Expand`, and `PredicateBuilder`, following the patterns in the
   [LINQKit README](https://github.com/scottksmith95/LINQKit/blob/master/README.md).
3. **Raffinert.Expressions** — `Condition<T>`, `Projection<TSource, TResult>`, direct composable LINQ overloads,
   and `AsRaffinertQuery()` for query syntax.

The runner executes every query against the same in-memory database and fails if the three result sets differ.

## Run

```shell
dotnet run --project examples/LinqKitComparison
```

Add `--sql` to print the SQL generated for all three implementations:

```shell
dotnet run --project examples/LinqKitComparison -- --sql
```

## Scenario map

| LINQKit README scenario | Pure .NET / EF Core | LINQKit | Raffinert.Expressions |
|---|---|---|---|
| Predicate in a navigation collection | Inline the purchase predicate | `AsExpandable()` + `Compile()` | Nested `Condition.Invoke` method group, expanded before `Where` |
| Expression variable in a correlated subquery | Inline the predicate | `AsExpandable()` + an expression passed to subquery `Any` | `AsRaffinertQuery()` + `Condition.Invoke` |
| Combining expressions | Write the combined lambda inline | `Invoke()` + `Expand()` | A condition containing another condition's `Invoke()` |
| Dynamic all-keyword predicate | Chain ordinary `Where` calls | `PredicateBuilder.And` | Fold conditions with `Condition.And` |
| Dynamic any-keyword predicate | Spell out the OR terms (two in this example) | `PredicateBuilder.Or` | Fold conditions with `Condition.Or` |
| Nested predicates | Write the parenthesized lambda inline | Nested `PredicateBuilder` instances | Compose inner and outer `Condition` instances |
| Reusable predicate library | Duplicate the complete rule inline | Reusable expressions composed with `And`/`Or` | Reusable conditions composed with `And`/`Or` |
| Generic validity predicate | Duplicate the validity clauses in the provider lambda | Generic expression + `And` | Generic `Condition<TEntity>` + `And` |
| Reusable aggregate | Put `Average` directly in the group projection | Invoked aggregate expression + `AsExpandable()` | Invoked `Projection<IQueryable<Order>, double?>` + `AsRaffinertQuery()` |

The pure .NET implementations are deliberately the baseline, not an expression-composition library hidden inside
the example. They show that EF Core can solve every concrete query when the logic is placed directly in the
provider-facing lambda. LINQKit and Raffinert.Expressions become useful when that logic must remain independently reusable.

The LINQKit README's optional expression-optimizer section has no direct Raffinert.Expressions equivalent. Raffinert.Expressions performs
composition/expansion only; it does not attempt general constant folding or query optimization.

The original README writes the ad-hoc subquery with a `let` that temporarily projects an `IQueryable<Purchase>`.
EF Core 10 rejects that intermediate projection. This sample keeps the same correlated-subquery intent but writes
the `Where(...).Any(...)` operation directly in the outer predicate.

## Raffinert.Expressions capabilities beyond LINQKit's API

After the three-way comparisons, `RaffinertSpecificExamples` runs capabilities that Raffinert.Expressions exposes as supported
APIs and LINQKit does not expose directly:

| Raffinert.Expressions API | What the runnable example does | LINQKit comparison |
|---|---|---|
| `AdaptSource` / `Adapt` | Reuses a condition and projection with structurally compatible source and result types | No structural source/result adaptation API |
| `Projection.Then` | Type-safely chains projections and then a condition | Possible with `Invoke`/`Expand`, but no typed forward-composition abstraction |
| `MergeBindings` | Combines two member-initializer projections, with defined conflict behavior | No projection-binding merge API |
| `InvokeOrDefault` | Inlines a nested projection with built-in null-to-default behavior | Requires an explicit conditional around a LINQKit invocation |
| `MapToExisting` | Compiles a projection into an updater and preserves an existing destination instance | Outside LINQKit's expression-expansion scope |

“No LINQKit equivalent” here means LINQKit itself provides no corresponding operation. Since both libraries expose
ordinary expression trees, custom visitors or mapping code could reproduce most outcomes; that additional code is
not a LINQKit feature.
