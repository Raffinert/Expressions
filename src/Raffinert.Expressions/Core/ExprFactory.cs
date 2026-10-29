using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Creates typed expressions while inferring the result type from a lambda.</summary>
public static class Expr<TSource>
{
    /// <summary>Creates an expression and infers the result type from its lambda.</summary>
    public static Expr<TSource, TResult> Create<TResult>(
        Expression<Func<TSource, TResult>> expression) =>
        Expr<TSource, TResult>.Create(expression);

    /// <summary>An expression that always evaluates to true.</summary>
    public static Expr<TSource, bool> True { get; } =
        Create(static value => true);

    /// <summary>An expression that always evaluates to false.</summary>
    public static Expr<TSource, bool> False { get; } =
        Create(static value => false);

    /// <summary>Starts a Boolean composition chain without adding a seed to the first operand.</summary>
    public static Expr<TSource, bool> Start(bool defaultWhenEmpty = false) =>
        defaultWhenEmpty ? StartTrue : StartFalse;

    private static Expr<TSource, bool> StartTrue { get; } = new BooleanStartExpression<TSource>(true);
    private static Expr<TSource, bool> StartFalse { get; } = new BooleanStartExpression<TSource>(false);
}

internal sealed class BooleanStartExpression<T>(bool defaultWhenEmpty) : Expr<T, bool>
{
    public override Expression<Func<T, bool>> GetExpression() =>
        defaultWhenEmpty ? Expr<T>.True.GetExpression() : Expr<T>.False.GetExpression();
}
