using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal static class ExprAdapter
{
    public static Expression<Func<TSource, TResult>> GetExpandedExpression<TSource, TResult>(
        IExpr<TSource, TResult> expression)
    {
        var expanded = expression.GetExpandedLambdaExpression();

        if (expanded is Expression<Func<TSource, TResult>> typedExpression)
            return typedExpression;

        return Expression.Lambda<Func<TSource, TResult>>(
            expanded.Body,
            expanded.Parameters);
    }
}
