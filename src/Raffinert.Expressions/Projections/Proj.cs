using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable projection from <typeparamref name="TIn"/> to <typeparamref name="TOut"/>.</summary>
public abstract class Proj<TIn, TOut> : Expr<TIn, TOut>
{
    private readonly object _projectionCacheLock = new();
    private Expression<Action<TIn, TOut>>? _mapToExistingExpression;
    private Action<TIn, TOut>? _mapToExistingAction;

    /// <inheritdoc />
    public abstract override Expression<Func<TIn, TOut>> GetExpression();

    /// <summary>Creates a projection from an expression.</summary>
    public static Proj<TIn, TOut> Create(Expression<Func<TIn, TOut>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : new InlineProj(expression);
    }

    /// <summary>Composes this projection with a following projection.</summary>
    public Proj<TIn, TNext> Then<TNext>(Proj<TOut, TNext> next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));

        var firstExpression = GetExpandedExpression();
        var nextExpression = next.GetExpandedExpression();
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Proj<TIn, TNext>.Create(
            Expression.Lambda<Func<TIn, TNext>>(body, firstExpression.Parameters));
    }

    /// <summary>Composes this projection with a specification over its output.</summary>
    public Spec<TIn> Then(Spec<TOut> next)
    {
        if (next == null) throw new ArgumentNullException(nameof(next));

        var firstExpression = GetExpandedExpression();
        var nextExpression = next.GetExpandedExpression();
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Spec<TIn>.Create(
            Expression.Lambda<Func<TIn, bool>>(body, firstExpression.Parameters));
    }

    /// <summary>Merges member bindings, using the second projection when both bind the same member.</summary>
    public Proj<TIn, TOut> MergeBindings(Proj<TIn, TOut> other) =>
        MergeBindings(other, BindingConflictBehavior.UseLast);

    /// <summary>Merges member bindings using the selected conflict behavior.</summary>
    public Proj<TIn, TOut> MergeBindings(
        Proj<TIn, TOut> other,
        BindingConflictBehavior conflictBehavior)
    {
        return other == null
            ? throw new ArgumentNullException(nameof(other))
            : ProjectionBindingMerger.Merge(this, other, conflictBehavior);
    }

    /// <summary>Merges member bindings, using the second expression when both bind the same member.</summary>
    public Proj<TIn, TOut> MergeBindings(Expression<Func<TIn, TOut>> other) =>
        MergeBindings(Create(other), BindingConflictBehavior.UseLast);

    /// <summary>Merges member bindings using the selected conflict behavior.</summary>
    public Proj<TIn, TOut> MergeBindings(
        Expression<Func<TIn, TOut>> other,
        BindingConflictBehavior conflictBehavior) =>
        MergeBindings(Create(other), conflictBehavior);

    /// <summary>Gets an expression that updates an existing destination from the projection's member initializers.</summary>
    public Expression<Action<TIn, TOut>> GetMapToExistingExpression()
    {
        if (_mapToExistingExpression != null) return _mapToExistingExpression;

        lock (_projectionCacheLock)
        {
            return _mapToExistingExpression ??= MapToExistingBuilder.Build(GetExpandedExpression());
        }
    }

    /// <summary>Gets a cached compiled action that updates an existing destination.</summary>
    public Action<TIn, TOut> GetMapToExistingAction()
    {
        if (_mapToExistingAction != null) return _mapToExistingAction;

        lock (_projectionCacheLock)
        {
            return _mapToExistingAction ??= GetMapToExistingExpression().Compile();
        }
    }

    /// <summary>Maps to a new destination when null, or updates the supplied destination when present.</summary>
    public void MapToExisting(TIn source, ref TOut? destination)
    {
        if (destination is null)
        {
            destination = Invoke(source);
            return;
        }

        GetMapToExistingAction()(source, destination);
    }

    private sealed class InlineProj(Expression<Func<TIn, TOut>> expression) : Proj<TIn, TOut>
    {
        public override Expression<Func<TIn, TOut>> GetExpression() => expression;
    }
}
