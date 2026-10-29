using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Creates typed expressions while inferring the result type from a lambda.</summary>
public static class ComposableExpression<TSource>
{
    public static ComposableExpression<TSource, TResult> Create<TResult>(
        Expression<Func<TSource, TResult>> expression) =>
        ComposableExpression<TSource, TResult>.Create(expression);

    /// <summary>An expression that always evaluates to true.</summary>
    public static ComposableExpression<TSource, bool> True { get; } =
        Create(static value => true);

    /// <summary>An expression that always evaluates to false.</summary>
    public static ComposableExpression<TSource, bool> False { get; } =
        Create(static value => false);

    /// <summary>Starts a Boolean composition chain without adding a seed to the first operand.</summary>
    public static ComposableExpression<TSource, bool> Start(bool defaultWhenEmpty = false) =>
        defaultWhenEmpty ? StartTrue : StartFalse;

    private static ComposableExpression<TSource, bool> StartTrue { get; } = new BooleanStartExpression<TSource>(true);
    private static ComposableExpression<TSource, bool> StartFalse { get; } = new BooleanStartExpression<TSource>(false);
}

internal sealed class BooleanStartExpression<T>(bool defaultWhenEmpty) : ComposableExpression<T, bool>
{
    public override Expression<Func<T, bool>> GetExpression() =>
        defaultWhenEmpty ? ComposableExpression<T>.True.GetExpression() : ComposableExpression<T>.False.GetExpression();
}
