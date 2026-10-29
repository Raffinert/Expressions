using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class UnifiedExpressionTests
{
    [Fact]
    public void BooleanAndValueExpressionsHaveTheSameConcretePublicType()
    {
        var isActive = ComposableExpression<Product>.Create(p => p.Price > 0m);
        var name = ComposableExpression<Product>.Create(p => p.Name);

        Assert.IsAssignableFrom<ComposableExpression<Product, bool>>(isActive);
        Assert.IsAssignableFrom<ComposableExpression<Product, string>>(name);

        var result = isActive.And(p => p.Name.Length > 0).Not();
        Assert.False(result.Invoke(new Product { Price = 1m, Name = "A" }));
        Assert.True(result.Invoke(new Product { Price = 0m, Name = "A" }));
    }

    [Fact]
    public void MixedForwardCompositionProducesBooleanExpressionUsableByWhere()
    {
        var price = ComposableExpression<Product>.Create(p => p.Price);
        var positive = ComposableExpression<decimal>.Create(value => value > 0);
        ComposableExpression<Product, bool> predicate = price.Then(positive);

        var query = new[]
        {
            new Product { Price = 0m },
            new Product { Price = 10m }
        }.AsQueryable().Where(predicate);

        Assert.Single(query);
        Assert.Equal(10m, query.Single().Price);
    }

    [Fact]
    public void StartAndTrueHaveDifferentFirstCompositionSemantics()
    {
        var p = ComposableExpression<Product>.Create(x => x.Price > 10m);
        Assert.Same(p, ComposableExpression<Product>.Start().And(p));
        Assert.Same(p, ComposableExpression<Product>.Start().Or(p));
        Assert.NotSame(p, ComposableExpression<Product>.True.And(p));
    }

    [Fact]
    public void ExplicitlyTypedAndInferredFactoriesAgreeOnExpandedTree()
    {
        Expression<Func<Product, bool>> lambda = product => product.Price > 10m;
        var inferred = ComposableExpression<Product>.Create(lambda);
        var typed = ComposableExpression<Product, bool>.Create(lambda);

        Assert.Equal(inferred.GetExpandedExpression().ToString(), typed.GetExpandedExpression().ToString());
    }
}
