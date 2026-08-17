using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal sealed class ReplaceExpressionVisitor(Expression from, Expression to) : ExpressionVisitor
{
    private readonly Expression _from = from ?? throw new ArgumentNullException(nameof(from));
    private readonly Expression _to = to ?? throw new ArgumentNullException(nameof(to));

    public override Expression? Visit(Expression? node)
    {
        return node == _from 
            ? _to 
            : node == null 
                ? null 
                : base.Visit(node);
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        if (_from is ParameterExpression parameter && node.Parameters.Contains(parameter))
        {
            return node;
        }

        return base.VisitLambda(node);
    }
}
