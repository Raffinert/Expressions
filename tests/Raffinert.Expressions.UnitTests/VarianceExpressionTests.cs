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

        var predicate = PredicateBuilder<Dog>.Create().And(shared).Build();
        Assert.True(predicate.Invoke(new Dog { Active = true }));
        Assert.False(predicate.Invoke(new Dog { Active = false }));
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
    public void ThenPreservesIntermediateTypeWhenCovariantBodyIsNarrower()
    {
        IExpr<Dog, Animal> first = Expr<Dog>.Create(dog => dog);
        var cat = new Cat();
        var compareToCat = Expr<Animal>.Create(animal => animal == cat);

        var composed = first.Then(compareToCat);
        var body = Assert.IsAssignableFrom<BinaryExpression>(composed.GetExpression().Body);
        var converted = Assert.IsAssignableFrom<UnaryExpression>(body.Left);

        Assert.Equal(ExpressionType.Convert, converted.NodeType);
        Assert.Equal(typeof(Animal), converted.Type);
        Assert.Equal(typeof(Dog), converted.Operand.Type);

        var dog = new Dog { Name = "Ada" };
        Assert.False(composed.Invoke(dog));
        Assert.Empty(new[] { dog }.AsQueryable().Where(composed));
    }

    [Fact]
    public void ThenKeepsReferenceEqualityForCovariantViews()
    {
        var original = new Dog { Name = "Ada" };
        IExpr<Dog, Animal> first = Expr<Dog>.Create(dog => dog);
        var next = Expr<Animal>.Create(animal => animal == original);

        var composed = first.Then(next);

        Assert.True(composed.Invoke(original));
        Assert.False(composed.Invoke(new Dog { Name = "Ada" }));
        Assert.Single(new[] { original, new Dog { Name = "Ada" } }.AsQueryable().Where(composed));
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
    public void DirectConcreteExternalInvokeExpandsAsNestedExpression()
    {
        var external = new ExternalDogExpression();
        var nested = Expr<Dog>.Create(dog => external.Invoke(dog));

        var expanded = nested.GetExpandedExpression();

        Assert.DoesNotContain("Invoke", expanded.ToString());
        Assert.True(nested.Invoke(new Dog { Active = true }));
        Assert.False(nested.Invoke(new Dog { Active = false }));
        Assert.Single(new[]
        {
            new Dog { Active = true },
            new Dog { Active = false }
        }.AsQueryable().Where(nested));
    }

    [Fact]
    public void OrdinaryInvokeWithPropertyReceiverIsNotEagerlyEvaluated()
    {
        var holder = new UnrelatedInvokeHolder();
        Expression<Func<Dog, bool>> ordinary = dog => holder.Target.Invoke(dog);

        var expanded = ExpressionExpander.Expand(ordinary);

        Assert.Same(ordinary, expanded);
        Assert.Equal(0, holder.Reads);
        Assert.True(expanded.Compile()(new Dog { Active = true }));
        Assert.Equal(1, holder.Reads);
    }

    [Fact]
    public void NonMarkerInvokeOverloadOnExternalIExprIsPreserved()
    {
        var external = new ExternalDogExpression();
        var nested = Expr<Dog>.Create(dog => external.Invoke(dog, 1));

        var expanded = nested.GetExpandedExpression();

        Assert.Contains("Invoke", expanded.ToString());
        Assert.False(nested.Invoke(new Dog { Active = true }));
    }

    [Fact]
    public void InlineConcreteExternalInvokeExpandsBeforeQueryExecution()
    {
        var external = new ExternalDogExpression();
        var source = new[]
        {
            new Dog { Puppies = [new Dog { Active = true }] },
            new Dog { Puppies = [new Dog { Active = false }] }
        }.AsQueryable();
        var query = source.Where(dog => dog.Puppies.Any(external.Invoke));

        var expanded = ExpressionExpander.Expand(query.Expression);

        Assert.DoesNotContain("CreateDelegate", expanded.ToString());
        Assert.Contains(".Active", expanded.ToString());
        Assert.Single(query.Provider.CreateQuery<Dog>(expanded));
    }

    [Fact]
    public void InlineUnrelatedMethodOnExternalExpressionIsNotExpanded()
    {
        var external = new ExternalDogExpression();
        var source = new[]
        {
            new Dog { Puppies = [new Dog { Active = true }] }
        }.AsQueryable();
        var query = source.Where(dog => dog.Puppies.Any(external.Unrelated));

        var expanded = ExpressionExpander.Expand(query.Expression);

        Assert.Empty(query.Provider.CreateQuery<Dog>(expanded));
        Assert.DoesNotContain(".Active", expanded.ToString());
    }

    [Fact]
    public void CapturedConcreteExternalInvokeExpandsBeforeQueryExecution()
    {
        var external = new ExternalDogExpression();
        Func<Dog, bool> callback = external.Invoke;
        Assert.Equal(typeof(ExternalDogExpression), callback.Method.DeclaringType);

        var source = new[]
        {
            new Dog { Puppies = [new Dog { Active = true }] },
            new Dog { Puppies = [new Dog { Active = false }] }
        }.AsQueryable();
        var query = source.Where(dog => dog.Puppies.Any(callback));

        var expanded = ExpressionExpander.Expand(query.Expression);

        Assert.Contains(".Active", expanded.ToString());
        Assert.DoesNotContain("callback", expanded.ToString());
        Assert.Single(query.Provider.CreateQuery<Dog>(expanded));
    }

    [Fact]
    public void CapturedExplicitExternalInvokeExpandsThroughInterfaceMap()
    {
        IExpr<Dog, bool> external = new ExplicitDogExpression();
        Func<Dog, bool> callback = external.Invoke;
        var source = new[]
        {
            new Dog { Puppies = [new Dog { Active = true }] },
            new Dog { Puppies = [new Dog { Active = false }] }
        }.AsQueryable();
        var query = source.Where(dog => dog.Puppies.Any(callback));

        var expanded = ExpressionExpander.Expand(query.Expression);

        Assert.Contains(".Active", expanded.ToString());
        Assert.Single(query.Provider.CreateQuery<Dog>(expanded));
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

    private sealed class ExternalDogExpression : IExpr<Dog, bool>
    {
        public bool Invoke(Dog value) => value.Active;
        public bool Invoke(Dog value, int ignored) => false;
        public bool Unrelated(Dog value) => false;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Dog, bool>>)(value => value.Active);
    }

    private sealed class ExplicitDogExpression : IExpr<Dog, bool>
    {
        bool IExpr<Dog, bool>.Invoke(Dog value) => value.Active;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Dog, bool>>)(value => value.Active);
    }

    private sealed class UnrelatedInvokeHolder
    {
        public int Reads { get; private set; }
        public UnrelatedDog Target
        {
            get
            {
                Reads++;
                return new UnrelatedDog();
            }
        }
    }

    private sealed class UnrelatedDog
    {
        public bool Invoke(Dog dog) => dog.Active;
    }

    private sealed class InvalidDogExpression : IExpr<Dog, bool>
    {
        public bool Invoke(Dog value) => true;
        public LambdaExpression GetExpandedLambdaExpression() =>
            (Expression<Func<Cat, bool>>)(cat => cat.Active);
    }
}
