using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal sealed class ExpressionDebugView(IExpressionExpansionSource source)
{
    public LambdaExpression OriginalExpression => source.GetExpression();

    public LambdaExpression ExpandedExpression => source.GetExpandedLambdaExpression();

    public string RuntimeRepresentation => ExpandedExpression.ToString();
}
