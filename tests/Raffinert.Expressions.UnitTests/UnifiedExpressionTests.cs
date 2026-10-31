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
        var startAnd = PredicateBuilder<Product>.Start().And(p).Build();
        var startOr = PredicateBuilder<Product>.Start().Or(p).Build();
        var trueAnd = PredicateBuilder<Product>.True().And(p).Build();

        Assert.Equal(p.Invoke(new Product { Price = 12m }), startAnd.Invoke(new Product { Price = 12m }));
        Assert.Equal(p.Invoke(new Product { Price = 12m }), startOr.Invoke(new Product { Price = 12m }));
        Assert.Equal(ExpressionType.AndAlso, trueAnd.GetExpression().Body.NodeType);
    }

    [Fact]
    public void ExplicitlyTypedAndInferredFactoriesAgreeOnExpandedTree()
    {
        Expression<Func<Product, bool>> lambda = product => product.Price > 10m;
        var inferred = Expr<Product>.Create(lambda);
        var typed = Expr<Product, bool>.Create(lambda);

        Assert.Equal(inferred.GetExpandedExpression().ToString(), typed.GetExpandedExpression().ToString());
    }

    [Fact]
    public void InterfaceTypedExpressionsCanComposeWithCustomImplementations()
    {
        IExpr<Product, bool> positive = new ExternalExpr<Product, bool>(p => p.Price > 0m);
        IExpr<Product, bool> named = Expr<Product>.Create(p => p.Name == "A");

        var predicate = positive.And(named).Not();
        Assert.False(predicate.Invoke(new Product { Price = 10m, Name = "A" }));
        Assert.True(predicate.Invoke(new Product { Price = 10m, Name = "B" }));

        IExpr<Product, decimal> price = Expr<Product>.Create(p => p.Price);
        IExpr<decimal, bool> threshold = new ExternalExpr<decimal, bool>(value => value >= 10m);
        var forward = price.Then(threshold);
        Assert.True(forward.Invoke(new Product { Price = 10m }));
        Assert.False(forward.Invoke(new Product { Price = 9m }));
    }

    [Fact]
    public void BooleanStarterPreservesInterfaceOperands()
    {
        IExpr<Product, bool> custom = new ExternalExpr<Product, bool>(p => p.Price > 10m);
        var predicate = PredicateBuilder<Product>.Start().And(custom).Build();
        Assert.True(predicate.Invoke(new Product { Price = 11m }));
        Assert.False(predicate.Invoke(new Product { Price = 9m }));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(1024)]
    public void LongBooleanChainsRemainComposableAndExecutable(int count)
    {
        var builder = PredicateBuilder<Product>.Start(defaultWhenEmpty: true);
        for (var index = 0; index < count; index++)
            builder.And(product => product.Price >= 0m);

        var chain = builder.Build();
        Assert.True(chain.Invoke(new Product { Price = 1m }));
        Assert.False(chain.Invoke(new Product { Price = -1m }));
        Assert.Single(new[]
        {
            new Product { Price = 1m },
            new Product { Price = -1m }
        }.AsQueryable().Where(chain));
    }

    [Fact]
    public void MappingUsesLazyCacheWithoutPerExpressionMappingState()
    {
        var projection = Expr<Product>.Create(p => new ProductDto { Name = p.Name });
        var fields = typeof(Expr<Product, ProductDto>).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, f => f.Name.Contains("mapToExisting", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, f => f.Name.Contains("projectionCache", StringComparison.OrdinalIgnoreCase));

        var expression = projection.GetMapToExistingExpression();
        Assert.Same(expression, projection.GetMapToExistingExpression());

        var actions = Enumerable.Range(0, 32).AsParallel()
            .Select(_ => projection.GetMapToExistingAction()).ToArray();
        Assert.All(actions, action => Assert.Same(actions[0], action));

        ProductDto? existing = new() { Name = "old" };
        projection.MapToExisting(new Product { Name = "updated" }, ref existing);
        Assert.Equal("updated", existing!.Name);
    }

    [Fact]
    public void InterfaceTypedMemberInitializerSupportsMappingAndMerging()
    {
        IExpr<Product, ProductDto> first = new ExternalExpr<Product, ProductDto>(
            p => new ProductDto { Id = p.Id });
        IExpr<Product, ProductDto> second = Expr<Product>.Create(
            p => new ProductDto { Name = p.Name });
        var merged = first.MergeBindings(second);
        var result = merged.Invoke(new Product { Id = 7, Name = "Desk" });
        Assert.Equal(7, result.Id);
        Assert.Equal("Desk", result.Name);

        ProductDto? destination = new() { Name = "before" };
        first.MapToExisting(new Product { Id = 11 }, ref destination);
        Assert.Equal(11, destination!.Id);
        Assert.Equal("before", destination.Name);
    }

    private sealed class ExternalExpr<TSource, TResult>(Expression<Func<TSource, TResult>> lambda)
        : IExpr<TSource, TResult>
    {
        public TResult Invoke(TSource value) => lambda.Compile()(value);

        public LambdaExpression GetExpandedLambdaExpression() => lambda;
    }


    [Fact]
    public void BooleanCompositionDoesNotEagerlyExpandNestedExpressions()
    {
        var nested = new CountingBoolExpression();
        IExpr<Product, bool> outer = Expr<Product>.Create(p => nested.Invoke(p));
        var combined = outer.And(p => p.Name.Length > 0).Or(p => p.Price == 0m).Not();

        Assert.Equal(0, nested.ExpressionRequests);
        var expanded = combined.GetExpandedExpression();
        Assert.Equal(1, nested.ExpressionRequests);
        Assert.True(combined.Invoke(new Product { Price = 3m, Name = "A" }) == false);
        Assert.DoesNotContain(nameof(Expr<Product, bool>.Invoke), expanded.Body.ToString());
    }

    private sealed class CountingBoolExpression : Expr<Product, bool>
    {
        public int ExpressionRequests { get; private set; }

        public override Expression<Func<Product, bool>> GetExpression()
        {
            ExpressionRequests++;
            return p => p.Price > 0m;
        }
    }

}
