using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Forward composition for interface-typed reusable expressions.</summary>
public static class ExprCompositionExtensions
{
    /// <summary>Applies the second expression to the result of the first, inlining both bodies.</summary>
    public static Expr<TSource, TNext> Then<TSource, TResult, TNext>(
        this IExpr<TSource, TResult> first, IExpr<TResult, TNext> next)
    {
        if (first == null) throw new ArgumentNullException(nameof(first));
        if (next == null) throw new ArgumentNullException(nameof(next));
        var firstExpression = ExprAdapter.GetExpandedExpression(first);
        var nextExpression = ExprAdapter.GetExpandedExpression(next);
        var body = new ReplaceExpressionVisitor(nextExpression.Parameters[0], firstExpression.Body)
            .Visit(nextExpression.Body)!;
        return Expr<TSource, TNext>.Create(
            Expression.Lambda<Func<TSource, TNext>>(body, firstExpression.Parameters));
    }
}
