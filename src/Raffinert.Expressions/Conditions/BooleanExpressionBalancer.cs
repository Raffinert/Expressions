using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Reassociates only the outermost homogeneous built-in Boolean chain.</summary>
internal static class BooleanExpressionBalancer
{
    public static Expression Balance(Expression expression)
    {
        if (expression is not BinaryExpression root || !CanReassociate(root, root.NodeType))
            return expression;

        var operands = new List<Expression>();
        var pending = new Stack<Expression>();
        pending.Push(root);

        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current is BinaryExpression binary && CanReassociate(binary, root.NodeType))
            {
                // LIFO: visit the left operand first, preserving short-circuit order.
                pending.Push(binary.Right);
                pending.Push(binary.Left);
            }
            else
            {
                operands.Add(current);
            }
        }

        if (operands.Count <= 2)
            return expression;

        return Build(operands, 0, operands.Count, root.NodeType);
    }

    private static bool CanReassociate(BinaryExpression binary, ExpressionType kind) =>
        (kind == ExpressionType.AndAlso || kind == ExpressionType.OrElse) &&
        binary.NodeType == kind &&
        binary.Method == null &&
        !binary.IsLifted &&
        binary.Type == typeof(bool) &&
        binary.Left.Type == typeof(bool) &&
        binary.Right.Type == typeof(bool);

    private static Expression Build(
        IReadOnlyList<Expression> operands, int start, int count, ExpressionType kind)
    {
        if (count == 1)
            return operands[start];

        var leftCount = count / 2;
        var left = Build(operands, start, leftCount, kind);
        var right = Build(operands, start + leftCount, count - leftCount, kind);
        return kind == ExpressionType.AndAlso
            ? Expression.AndAlso(left, right)
            : Expression.OrElse(left, right);
    }
}
