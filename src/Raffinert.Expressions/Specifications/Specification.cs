using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable predicate over values of a specified type.</summary>
/// <typeparam name="T">The type of value tested by the predicate.</typeparam>
public abstract class Specification<T> : ComposableExpression<T, bool>
{
    /// <inheritdoc />
    public abstract override Expression<Func<T, bool>> GetExpression();

    /// <summary>Creates a specification from a predicate expression.</summary>
    /// <param name="expression">The predicate expression represented by the specification.</param>
    /// <returns>A specification that represents <paramref name="expression"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public static Specification<T> Create(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : new InlineSpecification(expression);
    }

    /// <summary>Gets a specification that accepts every value.</summary>
    public static Specification<T> True { get; } = Create(static value => true);

    /// <summary>Gets a specification that rejects every value.</summary>
    public static Specification<T> False { get; } = Create(static value => false);

    /// <summary>Combines this specification and another specification using short-circuiting logical AND.</summary>
    /// <param name="specification">The specification to evaluate after this specification.</param>
    /// <returns>A specification that accepts a value when both specifications accept it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="specification"/> is null.</exception>
    public Specification<T> And(Specification<T> specification)
    {
        return specification == null
            ? throw new ArgumentNullException(nameof(specification))
            : Create(BooleanExpressionComposer.And(GetExpression(), specification.GetExpression()));
    }

    /// <summary>Combines this specification and a predicate expression using short-circuiting logical AND.</summary>
    /// <param name="expression">The predicate expression to evaluate after this specification.</param>
    /// <returns>A specification that accepts a value when both predicates accept it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public Specification<T> And(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.And(GetExpression(), expression));
    }

    /// <summary>Combines this specification and another specification using short-circuiting logical OR.</summary>
    /// <param name="specification">The specification to evaluate when this specification rejects a value.</param>
    /// <returns>A specification that accepts a value when either specification accepts it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="specification"/> is null.</exception>
    public Specification<T> Or(Specification<T> specification)
    {
        return specification == null
            ? throw new ArgumentNullException(nameof(specification))
            : Create(BooleanExpressionComposer.Or(GetExpression(), specification.GetExpression()));
    }

    /// <summary>Combines this specification and a predicate expression using short-circuiting logical OR.</summary>
    /// <param name="expression">The predicate expression to evaluate when this specification rejects a value.</param>
    /// <returns>A specification that accepts a value when either predicate accepts it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
    public Specification<T> Or(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.Or(GetExpression(), expression));
    }

    /// <summary>Negates this specification.</summary>
    /// <returns>A specification that accepts exactly the values rejected by this specification.</returns>
    public Specification<T> Not()
    {
        var expression = GetExpression();
        return Create(Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters));
    }

    /// <summary>Adapts this specification to a source type with compatible public members.</summary>
    /// <typeparam name="TNewSource">The source type to which the specification is adapted.</typeparam>
    /// <returns>A specification over <typeparamref name="TNewSource"/> with structurally rebound member access.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required source member is missing, ambiguous, unreadable, or has an incompatible type.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The expression uses its source parameter in a way that cannot be structurally adapted.
    /// </exception>
    public Specification<TNewSource> AdaptSource<TNewSource>()
    {
        var expression = StructuralExpressionAdapter.AdaptSource<T, bool, TNewSource>(GetExpandedExpression());
        return Specification<TNewSource>.Create(expression);
    }

    /// <summary>Combines two specifications using short-circuiting logical AND.</summary>
    /// <param name="left">The first specification to evaluate.</param>
    /// <param name="right">The specification to evaluate when <paramref name="left"/> accepts a value.</param>
    /// <returns>A specification that accepts a value when both specifications accept it.</returns>
    public static Specification<T> operator &(Specification<T> left, Specification<T> right) => left.And(right);

    /// <summary>Combines two specifications using short-circuiting logical OR.</summary>
    /// <param name="left">The first specification to evaluate.</param>
    /// <param name="right">The specification to evaluate when <paramref name="left"/> rejects a value.</param>
    /// <returns>A specification that accepts a value when either specification accepts it.</returns>
    public static Specification<T> operator |(Specification<T> left, Specification<T> right) => left.Or(right);

    /// <summary>Negates a specification.</summary>
    /// <param name="specification">The specification to negate.</param>
    /// <returns>A specification that accepts exactly the values rejected by <paramref name="specification"/>.</returns>
    public static Specification<T> operator !(Specification<T> specification) => specification.Not();

    /// <summary>Supports <c>||</c> composition without evaluating a specification as a Boolean value.</summary>
    /// <param name="specification">The specification used as the left operand.</param>
    /// <returns>Always <see langword="false"/> so both operands are combined into a specification.</returns>
    public static bool operator true(Specification<T> specification) => false;

    /// <summary>Supports <c>&amp;&amp;</c> composition without evaluating a specification as a Boolean value.</summary>
    /// <param name="specification">The specification used as the left operand.</param>
    /// <returns>Always <see langword="false"/> so both operands are combined into a specification.</returns>
    public static bool operator false(Specification<T> specification) => false;

    /// <summary>Converts a Boolean-valued projection to a specification.</summary>
    /// <param name="projection">The projection to convert.</param>
    /// <returns>A specification represented by the projection's expression.</returns>
    public static implicit operator Specification<T>(Projection<T, bool> projection) => Create(projection.GetExpression());

    private sealed class InlineSpecification(Expression<Func<T, bool>> expression) : Specification<T>
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
