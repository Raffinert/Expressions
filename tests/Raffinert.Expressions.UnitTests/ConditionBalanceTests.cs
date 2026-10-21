using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class ConditionBalanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BalancesLongHomogeneousChains(bool useOr)
    {
        var condition = Condition<int>.Create(value => value > 0);
        for (var i = 1; i < 1024; i++)
        {
            Expression<Func<int, bool>> next = value => value > 0;
            condition = useOr ? condition.Or(next) : condition.And(next);
        }

        var original = condition.GetExpression();
        var balanced = condition.Balance();
        var result = balanced.GetExpression();

        Assert.Same(original, condition.GetExpression());
        Assert.Equal(original.Parameters[0], result.Parameters[0]);
        Assert.True(Depth(result.Body) <= 12);
        Assert.Equal(condition.Invoke(1), balanced.Invoke(1));
        Assert.Equal(condition.Invoke(-1), balanced.Invoke(-1));
        Assert.Equal(result.ToString(), balanced.Balance().GetExpression().ToString());
    }

    [Fact]
    public void PreservesMixedGroupsAsOpaqueOperands()
    {
        var a = Condition<int>.Create(x => x > 0);
        var b = Condition<int>.Create(x => x < 100);
        var c = Condition<int>.Create(x => x == 42);
        var d = Condition<int>.Create(x => x != 7);
        var e = Condition<int>.Create(x => x % 2 == 0);
        var mixed = a.And(b).Or(c).Or(d).Or(e);

        var balanced = mixed.Balance();
        var root = Assert.IsType<BinaryExpression>(balanced.GetExpression().Body);
        Assert.Equal(ExpressionType.OrElse, root.NodeType);

        foreach (var value in new[] { -1, 0, 7, 42, 101 })
            Assert.Equal(mixed.Invoke(value), balanced.Invoke(value));

        // The inner AND group is preserved rather than flattened into OR.
        var leftmost = root;
        while (leftmost.Left is BinaryExpression child && child.NodeType == ExpressionType.OrElse)
            leftmost = child;
        Assert.Equal(ExpressionType.AndAlso, leftmost.Left.NodeType);
    }

    [Fact]
    public void BalancesNestedGroupsWithoutCrossingAndOrOrNotBoundaries()
    {
        var condition = Condition<int>.Create(x => x > 0);
        for (var i = 0; i < 256; i++)
            condition = condition.And(x => x < 1000);

        var nested = Condition<int>.Create(x => x == -1);
        for (var i = 0; i < 256; i++)
            nested = nested.Or(x => x == 42);

        var combined = condition.Or(nested.Not()).And(x => x != 7).And(x => x != 8);
        var balanced = combined.Balance();
        var root = Assert.IsType<BinaryExpression>(balanced.GetExpression().Body);

        Assert.True(Depth(root) < 24);
        foreach (var value in new[] { -1, 0, 7, 8, 42, 1001 })
            Assert.Equal(combined.Invoke(value), balanced.Invoke(value));
        Assert.Equal(balanced.GetExpression().ToString(),
            balanced.Balance().GetExpression().ToString());
    }

    [Fact]
    public void HandlesDeepAlternatingGroupsWithoutRecursiveTraversal()
    {
        var condition = Condition<int>.Create(x => x > 0);
        for (var i = 0; i < 2048; i++)
            condition = (i & 1) == 0
                ? condition.And(x => x < 100)
                : condition.Or(x => x == 42);

        var balanced = condition.Balance();
        // Alternating operators cannot be reassociated across their group boundaries.
        Assert.Same(condition, balanced);
    }

    [Fact]
    public void KeepsShortCircuitOperandOrder()
    {
        var calls = new List<int>();
        var a = Condition<int>.Create(x => Record(calls, 1, true));
        var b = Condition<int>.Create(x => Record(calls, 2, false));
        var c = Condition<int>.Create(x => Record(calls, 3, true));
        var d = Condition<int>.Create(x => Record(calls, 4, true));

        Assert.False(a.And(b).And(c).And(d).Balance().Invoke(0));
        Assert.Equal(new[] { 1, 2 }, calls);
        calls.Clear();

        Assert.True(a.Or(b).Or(c).Or(d).Balance().Invoke(0));
        Assert.Equal(new[] { 1 }, calls);
    }

    [Fact]
    public void LeavesSingleExpressionAndUnchangedTwoOperandChainAlone()
    {
        var a = Condition<int>.Create(x => x > 0);
        Assert.Same(a, a.Balance());
        var pair = a.And(x => x < 10);
        Assert.Same(pair, pair.Balance());
    }

    private static bool Record(List<int> calls, int number, bool result)
    {
        calls.Add(number);
        return result;
    }

    private static int Depth(Expression expression)
    {
        var max = 0;
        var pending = new Stack<(Expression Node, int Depth)>();
        pending.Push((expression, 1));
        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            if (depth > max) max = depth;
            if (node is BinaryExpression binary &&
                (binary.NodeType == ExpressionType.AndAlso || binary.NodeType == ExpressionType.OrElse))
            {
                pending.Push((binary.Left, depth + 1));
                pending.Push((binary.Right, depth + 1));
            }
        }
        return max;
    }
}
