using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class PredicateBuilderTests
{
    [Fact]
    public void EmptyStartUsesItsFallbackAndDoesNotSeedItsFirstOperand()
    {
        Assert.False(PredicateBuilder<int>.Start().Build().Invoke(1));
        Assert.True(PredicateBuilder<int>.Start(true).Build().Invoke(1));

        var and = PredicateBuilder<int>.Start().And(x => x > 10).Build();
        var or = PredicateBuilder<int>.Start(true).Or(x => x > 10).Build();

        Assert.False(and.Invoke(10));
        Assert.True(and.Invoke(11));
        Assert.False(or.Invoke(10));
        Assert.True(or.Invoke(11));

        Assert.Equal(ExpressionType.GreaterThan, and.GetExpression().Body.NodeType);
    }

    [Fact]
    public void TrueAndFalseAreActualBooleanOperands()
    {
        var yes = PredicateBuilder<int>.True().And(x => x > 0).Build();
        var no = PredicateBuilder<int>.False().Or(x => x > 0).Build();

        Assert.Equal(ExpressionType.AndAlso, yes.GetExpression().Body.NodeType);
        Assert.Equal(ExpressionType.OrElse, no.GetExpression().Body.NodeType);
        Assert.True(yes.Invoke(1));
        Assert.False(yes.Invoke(-1));
        Assert.True(no.Invoke(1));
        Assert.False(no.Invoke(-1));
        Assert.True(PredicateBuilder<int>.True().Build().Invoke(0));
        Assert.False(PredicateBuilder<int>.False().Build().Invoke(0));
    }

    [Fact]
    public void EmptyNotMaterializesFallbackAndSubsequentAndDoesNotSkipIt()
    {
        var invertFalse = PredicateBuilder<int>.Start().Not().Build();
        var invertTrue = PredicateBuilder<int>.Start(true).Not().Build();

        Assert.True(invertFalse.Invoke(0));
        Assert.False(invertTrue.Invoke(0));

        var chain = PredicateBuilder<int>.Start().Not().And(x => x > 0).Build();
        Assert.Equal(ExpressionType.AndAlso, chain.GetExpression().Body.NodeType);
        Assert.True(chain.Invoke(1));
        Assert.False(chain.Invoke(-1));
    }

    [Fact]
    public void FluentMethodsMutateBuilderAndBuildCreatesIndependentSnapshots()
    {
        var builder = PredicateBuilder<int>.Start();
        Assert.Same(builder, builder.And(x => x > 0));
        var before = builder.Build();

        Assert.Same(builder, builder.Or(x => x == 0));
        var after = builder.Build();

        Assert.False(before.Invoke(0));
        Assert.True(after.Invoke(0));
        Assert.False(builder.Build().Invoke(-1));

        Assert.Same(builder, builder.Not());
        Assert.True(builder.Build().Invoke(-1));
        Assert.False(after.Invoke(-1));
        Assert.NotSame(builder.Build(), builder.Build());
    }

    [Fact]
    public void MixedOperatorsComposeInCallOrderAndNotWrapsWholeChain()
    {
        var a = PredicateBuilder<int>.Start().And(x => x > 0).Or(x => x == -1)
            .And(x => x != 2).Build();
        Assert.Equal(ExpressionType.AndAlso, a.GetExpression().Body.NodeType);
        Assert.True(a.Invoke(-1));
        Assert.False(a.Invoke(2));
        Assert.False(a.Invoke(0));

        var inverted = PredicateBuilder<int>.Start().Or(x => x == 7).Or(x => x == 8).Not().Build();
        Assert.False(inverted.Invoke(7));
        Assert.False(inverted.Invoke(8));
        Assert.True(inverted.Invoke(9));
        Assert.Equal(ExpressionType.Not, inverted.GetExpression().Body.NodeType);
    }

    [Fact]
    public void LongHomogeneousChainsAreBalancedByDefault()
    {
        foreach (var useOr in new[] { false, true })
        {
            var builder = PredicateBuilder<int>.Start();
            for (var i = 0; i < 4096; i++)
            {
                if (useOr) builder.Or(x => x == 7);
                else builder.And(x => x > 0);
            }

            var balanced = builder.Build();
            var explicitBalanced = builder.Build(balanced: true);
            var unbalanced = builder.Build(balanced: false);

            Assert.True(Depth(balanced.GetExpression().Body) <= 14);
            Assert.True(Depth(unbalanced.GetExpression().Body) >= 4090);
            Assert.Equal(balanced.GetExpression().ToString(), explicitBalanced.GetExpression().ToString());
            Assert.True(balanced.Invoke(7));
            Assert.Equal(useOr, unbalanced.GetExpression().Body.NodeType == ExpressionType.OrElse);
        }
    }

    [Fact]
    public void MixedAndNotGroupsAreBalancedWithoutCrossingBoundaries()
    {
        var builder = PredicateBuilder<int>.Start();
        for (var i = 0; i < 128; i++) builder.And(x => x > 0);
        builder.Not();
        for (var i = 0; i < 128; i++) builder.Or(x => x < 0);
        var balanced = builder.Build();
        var unbalanced = builder.Build(balanced: false);

        Assert.True(Depth(balanced.GetExpression().Body) < 25);
        foreach (var value in new[] { -1, 0, 1 })
            Assert.Equal(unbalanced.Invoke(value), balanced.Invoke(value));
    }

    [Fact]
    public void DeepAlternatingOperatorsCanBeBuiltWithoutRecursiveTraversal()
    {
        var builder = PredicateBuilder<int>.Start();
        for (var i = 0; i < 2048; i++)
        {
            if ((i & 1) == 0) builder.And(x => x >= 0);
            else builder.Or(x => x < 0);
        }

        Assert.NotNull(builder.Build().GetExpression());
        Assert.NotNull(builder.Build(balanced: false).GetExpression());
    }

    [Fact]
    public void BalancingPreservesLeftToRightShortCircuitOrder()
    {
        var calls = new List<int>();
        var conjunction = PredicateBuilder<int>.Start()
            .And(x => Record(calls, 1, true))
            .And(x => Record(calls, 2, false))
            .And(x => Record(calls, 3, true))
            .And(x => Record(calls, 4, true))
            .Build();

        Assert.False(conjunction.Invoke(0));
        Assert.Equal([1, 2], calls);
        calls.Clear();

        var disjunction = PredicateBuilder<int>.Start()
            .Or(x => Record(calls, 1, false))
            .Or(x => Record(calls, 2, true))
            .Or(x => Record(calls, 3, true))
            .Or(x => Record(calls, 4, true))
            .Build();

        Assert.True(disjunction.Invoke(0));
        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public void OpaqueNativeLeavesRemainUnbalancedInternally()
    {
        var leaf = Expr<int>.Create(x => x > 0 && x < 100 && x != 7 && x != 8);
        var predicate = PredicateBuilder<int>.Start()
            .And(leaf)
            .And(x => x != 3)
            .And(x => x != 4)
            .Build();

        var root = Assert.IsAssignableFrom<BinaryExpression>(predicate.GetExpression().Body);
        Assert.Equal(ExpressionType.AndAlso, root.Left.NodeType);
        Assert.Equal(4, Depth(root.Left));
    }

    [Fact]
    public void NativeNestedInvocationRemainsLazyUntilExpansion()
    {
        var nested = new CountingExpression();
        var wrapper = Expr<int>.Create(x => nested.Invoke(x));
        var builder = PredicateBuilder<int>.Start().And(wrapper).And(x => x > 1);

        var built = builder.Build();
        Assert.Equal(0, nested.Requests);
        Assert.True(built.Invoke(5));
        Assert.Equal(1, nested.Requests);
    }

    [Fact]
    public void ExternalAndContravariantOperandsAreAccepted()
    {
        IExpr<Animal, bool> native = Expr<Animal>.Create(x => x.IsActive);
        IExpr<Dog, bool> dogRule = native;
        IExpr<Dog, bool> external = new ExternalDogExpression();
        var builder = PredicateBuilder<Dog>.Start().And(dogRule).And(external);

        var predicate = builder.Build();
        Assert.True(predicate.Invoke(new Dog { IsActive = true, Name = "Ada" }));
        Assert.False(predicate.Invoke(new Dog { IsActive = false, Name = "Ada" }));
        Assert.False(predicate.Invoke(new Dog { IsActive = true, Name = "Bob" }));
    }

    [Fact]
    public void NullOperandsAreRejectedAtAppendTime()
    {
        var builder = PredicateBuilder<int>.Start();
        Assert.Equal("predicate", Assert.Throws<ArgumentNullException>(
            () => builder.And((IExpr<int, bool>)null!)).ParamName);
        Assert.Equal("predicate", Assert.Throws<ArgumentNullException>(
            () => builder.Or((IExpr<int, bool>)null!)).ParamName);
        Assert.Equal("predicate", Assert.Throws<ArgumentNullException>(
            () => builder.And((Expression<Func<int, bool>>)null!)).ParamName);
        Assert.Equal("predicate", Assert.Throws<ArgumentNullException>(
            () => builder.Or((Expression<Func<int, bool>>)null!)).ParamName);
    }

    private static int Depth(Expression expression)
    {
        var depth = 0;
        var pending = new Stack<(Expression Expression, int Depth)>();
        pending.Push((expression, 1));
        while (pending.Count > 0)
        {
            var (node, level) = pending.Pop();
            depth = Math.Max(depth, level);
            if (node is BinaryExpression binary &&
                (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse))
            {
                pending.Push((binary.Left, level + 1));
                pending.Push((binary.Right, level + 1));
            }
            else if (node is UnaryExpression unary && node.NodeType == ExpressionType.Not)
                pending.Push((unary.Operand, level + 1));
        }
        return depth;
    }

    private static bool Record(List<int> calls, int value, bool result)
    {
        calls.Add(value);
        return result;
    }

    private class Animal
    {
        public bool IsActive { get; init; }
    }

    private sealed class Dog : Animal
    {
        public string Name { get; init; } = "";
    }

    private sealed class ExternalDogExpression : IExpr<Dog, bool>
    {
        public bool Invoke(Dog value) => value.Name == "Ada";
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Dog, bool>>)(value => value.Name == "Ada");
    }

    private sealed class CountingExpression : Expr<int, bool>
    {
        public int Requests { get; private set; }

        public override Expression<Func<int, bool>> GetExpression()
        {
            Requests++;
            return value => value > 0;
        }
    }
}
