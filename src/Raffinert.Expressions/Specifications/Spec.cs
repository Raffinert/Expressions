using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable boolean expression for values of type <typeparamref name="T"/>.</summary>
public abstract class Spec<T> : Expr<T, bool>
{
    /// <inheritdoc />
    public abstract override Expression<Func<T, bool>> GetExpression();

    /// <summary>Creates a specification from an expression.</summary>
    public static Spec<T> Create(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : new InlineSpec(expression);
    }

    /// <summary>Gets a specification that is always true.</summary>
    public static Spec<T> True { get; } =
        Create(static value => true);

    /// <summary>Gets a specification that is always false.</summary>
    public static Spec<T> False { get; } =
        Create(static value => false);

    /// <summary>Combines this specification and another specification with conditional AND.</summary>
    public Spec<T> And(Spec<T> spec)
    {
        return spec == null
            ? throw new ArgumentNullException(nameof(spec))
            : Create(BooleanExpressionComposer.And(GetExpression(), spec.GetExpression()));
    }

    /// <summary>Combines this specification and an expression with conditional AND.</summary>
    public Spec<T> And(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.And(GetExpression(), expression));
    }

    /// <summary>Combines this specification and another specification with conditional OR.</summary>
    public Spec<T> Or(Spec<T> spec)
    {
        return spec == null
            ? throw new ArgumentNullException(nameof(spec))
            : Create(BooleanExpressionComposer.Or(GetExpression(), spec.GetExpression()));
    }

    /// <summary>Combines this specification and an expression with conditional OR.</summary>
    public Spec<T> Or(Expression<Func<T, bool>> expression)
    {
        return expression == null
            ? throw new ArgumentNullException(nameof(expression))
            : Create(BooleanExpressionComposer.Or(GetExpression(), expression));
    }

    /// <summary>Negates this specification.</summary>
    public Spec<T> Not()
    {
        var expression = GetExpression();
        return Create(Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters));
    }

    /// <summary>Combines two specifications with conditional AND.</summary>
    public static Spec<T> operator &(Spec<T> left, Spec<T> right) => left.And(right);

    /// <summary>Combines two specifications with conditional OR.</summary>
    public static Spec<T> operator |(Spec<T> left, Spec<T> right) => left.Or(right);

    /// <summary>Negates a specification.</summary>
    public static Spec<T> operator !(Spec<T> spec) => spec.Not();

    /// <summary>Enables the C# conditional specification operators without converting a specification to a Boolean value.</summary>
    public static bool operator true(Spec<T> spec) => false;

    /// <summary>Enables the C# conditional specification operators without converting a specification to a Boolean value.</summary>
    public static bool operator false(Spec<T> spec) => false;

    private sealed class InlineSpec(Expression<Func<T, bool>> expression) : Spec<T>
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
