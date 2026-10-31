using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Creates typed expressions while inferring the result type from a lambda.</summary>
public static class Expr<TSource>
{
    /// <summary>Creates an expression and infers the result type from its lambda.</summary>
    public static Expr<TSource, TResult> Create<TResult>(
        Expression<Func<TSource, TResult>> expression) =>
        Expr<TSource, TResult>.Create(expression);
}
