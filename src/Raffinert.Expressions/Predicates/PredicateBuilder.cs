using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Builds reusable Boolean expressions with optional associative balancing.</summary>
/// <typeparam name="T">The source value type.</typeparam>
/// <remarks>
/// This mutable builder is not thread-safe. Each call to <see cref="Build"/> creates
/// an independent immutable expression that can be reused by LINQ and EF Core.
/// </remarks>
public sealed class PredicateBuilder<T>
{
    private readonly bool _defaultWhenEmpty;
    private Node? _root;

    private PredicateBuilder(bool defaultWhenEmpty, Node? root = null)
    {
        _defaultWhenEmpty = defaultWhenEmpty;
        _root = root;
    }

    /// <summary>Starts an empty chain. The first AND or OR supplies its initial operand.</summary>
    /// <param name="defaultWhenEmpty">The result of an unchanged empty builder.</param>
    public static PredicateBuilder<T> Start(bool defaultWhenEmpty = false) => new(defaultWhenEmpty);

    /// <summary>Starts a chain whose first operand is the Boolean constant true.</summary>
    public static PredicateBuilder<T> True() => new(false, new ConstantNode(true));

    /// <summary>Starts a chain whose first operand is the Boolean constant false.</summary>
    public static PredicateBuilder<T> False() => new(false, new ConstantNode(false));

    /// <summary>Appends a short-circuiting AND operand and returns this builder.</summary>
    public PredicateBuilder<T> And(IExpr<T, bool> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return Append(new ExpressionNode(predicate), ExpressionType.AndAlso);
    }

    /// <summary>Appends a short-circuiting AND lambda and returns this builder.</summary>
    public PredicateBuilder<T> And(Expression<Func<T, bool>> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return Append(new LambdaNode(predicate), ExpressionType.AndAlso);
    }

    /// <summary>Appends a short-circuiting OR operand and returns this builder.</summary>
    public PredicateBuilder<T> Or(IExpr<T, bool> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return Append(new ExpressionNode(predicate), ExpressionType.OrElse);
    }

    /// <summary>Appends a short-circuiting OR lambda and returns this builder.</summary>
    public PredicateBuilder<T> Or(Expression<Func<T, bool>> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return Append(new LambdaNode(predicate), ExpressionType.OrElse);
    }

    /// <summary>Negates the entire current expression, materializing the empty fallback if needed.</summary>
    public PredicateBuilder<T> Not()
    {
        _root = new NotNode(_root ?? new ConstantNode(_defaultWhenEmpty));
        return this;
    }

    /// <summary>Creates an independent expression, balancing builder-owned associative groups by default.</summary>
    /// <param name="balanced">Whether to rebalance homogeneous short-circuiting AND/OR groups.</param>
    public Expr<T, bool> Build(bool balanced = true)
    {
        var root = _root ?? new ConstantNode(_defaultWhenEmpty);
        if (balanced)
            root = Balance(root);

        var parameter = Expression.Parameter(typeof(T), "value");
        var body = Lower(root, parameter);
        return Expr<T, bool>.Create(Expression.Lambda<Func<T, bool>>(body, parameter));
    }

    private PredicateBuilder<T> Append(Node operand, ExpressionType kind)
    {
        _root = _root is null ? operand : new BinaryNode(kind, _root, operand);
        return this;
    }

    // Port of the group collection/bottom-up approach in PR #15, applied only to
    // private builder nodes. Leaf expressions are deliberately opaque.
    private static Node Balance(Node root)
    {
        var results = new Dictionary<Node, Node>();
        var groups = new Dictionary<Node, List<Node>>();
        var pending = new Stack<(Node Node, bool Complete)>();
        pending.Push((root, false));

        while (pending.Count > 0)
        {
            var (node, complete) = pending.Pop();
            if (complete)
            {
                if (node is BinaryNode binary)
                {
                    var operands = groups[node];
                    var rewritten = new Node[operands.Count];
                    for (var i = 0; i < operands.Count; i++)
                        rewritten[i] = results[operands[i]];

                    results[node] = BuildBalanced(rewritten, 0, rewritten.Length, binary.Kind);
                }
                else if (node is NotNode negation)
                {
                    results[node] = new NotNode(results[negation.Operand]);
                }
                continue;
            }

            if (node is BinaryNode group)
            {
                var operands = Collect(group);
                groups.Add(node, operands);
                pending.Push((node, true));
                for (var i = operands.Count - 1; i >= 0; i--)
                    pending.Push((operands[i], false));
            }
            else if (node is NotNode not)
            {
                pending.Push((node, true));
                pending.Push((negation.Operand, false));
            }
            else
            {
                results[node] = node;
            }
        }

        return results[root];
    }

    private static List<Node> Collect(BinaryNode root)
    {
        var operands = new List<Node>();
        var pending = new Stack<Node>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is BinaryNode binary && binary.Kind == root.Kind)
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

    private static Node BuildBalanced(IReadOnlyList<Node> nodes, int start, int count, ExpressionType kind)
    {
        if (count == 1)
            return nodes[start];

        var leftCount = count / 2;
        return new BinaryNode(
            kind,
            BuildBalanced(nodes, start, leftCount, kind),
            BuildBalanced(nodes, start + leftCount, count - leftCount, kind));
    }

    // Iterative postorder lowering avoids recursion over deep alternating groups.
    private static Expression Lower(Node root, ParameterExpression parameter)
    {
        var results = new Dictionary<Node, Expression>();
        var pending = new Stack<(Node Node, bool Complete)>();
        pending.Push((root, false));

        while (pending.Count > 0)
        {
            var (node, complete) = pending.Pop();
            if (complete)
            {
                if (node is BinaryNode binary)
                {
                    var left = results[binary.Left];
                    var right = results[binary.Right];
                    results[node] = binary.Kind == ExpressionType.AndAlso
                        ? Expression.AndAlso(left, right)
                        : Expression.OrElse(left, right);
                }
                else if (node is NotNode not)
                {
                    results[node] = Expression.Not(results[negation.Operand]);
                }
                continue;
            }

            if (node is BinaryNode pair)
            {
                pending.Push((node, true));
                pending.Push((pair.Right, false));
                pending.Push((pair.Left, false));
            }
            else if (node is NotNode negation)
            {
                pending.Push((node, true));
                pending.Push((negation.Operand, false));
            }
            else if (node is ConstantNode constant)
            {
                results[node] = Expression.Constant(constant.Value);
            }
            else if (node is LambdaNode lambda)
            {
                results[node] = Rebind(lambda.Expression, parameter);
            }
            else if (node is ExpressionNode expression)
            {
                var wrapper = expression.Expression;
                var definition = wrapper is Expr<T, bool> native
                    ? native.GetExpression()
                    : ExprAdapter.GetExpandedExpression(wrapper);
                results[node] = Rebind(definition, parameter);
            }
        }

        return results[root];
    }

    private static Expression Rebind(Expression<Func<T, bool>> expression, ParameterExpression parameter) =>
        new ReplaceExpressionVisitor(expression.Parameters[0], parameter).Visit(expression.Body)!;

    private abstract class Node
    {
    }

    private sealed class LambdaNode(Expression<Func<T, bool>> expression) : Node
    {
        public Expression<Func<T, bool>> Expression { get; } = expression;
    }

    private sealed class ExpressionNode(IExpr<T, bool> expression) : Node
    {
        public IExpr<T, bool> Expression { get; } = expression;
    }

    private sealed class ConstantNode(bool value) : Node
    {
        public bool Value { get; } = value;
    }

    private sealed class NotNode(Node operand) : Node
    {
        public Node Operand { get; } = operand;
    }

    private sealed class BinaryNode(ExpressionType kind, Node left, Node right) : Node
    {
        public ExpressionType Kind { get; } = kind;
        public Node Left { get; } = left;
        public Node Right { get; } = right;
    }
}
