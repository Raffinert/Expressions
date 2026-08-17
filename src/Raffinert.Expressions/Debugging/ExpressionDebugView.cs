using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal sealed class ExpressionDebugView(IExpandableExpression expression)
{
    public LambdaExpression OriginalExpression => expression.GetExpressionUntyped();

    public LambdaExpression ExpandedExpression
    {
        get
        {
            var method = expression.GetType().GetMethod(nameof(Expr<,>.GetExpandedExpression));
            return (LambdaExpression)method!.Invoke(expression, null)!;
        }
    }

    public string RuntimeRepresentation => ExpandedExpression.ToString();
}
