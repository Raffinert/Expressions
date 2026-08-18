using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable condition over values of a specified type.</summary>
/// <typeparam name="T">The type of value tested by the condition.</typeparam>
public abstract class Condition<T> : ComposableExpression<T, bool>
{
    /// <inheritdoc />
    public abstract override Expression<Func<T, bool>> GetExpression();

    /// <summary>Creates a condition from a Boolean expression.</summary>
    /// <param name="expression">The Boolean expression represented by the condition.</param>
    /// <returns>A condition that represents <paramref name="expression"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public static Condition<T> Create(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : new InlineCondition(expression);
    }

    /// <summary>Gets a condition that accepts every value.</summary>
    public static Condition<T> True { get; } = Create(static value => true);

    /// <summary>Gets a condition that rejects every value.</summary>
    public static Condition<T> False { get; } = Create(static value => false);

    /// <summary>Combines this condition and another condition using short-circuiting logical AND.</summary>
    /// <param name="condition">The condition to evaluate after this condition.</param>
    /// <returns>A condition that accepts a value when both conditions accept it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
    public Condition<T> And(Condition<T> condition)
    {
        return condition == null
            ? throw new ArgumentNullException(nameof(condition))
            : Create(BooleanExpressionComposer.And(GetExpression(), condition.GetExpression()));
    }

    /// <summary>Combines this condition and a Boolean expression using short-circuiting logical AND.</summary>
    /// <param name="expression">The Boolean expression to evaluate after this condition.</param>
    /// <returns>A condition that accepts a value when both conditions accept it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public Condition<T> And(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.And(GetExpression(), expression));
    }

    /// <summary>Combines this condition and another condition using short-circuiting logical OR.</summary>
    /// <param name="condition">The condition to evaluate when this condition rejects a value.</param>
    /// <returns>A condition that accepts a value when either condition accepts it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
    public Condition<T> Or(Condition<T> condition)
    {
        return condition == null
            ? throw new ArgumentNullException(nameof(condition))
            : Create(BooleanExpressionComposer.Or(GetExpression(), condition.GetExpression()));
    }

    /// <summary>Combines this condition and a Boolean expression using short-circuiting logical OR.</summary>
    /// <param name="expression">The Boolean expression to evaluate when this condition rejects a value.</param>
    /// <returns>A condition that accepts a value when either condition accepts it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public Condition<T> Or(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.Or(GetExpression(), expression));
    }

    /// <summary>Negates this condition.</summary>
    /// <returns>A condition that accepts exactly the values rejected by this condition.</returns>
    public Condition<T> Not()
    {
        var expression = GetExpression();
        return Create(Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters));
    }

    /// <summary>Adapts this condition to a source type with compatible public members.</summary>
    /// <typeparam name="TNewSource">The source type to which the condition is adapted.</typeparam>
    /// <returns>A condition over <typeparamref name="TNewSource"/> with structurally rebound member access.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The expression uses its source parameter in a way that cannot be structurally adapted.
    /// </exception>
    public Condition<TNewSource> AdaptSource<TNewSource>()
    {
        var expression = StructuralExpressionAdapter.AdaptSource<T, bool, TNewSource>(GetExpandedExpression());
        return Condition<TNewSource>.Create(expression);
    }

    /// <summary>Combines two conditions using short-circuiting logical AND.</summary>
    /// <param name="left">The first condition to evaluate.</param>
    /// <param name="right">The condition to evaluate when <paramref name="left"/> accepts a value.</param>
    /// <returns>A condition that accepts a value when both conditions accept it.</returns>
    public static Condition<T> operator &(Condition<T> left, Condition<T> right) => left.And(right);

    /// <summary>Combines two conditions using short-circuiting logical OR.</summary>
    /// <param name="left">The first condition to evaluate.</param>
    /// <param name="right">The condition to evaluate when <paramref name="left"/> rejects a value.</param>
    /// <returns>A condition that accepts a value when either condition accepts it.</returns>
    public static Condition<T> operator |(Condition<T> left, Condition<T> right) => left.Or(right);

    /// <summary>Negates a condition.</summary>
    /// <param name="condition">The condition to negate.</param>
    /// <returns>A condition that accepts exactly the values rejected by <paramref name="condition"/>.</returns>
    public static Condition<T> operator !(Condition<T> condition) => condition.Not();

    /// <summary>Supports <c>||</c> composition without evaluating a condition as a Boolean value.</summary>
    /// <param name="condition">The condition used as the left operand.</param>
    /// <returns>Always <see langword="false"/> so both operands are combined into a condition.</returns>
    public static bool operator true(Condition<T> condition) => false;

    /// <summary>Supports <c>&amp;&amp;</c> composition without evaluating a condition as a Boolean value.</summary>
    /// <param name="condition">The condition used as the left operand.</param>
    /// <returns>Always <see langword="false"/> so both operands are combined into a condition.</returns>
    public static bool operator false(Condition<T> condition) => false;

    /// <summary>Converts a Boolean-valued projection to a condition.</summary>
    /// <param name="projection">The projection to convert.</param>
    /// <returns>A condition represented by the projection's expression.</returns>
    public static implicit operator Condition<T>(Projection<T, bool> projection) => Create(projection.GetExpression());

    private sealed class InlineCondition(Expression<Func<T, bool>> expression) : Condition<T>
    {
        public override Expression<Func<T, bool>> GetExpression() => expression;
    }
}

internal static class BooleanExpressionComposer
{
    public static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var rightBody = new ReplaceExpressionVisitor(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rightBody), left.Parameters);
    }

    public static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var rightBody = new ReplaceExpressionVisitor(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rightBody), left.Parameters);
    }
}
