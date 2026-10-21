using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Balances every reachable built-in Boolean AND/OR group without crossing operator boundaries.</summary>
internal static class BooleanExpressionBalancer
{
    public static Expression Balance(Expression expression)
    {
        // Process group boundaries bottom-up, iteratively: neither a long homogeneous
        // chain nor deeply alternating AND/OR groups should overflow the traversal stack.
        var results = new Dictionary<Expression, Expression>();
        var groups = new Dictionary<Expression, List<Expression>>();
        var pending = new Stack<(Expression Node, bool Complete)>();
        pending.Push((expression, false));

        while (pending.Count != 0)
        {
            var (node, complete) = pending.Pop();
            if (complete)
            {
                if (groups.TryGetValue(node, out var operands))
                {
                    var rewritten = new Expression[operands.Count];
                    for (var i = 0; i < operands.Count; i++)
                        rewritten[i] = results[operands[i]];
                    // A two-operand group has no balancing work to do.
                    results[node] = operands.Count <= 2
                        ? ((BinaryExpression)node).Update(rewritten[0], null, rewritten[1])
                        : Build(rewritten, 0, rewritten.Length, node.NodeType);
                }
                else if (node is UnaryExpression unary)
                {
                    results[node] = unary.Update(results[unary.Operand]);
                }
                continue;
            }

            if (node is BinaryExpression root && CanReassociate(root, root.NodeType))
            {
                var operands = Collect(root);
                groups.Add(node, operands);
                pending.Push((node, true));
                for (var i = operands.Count - 1; i >= 0; i--)
                    pending.Push((operands[i], false));
            }
            else if (node is UnaryExpression unary && unary.NodeType == ExpressionType.Not &&
                     unary.Method == null && unary.Type == typeof(bool) &&
                     unary.Operand.Type == typeof(bool))
            {
                pending.Push((node, true));
                pending.Push((unary.Operand, false));
            }
            else
            {
                // Unknown operators are opaque; do not silently rewrite their semantics.
                results[node] = node;
            }
        }

        return results[expression];
    }

    private static List<Expression> Collect(BinaryExpression root)
    {
        var operands = new List<Expression>();
        var pending = new Stack<Expression>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (node is BinaryExpression binary && CanReassociate(binary, root.NodeType))
            {
                pending.Push(binary.Right);
                pending.Push(binary.Left);
            }
            else
            {
                operands.Add(node);
            }
        }
        return operands;
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
