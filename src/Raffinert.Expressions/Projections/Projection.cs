using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable expression that transforms a source value into a result value.</summary>
/// <typeparam name="TSource">The type of value supplied to the projection.</typeparam>
/// <typeparam name="TResult">The type of value produced by the projection.</typeparam>
public abstract class Projection<TSource, TResult> : ComposableExpression<TSource, TResult>
{
    private readonly object _projectionCacheLock = new();
    private Expression<Action<TSource, TResult>>? _mapToExistingExpression;
    private Action<TSource, TResult>? _mapToExistingAction;

    /// <inheritdoc />
    public abstract override Expression<Func<TSource, TResult>> GetExpression();

    /// <summary>Creates a projection from a transformation expression.</summary>
    /// <param name="expression">The transformation expression represented by the projection.</param>
    /// <returns>A projection that represents <paramref name="expression"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public static Projection<TSource, TResult> Create(Expression<Func<TSource, TResult>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : new InlineProjection(expression);
    }

    /// <summary>Adapts this projection to a source type with compatible public members.</summary>
    /// <typeparam name="TNewSource">The source type to which the projection is adapted.</typeparam>
    /// <returns>A projection with structurally rebound source-member access.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The expression uses its source parameter in a way that cannot be structurally adapted.
    /// </exception>
    public Projection<TNewSource, TResult> AdaptSource<TNewSource>()
    {
        var expression = StructuralExpressionAdapter.AdaptSource<TSource, TResult, TNewSource>(
            GetExpandedExpression());
        return Projection<TNewSource, TResult>.Create(expression);
    }

    /// <summary>Adapts this projection to a result type with compatible public members.</summary>
    /// <typeparam name="TNewResult">The result type to which the projection is adapted.</typeparam>
    /// <returns>A projection that constructs <typeparamref name="TNewResult"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required result member is missing, ambiguous, unwritable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The projection does not use a supported parameterless member-initializer shape.
    /// </exception>
    public Projection<TSource, TNewResult> AdaptResult<TNewResult>()
    {
        var expression = StructuralExpressionAdapter.AdaptResult<TSource, TResult, TNewResult>(
            GetExpandedExpression());
        return Projection<TSource, TNewResult>.Create(expression);
    }

    /// <summary>Adapts this projection to compatible source and result types.</summary>
    /// <typeparam name="TNewSource">The source type to which the projection is adapted.</typeparam>
    /// <typeparam name="TNewResult">The result type to which the projection is adapted.</typeparam>
    /// <returns>A projection with structurally rebound source access and result construction.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source or result member is missing, ambiguous, inaccessible, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The source expression or result construction uses an unsupported shape.
    /// </exception>
    public Projection<TNewSource, TNewResult> Adapt<TNewSource, TNewResult>()
    {
        var expression = StructuralExpressionAdapter.Adapt<TSource, TResult, TNewSource, TNewResult>(
            GetExpandedExpression());
        return Projection<TNewSource, TNewResult>.Create(expression);
    }

    /// <summary>Composes this projection with a projection applied to its result.</summary>
    /// <typeparam name="TNext">The result type of the composed projection.</typeparam>
    /// <param name="next">The projection to apply to the result of this projection.</param>
    /// <returns>A projection that applies this projection followed by <paramref name="next"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is null.</exception>
    public Projection<TSource, TNext> Then<TNext>(Projection<TResult, TNext> next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));

        var firstExpression = GetExpandedExpression();
        var nextExpression = next.GetExpandedExpression();
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Projection<TSource, TNext>.Create(
            Expression.Lambda<Func<TSource, TNext>>(body, firstExpression.Parameters));
    }

    /// <summary>Composes this projection with a condition applied to its result.</summary>
    /// <param name="next">The condition to apply to the result of this projection.</param>
    /// <returns>A condition over the source type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is null.</exception>
    public Condition<TSource> Then(Condition<TResult> next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));

        var firstExpression = GetExpandedExpression();
        var nextExpression = next.GetExpandedExpression();
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Condition<TSource>.Create(
            Expression.Lambda<Func<TSource, bool>>(body, firstExpression.Parameters));
    }

    /// <summary>Combines the member bindings from this projection and another projection.</summary>
    /// <param name="other">The projection whose member bindings are added to this projection.</param>
    /// <returns>A projection containing bindings from both projections; bindings from <paramref name="other"/> take precedence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="NotSupportedException">Either projection does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The projections cannot be combined into a compatible result.</exception>
    public Projection<TSource, TResult> MergeBindings(Projection<TSource, TResult> other) =>
        MergeBindings(other, BindingConflictBehavior.UseLast);

    /// <summary>Combines member bindings using the specified behavior for duplicate members.</summary>
    /// <param name="other">The projection whose member bindings are added to this projection.</param>
    /// <param name="conflictBehavior">The behavior to use when both projections bind the same member.</param>
    /// <returns>A projection containing the selected bindings from both projections.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conflictBehavior"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">Either projection does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The projections cannot be combined using the selected behavior.</exception>
    public Projection<TSource, TResult> MergeBindings(
        Projection<TSource, TResult> other,
        BindingConflictBehavior conflictBehavior)
    {
        return other == null
            ? throw new ArgumentNullException(nameof(other))
            : ProjectionBindingMerger.Merge(this, other, conflictBehavior);
    }

    /// <summary>Combines this projection's member bindings with those from another expression.</summary>
    /// <param name="other">The expression whose member bindings are added to this projection.</param>
    /// <returns>A projection containing bindings from both expressions; bindings from <paramref name="other"/> take precedence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="NotSupportedException">Either expression does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The expressions cannot be combined into a compatible result.</exception>
    public Projection<TSource, TResult> MergeBindings(Expression<Func<TSource, TResult>> other) =>
        MergeBindings(Create(other), BindingConflictBehavior.UseLast);

    /// <summary>Combines member bindings from another expression using the specified behavior for duplicate members.</summary>
    /// <param name="other">The expression whose member bindings are added to this projection.</param>
    /// <param name="conflictBehavior">The behavior to use when both expressions bind the same member.</param>
    /// <returns>A projection containing the selected bindings from both expressions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conflictBehavior"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">Either expression does not use a supported member-initializer shape.</exception>
    /// <exception cref="InvalidOperationException">The expressions cannot be combined using the selected behavior.</exception>
    public Projection<TSource, TResult> MergeBindings(
        Expression<Func<TSource, TResult>> other,
        BindingConflictBehavior conflictBehavior) =>
        MergeBindings(Create(other), conflictBehavior);

    /// <summary>Returns an expression that applies this projection's member assignments to an existing result.</summary>
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

    /// <summary>Returns a compiled action that applies this projection's member assignments to an existing result.</summary>
    /// <returns>An action that accepts a source value and the result instance to update.</returns>
    /// <remarks>The action is compiled once and cached for the lifetime of this projection.</remarks>
    /// <exception cref="NotSupportedException">The projection does not use a supported member-initializer shape.</exception>
    public Action<TSource, TResult> GetMapToExistingAction()
    {
        if (_mapToExistingAction != null) return _mapToExistingAction;

        lock (_projectionCacheLock)
        {
            return _mapToExistingAction ??= GetMapToExistingExpression().Compile();
        }
    }

    /// <summary>Projects a new result when it is null; otherwise updates it using this projection's member assignments.</summary>
    /// <param name="source">The source value to project.</param>
    /// <param name="destination">The result to create or update.</param>
    /// <exception cref="NotSupportedException">An existing result cannot be updated from this projection's shape.</exception>
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

    private sealed class InlineProjection(Expression<Func<TSource, TResult>> expression) : Projection<TSource, TResult>
    {
        public override Expression<Func<TSource, TResult>> GetExpression() => expression;
    }
}
