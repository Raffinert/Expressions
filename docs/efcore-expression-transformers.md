# Custom EF Core query transformations

The EF Core adapter can apply ordered user-defined transformations to an entire query **after Raffinert invocation expansion and before EF Core's native parameter extraction**:

```csharp
builder.UseRaffinertExpressions(options =>
{
    options.Transform(expression => MyTransformer(expression));
    options.Transform(expression => AnotherTransformer(expression));
});
```

Callbacks receive the complete query expression (typically a `Queryable` method-call tree), not an individual Boolean predicate. Each receives the previous callback's output; returning null throws. The no-argument overload remains unchanged. Repeated configuration calls append transformations.

Example with the optional third-party `Linq.Expression.Optimizer` package (not a dependency of Raffinert):

```csharp
builder.UseRaffinertExpressions(options =>
    options.Transform(ExpressionOptimizer.visit));
```

Transformations run only on ordinary synchronous/asynchronous `IQueryCompiler.Execute` calls. Compiled/precompiled query behavior is unchanged; the separate closed-wrapper compilation interceptor remains an expansion fallback and does not rerun custom transformations. The callback must preserve query type/translation semantics, be thread-safe and avoid scoped captures. In particular, the sample optimizer documents side-effect-free expressions as a precondition: enable it only for suitable queries. Native EF parameter extraction/cache behavior remains EF's responsibility, but an optimizer that eagerly evaluates captured state can change that behavior.
