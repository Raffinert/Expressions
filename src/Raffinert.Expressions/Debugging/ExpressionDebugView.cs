using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal sealed class ExpressionDebugView(IExpressionExpansionSource source)
{
    public LambdaExpression OriginalExpression => source.GetExpression();

    public LambdaExpression ExpandedExpression
    {
        get
        {
            var method = source.GetType().GetMethod(nameof(ComposableExpression<,>.GetExpandedExpression));
            return (LambdaExpression)method!.Invoke(source, null)!;
        }
    }

    public string RuntimeRepresentation => ExpandedExpression.ToString();
}
