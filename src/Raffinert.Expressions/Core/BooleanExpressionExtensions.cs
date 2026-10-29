using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Boolean composition for any Boolean-valued composable expression.</summary>
public static class BooleanExpressionExtensions
{
    /// <summary>Combines two Boolean expressions using short-circuiting AND.</summary>
    public static ComposableExpression<T, bool> And<T>(
        this ComposableExpression<T, bool> left, ComposableExpression<T, bool> right)
    {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        return left is BooleanStartExpression<T>
            ? right
            : ComposableExpression<T, bool>.Create(BooleanExpressionComposer.And(left.GetExpression(), right.GetExpression()));
    }

    /// <summary>Combines a Boolean expression and a lambda using short-circuiting AND.</summary>
    public static ComposableExpression<T, bool> And<T>(
        this ComposableExpression<T, bool> left, Expression<Func<T, bool>> right)
    {
        if (right == null) throw new ArgumentNullException(nameof(right));
        return left.And(ComposableExpression<T, bool>.Create(right));
    }

    /// <summary>Combines two Boolean expressions using short-circuiting OR.</summary>
    public static ComposableExpression<T, bool> Or<T>(
        this ComposableExpression<T, bool> left, ComposableExpression<T, bool> right)
    {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));
        return left is BooleanStartExpression<T>
            ? right
            : ComposableExpression<T, bool>.Create(BooleanExpressionComposer.Or(left.GetExpression(), right.GetExpression()));
    }

    /// <summary>Combines a Boolean expression and a lambda using short-circuiting OR.</summary>
    public static ComposableExpression<T, bool> Or<T>(
        this ComposableExpression<T, bool> left, Expression<Func<T, bool>> right)
    {
        if (right == null) throw new ArgumentNullException(nameof(right));
        return left.Or(ComposableExpression<T, bool>.Create(right));
    }

    /// <summary>Negates a Boolean-valued composable expression.</summary>
    public static ComposableExpression<T, bool> Not<T>(this ComposableExpression<T, bool> expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        var lambda = expression.GetExpression();
        return ComposableExpression<T, bool>.Create(
            Expression.Lambda<Func<T, bool>>(Expression.Not(lambda.Body), lambda.Parameters));
    }
}

internal static class BooleanExpressionComposer
{
    public static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var body = new ReplaceExpressionVisitor(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, body), left.Parameters);
    }

    public static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var body = new ReplaceExpressionVisitor(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), left.Parameters);
    }
}
