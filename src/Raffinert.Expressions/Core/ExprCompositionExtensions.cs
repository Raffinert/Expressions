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
        // A covariant IExpr view may expose a narrower body type than TResult.
        // Preserve the next expression's declared parameter type when substituting
        // so operators such as reference equality retain their original semantics.
        var nextParameter = nextExpression.Parameters[0];
        var result = firstExpression.Body.Type == nextParameter.Type
            ? firstExpression.Body
            : Expression.Convert(firstExpression.Body, nextParameter.Type);
        var body = new ReplaceExpressionVisitor(nextParameter, result)
            .Visit(nextExpression.Body)!;
        return Expr<TSource, TNext>.Create(
            Expression.Lambda<Func<TSource, TNext>>(body, firstExpression.Parameters));
    }
}
