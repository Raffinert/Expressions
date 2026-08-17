using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal interface IExpressionExpansionSource
{
    LambdaExpression GetExpression();

    LambdaExpression? GetCachedExpandedExpression();

    LambdaExpression CacheExpandedExpression(LambdaExpression expression);
}
