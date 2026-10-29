using System.Diagnostics;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable expression that can be composed with other expressions and evaluated.</summary>
/// <typeparam name="TSource">The type of value supplied to the expression.</typeparam>
/// <typeparam name="TResult">The type of value produced by the expression.</typeparam>
/// <remarks>
/// After expansion or evaluation is first requested, implementations must continue to return an expression
/// with equivalent behavior for the lifetime of the instance.
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[DebuggerTypeProxy(typeof(ExpressionDebugView))]
public abstract class Expr<TSource, TResult> :
    IExpr<TSource, TResult>,
    IExpressionExpansionSource
{
    private readonly object _cacheLock = new();
    private Expression<Func<TSource, TResult>>? _expandedExpression;
    private Func<TSource, TResult>? _compiledExpression;
    private readonly object _projectionCacheLock = new();
    private Expression<Action<TSource, TResult>>? _mapToExistingExpression;
    private Action<TSource, TResult>? _mapToExistingAction;

    /// <summary>Wraps a typed lambda in a reusable composable expression.</summary>
    public static Expr<TSource, TResult> Create(Expression<Func<TSource, TResult>> expression) =>
        expression == null ? throw new ArgumentNullException(nameof(expression)) : new InlineExpression(expression);

    private sealed class InlineExpression(Expression<Func<TSource, TResult>> expression)
        : Expr<TSource, TResult>
    {
        public override Expression<Func<TSource, TResult>> GetExpression() => expression;
    }

    /// <summary>Returns the expression represented by this instance.</summary>
    /// <returns>The expression from a source value to a result value.</returns>
    public abstract Expression<Func<TSource, TResult>> GetExpression();

    /// <summary>Returns the expression with all nested composition calls inlined.</summary>
    /// <returns>An expanded expression containing no calls to this library's composition methods.</returns>
    /// <remarks>The expanded expression is created once and cached for the lifetime of this instance.</remarks>
    public Expression<Func<TSource, TResult>> GetExpandedExpression()
    {
        var cached = _expandedExpression;
        if (cached is not null)
            return cached;

        lock (_cacheLock)
        {
            cached = _expandedExpression;

            if (cached is not null)
                return cached;

            return _expandedExpression = ExpressionExpander.Expand(this, GetExpression());
        }
    }

    /// <summary>Evaluates the expression for the specified value.</summary>
    /// <param name="value">The source value to evaluate.</param>
    /// <returns>The result of evaluating the expression.</returns>
    /// <remarks>
    /// The expanded expression is compiled on first use and then cached. When this call appears in another
    /// composable expression tree, <see cref="GetExpandedExpression"/> replaces it with the referenced expression body.
    /// </remarks>
    public TResult Invoke(TSource value)
    {
        var compiled = _compiledExpression;

        if (compiled is not null)
            return compiled(value);

        lock (_cacheLock)
        {
            compiled = _compiledExpression;

            if (compiled is not null)
                return compiled(value);

            _compiledExpression = compiled = GetExpandedExpression().Compile();
        }

        return compiled(value);
    }

    /// <summary>Returns the default result for a null input; otherwise evaluates the expression.</summary>
    /// <param name="value">The source value to evaluate, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="default"/>(<typeparamref name="TResult"/>) when <paramref name="value"/> is null;
    /// otherwise, the result of evaluating the expression.
    /// </returns>
    /// <remarks>
    /// When this call appears in another composable expression tree, expansion replaces it with a conditional expression.
    /// The default result is not necessarily null; for example, it is zero for numeric value types and false for Boolean values.
    /// </remarks>
    public TResult? InvokeOrDefault(TSource? value) => value is null ? default : Invoke(value);

    LambdaExpression IExpressionExpansionSource.GetExpression() => GetExpression();

    LambdaExpression IExpr<TSource, TResult>.GetExpandedLambdaExpression() =>
        GetExpandedExpression();

    LambdaExpression? IExpressionExpansionSource.GetCachedExpandedExpression() => _expandedExpression;

    LambdaExpression IExpressionExpansionSource.CacheExpandedExpression(LambdaExpression expression)
    {
        var typed = (Expression<Func<TSource, TResult>>)expression;
        Interlocked.CompareExchange(ref _expandedExpression, typed, null);
        return _expandedExpression!;
    }

    /// <summary>Adapts this expression to a source type with compatible public members.</summary>
    /// <typeparam name="TNewSource">The source type to which the projection is adapted.</typeparam>
    /// <returns>A projection with structurally rebound source-member access.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The expression uses its source parameter in a way that cannot be structurally adapted.
    /// </exception>
    public Expr<TNewSource, TResult> AdaptSource<TNewSource>()
    {
        var expression = StructuralExpressionAdapter.AdaptSource<TSource, TResult, TNewSource>(
            GetExpandedExpression());
        return Expr<TNewSource, TResult>.Create(expression);
    }

    /// <summary>Adapts this expression to a result type with compatible public members.</summary>
    /// <typeparam name="TNewResult">The result type to which the projection is adapted.</typeparam>
    /// <returns>A projection that constructs <typeparamref name="TNewResult"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required result member is missing, ambiguous, unwritable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The projection does not use a supported parameterless member-initializer shape.
    /// </exception>
    public Expr<TSource, TNewResult> AdaptResult<TNewResult>()
    {
        var expression = StructuralExpressionAdapter.AdaptResult<TSource, TResult, TNewResult>(
            GetExpandedExpression());
        return Expr<TSource, TNewResult>.Create(expression);
    }

    /// <summary>Adapts this expression to compatible source and result types.</summary>
    /// <typeparam name="TNewSource">The source type to which the projection is adapted.</typeparam>
    /// <typeparam name="TNewResult">The result type to which the projection is adapted.</typeparam>
    /// <returns>A projection with structurally rebound source access and result construction.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source or result member is missing, ambiguous, inaccessible, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The source expression or result construction uses an unsupported shape.
    /// </exception>
    public Expr<TNewSource, TNewResult> Adapt<TNewSource, TNewResult>()
    {
        var expression = StructuralExpressionAdapter.Adapt<TSource, TResult, TNewSource, TNewResult>(
            GetExpandedExpression());
        return Expr<TNewSource, TNewResult>.Create(expression);
    }

    /// <summary>Composes this expression with an expression applied to its result.</summary>
    /// <typeparam name="TNext">The result type of the composed projection.</typeparam>
    /// <param name="next">The projection to apply to the result of this expression.</param>
    /// <returns>A projection that applies this expression followed by <paramref name="next"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is null.</exception>
    public Expr<TSource, TNext> Then<TNext>(Expr<TResult, TNext> next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));

        var firstExpression = GetExpandedExpression();
        var nextExpression = next.GetExpandedExpression();
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Expr<TSource, TNext>.Create(
            Expression.Lambda<Func<TSource, TNext>>(body, firstExpression.Parameters));
    }

    /// <summary>Combines the member bindings from this expression and another projection.</summary>
    /// <param name="other">The projection whose member bindings are added to this expression.</param>
    /// <returns>A projection containing bindings from both projections; bindings from <paramref name="other"/> take precedence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="NotSupportedException">Either projection does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The projections cannot be combined into a compatible result.</exception>
    public Expr<TSource, TResult> MergeBindings(Expr<TSource, TResult> other) =>
        MergeBindings(other, BindingConflictBehavior.UseLast);

    /// <summary>Combines member bindings using the specified behavior for duplicate members.</summary>
    /// <param name="other">The projection whose member bindings are added to this expression.</param>
    /// <param name="conflictBehavior">The behavior to use when both projections bind the same member.</param>
    /// <returns>A projection containing the selected bindings from both projections.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conflictBehavior"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">Either projection does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The projections cannot be combined using the selected behavior.</exception>
    public Expr<TSource, TResult> MergeBindings(
        Expr<TSource, TResult> other,
        BindingConflictBehavior conflictBehavior)
    {
        return other == null
            ? throw new ArgumentNullException(nameof(other))
            : ProjectionBindingMerger.Merge(this, other, conflictBehavior);
    }

    /// <summary>Combines this expression's member bindings with those from another expression.</summary>
    /// <param name="other">The expression whose member bindings are added to this expression.</param>
    /// <returns>A projection containing bindings from both expressions; bindings from <paramref name="other"/> take precedence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="NotSupportedException">Either expression does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The expressions cannot be combined into a compatible result.</exception>
    public Expr<TSource, TResult> MergeBindings(Expression<Func<TSource, TResult>> other) =>
        MergeBindings(Create(other), BindingConflictBehavior.UseLast);

    /// <summary>Combines member bindings from another expression using the specified behavior for duplicate members.</summary>
    /// <param name="other">The expression whose member bindings are added to this expression.</param>
    /// <param name="conflictBehavior">The behavior to use when both expressions bind the same member.</param>
    /// <returns>A projection containing the selected bindings from both expressions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conflictBehavior"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">Either expression does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The expressions cannot be combined using the selected behavior.</exception>
    public Expr<TSource, TResult> MergeBindings(
        Expression<Func<TSource, TResult>> other,
        BindingConflictBehavior conflictBehavior) =>
        MergeBindings(Create(other), conflictBehavior);

    /// <summary>Returns an expression that applies this expression's member assignments to an existing result.</summary>
    /// <returns>An expression that accepts a source value and the result instance to update.</returns>
    /// <exception cref="NotSupportedException">The projection does not use a supported member-initializer shape.</exception>
    public Expression<Action<TSource, TResult>> GetMapToExistingExpression()
    {
        if (_mapToExistingExpression != null) return _mapToExistingExpression;

        lock (_projectionCacheLock)
        {
            return _mapToExistingExpression ??= MapToExistingBuilder.Build(GetExpandedExpression());
        }
    }

    /// <summary>Returns a compiled action that applies this expression's member assignments to an existing result.</summary>
    /// <returns>An action that accepts a source value and the result instance to update.</returns>
    /// <remarks>The action is compiled once and cached for the lifetime of this expression.</remarks>
    /// <exception cref="NotSupportedException">The projection does not use a supported member-initializer shape.</exception>
    public Action<TSource, TResult> GetMapToExistingAction()
    {
        if (_mapToExistingAction != null) return _mapToExistingAction;

        lock (_projectionCacheLock)
        {
            return _mapToExistingAction ??= GetMapToExistingExpression().Compile();
        }
    }

    /// <summary>Projects a new result when it is null; otherwise updates it using this expression's member assignments.</summary>
    /// <param name="source">The source value to project.</param>
    /// <param name="destination">The result to create or update.</param>
    /// <exception cref="NotSupportedException">An existing result cannot be updated from this expression's shape.</exception>
    /// <exception cref="InvalidOperationException">A nested read-only result member that must be created is null.</exception>
    /// <remarks>Existing mutable collections are cleared and refilled; their elements are not matched by key.</remarks>
    public void MapToExisting(TSource source, ref TResult? destination)
    {
        if (destination is null)
        {
            destination = Invoke(source);
            return;
        }

        GetMapToExistingAction()(source, destination);
    }


    private string DebuggerDisplay => GetExpandedExpression().ToString();
}
