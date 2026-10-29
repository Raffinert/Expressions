using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class UnifiedExpressionTests
{
    [Fact]
    public void BooleanAndValueExpressionsHaveTheSameConcretePublicType()
    {
        var isActive = Expr<Product>.Create(p => p.Price > 0m);
        var name = Expr<Product>.Create(p => p.Name);

        Assert.IsAssignableFrom<Expr<Product, bool>>(isActive);
        Assert.IsAssignableFrom<Expr<Product, string>>(name);

        var result = isActive.And(p => p.Name.Length > 0).Not();
        Assert.False(result.Invoke(new Product { Price = 1m, Name = "A" }));
        Assert.True(result.Invoke(new Product { Price = 0m, Name = "A" }));
    }

    [Fact]
    public void MixedForwardCompositionProducesBooleanExpressionUsableByWhere()
    {
        var price = Expr<Product>.Create(p => p.Price);
        var positive = Expr<decimal>.Create(value => value > 0);
        Expr<Product, bool> predicate = price.Then(positive);

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
        var p = Expr<Product>.Create(x => x.Price > 10m);
        Assert.Same(p, Expr<Product>.Start().And(p));
        Assert.Same(p, Expr<Product>.Start().Or(p));
        Assert.NotSame(p, Expr<Product>.True.And(p));
    }

    [Fact]
    public void ExplicitlyTypedAndInferredFactoriesAgreeOnExpandedTree()
    {
        Expression<Func<Product, bool>> lambda = product => product.Price > 10m;
        var inferred = Expr<Product>.Create(lambda);
        var typed = Expr<Product, bool>.Create(lambda);

        Assert.Equal(inferred.GetExpandedExpression().ToString(), typed.GetExpandedExpression().ToString());
    }
}
