using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal interface IExpressionExpansionSource : IExpr
{
    LambdaExpression GetExpression();

    LambdaExpression? GetCachedExpandedExpression();

    LambdaExpression CacheExpandedExpression(LambdaExpression expression);
}
