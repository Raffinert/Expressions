using System.Linq.Expressions;

namespace Raffinert.Expressions.UnitTests;

public class WholeQueryExpansionTests
{
    private static readonly IQueryable<Row> Rows = new[]
    {
        new Row { Id = 1, Value = 2 },
        new Row { Id = 2, Value = 12, Child = new Row { Value = 4 } }
    }.AsQueryable();

    private static Expression Expand(Expression root) => ExpressionExpander.Expand(root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceTypedInvocationExpands(bool explicitCast)
    {
        IComposableExpression<Row, bool> condition = Condition<Row>.Create(x => x.Value > 10);
        var concrete = Condition<Row>.Create(x => x.Value > 10);
        var query = explicitCast
            ? Rows.Where(x => ((IComposableExpression<Row, bool>)concrete).Invoke(x))
            : Rows.Where(x => condition.Invoke(x));
        var expanded = Expand(query.Expression);
        Assert.DoesNotContain("Invoke", expanded.ToString());
        Assert.Equal(new[] { 2 }, query.Provider.CreateQuery<Row>(expanded).Select(x => x.Id));
    }

    [Fact]
    public void NullRootIsRejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Expand(null!));
        Assert.Equal("expression", exception.ParamName);
    }

    [Fact]
    public void RootWherePreservesQueryableAndExpandsCapturedCondition()
    {
        var condition = Condition<Row>.Create(x => x.Value > 10);
        var query = Rows.Where(x => condition.Invoke(x));
        var root = Expand(query.Expression);

        Assert.Equal(typeof(Queryable), Assert.IsType<MethodCallExpression>(root, exactMatch: false).Method.DeclaringType);
        Assert.DoesNotContain("Invoke", root.ToString());
        Assert.Equal(new[] { 2 }, query.Provider.CreateQuery<Row>(root).Select(x => x.Id));
    }

    [Fact]
    public void NestedOperatorsAndCrossCompositionShareTheVisitor()
    {
        var value = Projection<Row>.Create(x => x.Value);
        var condition = Condition<Row>.Create(x => value.Invoke(x) > 1);
        var result = Projection<Row>.Create(x => condition.Invoke(x) ? value.Invoke(x) : 0);
        var query = Rows.Where(x => condition.Invoke(x)).OrderByDescending(x => value.Invoke(x)).Select(x => result.Invoke(x));
        var root = Expand(query.Expression);

        Assert.DoesNotContain("Invoke", root.ToString());
        Assert.Equal(new[] { 12, 2 }, query.Provider.CreateQuery<int>(root));
    }

    [Fact]
    public void NullPropagationReturnsDefaultForReferenceAndNullableValueInputs()
    {
        var value = Projection<Row>.Create(x => x.Value);
        var query = Rows.Select(x => value.InvokeOrDefault(x.Child));
        Assert.Equal(new[] { 0, 4 }, query.Provider.CreateQuery<int>(Expand(query.Expression)));

        var nullable = Projection<int?, int>.Create(x => x!.Value + 1);
        var inputs = new int?[] { null, 2 }.AsQueryable();
        var numbers = inputs.Select(x => nullable.InvokeOrDefault(x));
        Assert.Equal(new[] { 0, 3 }, numbers.Provider.CreateQuery<int>(Expand(numbers.Expression)));
    }

    [Fact]
    public void NonNullableValueInputHasNoNullGuard()
    {
        var value = Projection<int, int>.Create(x => x + 1);
        var query = new[] { 0, 2 }.AsQueryable().Select(x => value.InvokeOrDefault(x));
        var root = Expand(query.Expression);
        Assert.DoesNotContain("IIF", root.ToString());
        Assert.Equal(new[] { 1, 3 }, query.Provider.CreateQuery<int>(root));
    }

    [Fact]
    public void NoMarkersAndUnrelatedInvokeRemainUnchanged()
    {
        var unrelated = new Unrelated();
        var query = Rows.Where(x => x.Value > 1 && unrelated.Invoke(x));
        var root = Expand(query.Expression);
        Assert.Same(query.Expression, root);
        Assert.Contains("Invoke", root.ToString());
        Assert.Equal(2, query.Provider.CreateQuery<Row>(root).Count());
    }

    [Fact]
    public void UnsupportedTargetDoesNotExecuteArbitraryFactory()
    {
        var factory = new Factory();
        var query = Rows.Where(x => factory.Create().Invoke(x));
        var exception = Assert.Throws<InvalidOperationException>(() => Expand(query.Expression));
        Assert.Contains("Unable to resolve expression instance", exception.Message);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndIndirectCyclesFailDescriptively(bool indirect)
    {
        var first = new LinkedCondition();
        first.Next = indirect ? new LinkedCondition { Next = first } : first;
        var query = Rows.Where(x => first.Invoke(x));
        var exception = Assert.Throws<InvalidOperationException>(() => Expand(query.Expression));
        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedMethodGroupsAndCapturedDelegatesExpand()
    {
        var positive = Condition<Row>.Create(x => x.Value > 0);
        Func<Row, bool> predicate = positive.Invoke;
        var query = Rows.Select(x => x.Children.Any(positive.Invoke) || x.Children.Any(predicate));
        var root = Expand(query.Expression);
        Assert.DoesNotContain("Invoke", root.ToString());
        Assert.Equal(new[] { false, false }, query.Provider.CreateQuery<bool>(root));
    }

    [Fact]
    public void ClosurePropertyGetterIsEvaluatedAndFailureKeepsInnerException()
    {
        var holder = new Holder();
        var query = Rows.Where(x => holder.Condition.Invoke(x));
        Expand(query.Expression);
        Assert.True(holder.Reads > 0);

        holder.Throw = true;
        var exception = Assert.Throws<InvalidOperationException>(() => Expand(query.Expression));
        Assert.Contains("Unable to read closure member", exception.Message);
        Assert.IsType<ApplicationException>(exception.InnerException);
    }

    [Fact]
    public void DirectConstructorIsEvaluatedAndFailureKeepsInnerException()
    {
        var counter = new Counter();
        var query = Rows.Where(x => new ConstructedCondition(counter, false).Invoke(x));
        Expand(query.Expression);
        Assert.True(counter.Calls > 0);

        var failing = Rows.Where(x => new ConstructedCondition(counter, true).Invoke(x));
        var exception = Assert.Throws<InvalidOperationException>(() => Expand(failing.Expression));
        Assert.Contains("Unable to construct expression instance", exception.Message);
        Assert.IsType<ApplicationException>(exception.InnerException);
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public Row? Child { get; set; }
        public List<Row> Children { get; } = [];
    }

    private sealed class Unrelated
    {
        public bool Invoke(Row row) => row.Value > 0;
    }

    private sealed class Factory
    {
        public int Calls { get; private set; }
        public Condition<Row> Create()
        {
            Calls++;
            return Condition<Row>.True;
        }
    }

    private sealed class LinkedCondition : Condition<Row>
    {
        public Condition<Row> Next { get; set; } = null!;
        public override Expression<Func<Row, bool>> GetExpression() => x => Next.Invoke(x);
    }

    private sealed class Holder
    {
        public int Reads { get; private set; }
        public bool Throw { get; set; }
        public Condition<Row> Condition
        {
            get
            {
                Reads++;
                if (Throw) throw new ApplicationException("getter failure");
                return Condition<Row>.True;
            }
        }
    }

    private sealed class Counter
    {
        public int Calls { get; set; }
    }

    private sealed class ConstructedCondition : Condition<Row>
    {
        public ConstructedCondition(Counter counter, bool fail)
        {
            counter.Calls++;
            if (fail) throw new ApplicationException("constructor failure");
        }

        public override Expression<Func<Row, bool>> GetExpression() => x => x.Value > 0;
    }
}
