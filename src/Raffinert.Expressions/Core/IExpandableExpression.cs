using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal interface IExpandableExpression
{
    LambdaExpression GetExpressionUntyped();

    LambdaExpression? GetCachedExpandedExpressionUntyped();

    LambdaExpression CacheExpandedExpressionUntyped(LambdaExpression expression);
}
