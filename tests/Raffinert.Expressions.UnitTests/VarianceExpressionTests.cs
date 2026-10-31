using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class VarianceExpressionTests
{
    [Fact]
    public void ContravariantInterfaceInvokesAndFiltersDerivedSources()
    {
        IExpr<Animal, bool> allAnimals = Expr<Animal>.Create(animal => animal.Active);
        IExpr<Dog, bool> dogs = allAnimals;

        Assert.True(dogs.Invoke(new Dog { Active = true }));
        Assert.False(dogs.Invoke(new Dog { Active = false }));

        var source = new[]
        {
            new Dog { Active = true, Name = "Ada" },
            new Dog { Active = false, Name = "Bob" }
        }.AsQueryable();

        Assert.Equal(["Ada"], source.Where(dogs).Select(dog => dog.Name).ToArray());
        Assert.Equal(["Bob"], source.Where(dogs.Not()).Select(dog => dog.Name).ToArray());
    }

    [Fact]
    public void BooleanCompositionAdaptsBothContravariantOperands()
    {
        IExpr<Animal, bool> active = Expr<Animal>.Create(animal => animal.Active);
        IExpr<Dog, bool> shared = active;
        var named = Expr<Dog>.Create(dog => dog.Name == "Ada");

        var both = shared.And(named);
        var either = named.Or(shared);
        Assert.True(both.Invoke(new Dog { Active = true, Name = "Ada" }));
        Assert.False(both.Invoke(new Dog { Active = false, Name = "Ada" }));
        Assert.True(either.Invoke(new Dog { Active = true, Name = "Bob" }));
        Assert.False(either.Invoke(new Dog { Active = false, Name = "Bob" }));

        var starter = Expr<Dog>.Start().And(shared);
        Assert.True(starter.Invoke(new Dog { Active = true }));
        Assert.False(starter.Invoke(new Dog { Active = false }));
    }

    [Fact]
    public void CovariantAndContravariantViewsComposeInThenAndSelect()
    {
        IExpr<Animal, string> names = Expr<Animal>.Create(animal => animal.Name);
        IExpr<Dog, object> viewedAsObjects = names;
        var source = new[]
        {
            new Dog { Name = "Ada" },
            new Dog { Name = "Bob" }
        }.AsQueryable();

        Assert.Equal(["Ada", "Bob"], source.Select(viewedAsObjects).Cast<string>().ToArray());

        IExpr<object, int> length = Expr<object>.Create(value => value.ToString()!.Length);
        var composed = viewedAsObjects.Then(length);
        Assert.Equal(3, composed.Invoke(new Dog { Name = "Ada" }));
        Assert.Equal([3, 3], source.Select(composed).ToArray());

        var nested = Expr<Dog>.Create(dog => viewedAsObjects.Invoke(dog));
        Assert.Equal("Ada", nested.Invoke(new Dog { Name = "Ada" }));
    }

    [Fact]
    public void DirectProjectionAndOrderingAcceptContravariantSources()
    {
        IExpr<Animal, string> names = Expr<Animal>.Create(animal => animal.Name);
        IExpr<Dog, string> dogNames = names;

        var source = new[]
        {
            new Dog { Name = "Bob" },
            new Dog { Name = "Ada" }
        }.AsQueryable();

        Assert.Equal(["Ada", "Bob"], source.OrderBy(dogNames).Select(dogNames).ToArray());
        Assert.Equal(["Bob", "Ada"], source.OrderByDescending(dogNames).Select(dogNames).ToArray());
    }

    [Fact]
    public void NestedVarianceInvokeAndNullFallbackExpandCorrectly()
    {
        IExpr<Animal, bool> original = Expr<Animal>.Create(animal => animal.Active);
        IExpr<Dog, bool> dogs = original;
        var nested = Expr<Dog>.Create(dog => dogs.Invoke(dog));

        Assert.True(nested.Invoke(new Dog { Active = true }));
        Assert.False(nested.Invoke(new Dog { Active = false }));
        Assert.DoesNotContain("Invoke", nested.GetExpandedExpression().ToString());

        var native = Expr<Animal>.Create(animal => animal.Active);
        var fallback = Expr<Dog>.Create(dog => native.InvokeOrDefault(dog));
        Assert.True(fallback.Invoke(new Dog { Active = true }));
        Assert.False(fallback.Invoke(new Dog { Active = false }));
        Assert.False(fallback.Invoke(null!));
    }

    [Fact]
    public void ContravariantMethodGroupAndCapturedCallbackUseDerivedDelegate()
    {
        IExpr<Animal, bool> source = Expr<Animal>.Create(animal => animal.Active);
        IExpr<Dog, bool> shared = source;
        Func<Dog, bool> callback = shared.Invoke;
        var query = new[]
        {
            new Dog { Puppies = [new Dog { Active = true }] },
            new Dog { Puppies = [new Dog { Active = false }] }
        }.AsQueryable();

        var methodGroup = query.Where(dog => dog.Puppies.Any(shared.Invoke));
        var captured = query.Where(dog => dog.Puppies.Any(callback));
        var expandedMethodGroup = ExpressionExpander.Expand(methodGroup.Expression);
        var expandedCaptured = ExpressionExpander.Expand(captured.Expression);

        Assert.DoesNotContain("Invoke", expandedMethodGroup.ToString());
        Assert.DoesNotContain("Invoke", expandedCaptured.ToString());
        Assert.Single(query.Provider.CreateQuery<Dog>(expandedMethodGroup));
        Assert.Single(query.Provider.CreateQuery<Dog>(expandedCaptured));
    }

    [Fact]
    public void ExternalInterfaceAndInvalidSourceShapesRespectVariance()
    {
        IExpr<Animal, bool> external = new ExternalAnimalExpression();
        IExpr<Dog, bool> dogs = external;

        Assert.Single(new[]
        {
            new Dog { Active = true },
            new Dog { Active = false }
        }.AsQueryable().Where(dogs));

        IExpr<Dog, bool> invalid = new InvalidDogExpression();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new[] { new Dog() }.AsQueryable().Where(invalid));
        Assert.Contains("compatible single-parameter lambda", error.Message);
    }

    private class Animal
    {
        public bool Active { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class Dog : Animal
    {
        public List<Dog> Puppies { get; init; } = [];
    }

    private sealed class Cat : Animal
    {
    }

    private sealed class ExternalAnimalExpression : IExpr<Animal, bool>
    {
        public bool Invoke(Animal value) => value.Active;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Animal, bool>>)(animal => animal.Active);
    }

    private sealed class InvalidDogExpression : IExpr<Dog, bool>
    {
        public bool Invoke(Dog value) => true;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Cat, bool>>)(cat => cat.Active);
    }
}
