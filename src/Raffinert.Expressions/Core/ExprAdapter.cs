using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal static class ExprAdapter
{
    public static Expression<Func<TSource, TResult>> GetExpandedExpression<TSource, TResult>(
        IExpr<TSource, TResult> expression)
    {
        var expanded = expression.GetExpandedLambdaExpression();
        if (expanded is null ||
            expanded.Parameters.Count != 1 ||
            expanded.Parameters[0].Type != typeof(TSource) ||
            !typeof(TResult).IsAssignableFrom(expanded.ReturnType))
        {
            throw new InvalidOperationException(
                $"IExpr<{typeof(TSource).Name}, {typeof(TResult).Name}> must return a compatible " +
                "single-parameter lambda from GetExpandedLambdaExpression().");
        }

        if (expanded is Expression<Func<TSource, TResult>> typedExpression)
            return typedExpression;

        return Expression.Lambda<Func<TSource, TResult>>(
            expanded.Body,
            expanded.Parameters);
    }
}
