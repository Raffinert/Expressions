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
    public void CarriedDelegateFieldDoesNotReadItsPropertyReceiver(bool fail)
    {
        var holder = new FieldCallbackHolder { Fail = fail };
        Expression<Func<int, Func<int, int>>> expression = value => holder.Provider.Callback;
        Assert.Same(expression, Expand(expression));
        Assert.Equal(0, holder.Reads);
    }

    private sealed class FieldCallbackHolder
    {
        public int Reads;
        public bool Fail;
        public FieldCallbackProvider Provider
        {
            get
            {
                Reads++;
                if (Fail) throw new ApplicationException("synthetic property-backed field failure");
                return new FieldCallbackProvider();
            }
        }
    }

    private sealed class FieldCallbackProvider
    {
        public Func<int, int> Callback = value => value + 1;
    }

    [Fact]
    public void PropertyBackedMarkerDelegateFieldStillExpandsInCallbackPositions()
    {
        var condition = Condition<Row>.Create(row => row.Value > 10);
        var holder = new MarkerFieldHolder(condition.Invoke);
        var query = Rows.Where(row => holder.Provider.Callback(row))
            .Select(row => row.Children.Any(holder.Provider.Callback));
        Assert.Equal(new[] { false }, query.Provider.CreateQuery<bool>(Expand(query.Expression)));
        Assert.Equal(2, holder.Reads);
    }

    [Fact]
    public void ConstantMarkerDelegateStillExpands()
    {
        var condition = Condition<Row>.Create(row => row.Value > 10);
        Func<Row, bool> callback = condition.Invoke;
        var parameter = Expression.Parameter(typeof(Row));
        var expression = Expression.Lambda<Func<Row, bool>>(Expression.Invoke(Expression.Constant(callback), parameter), parameter);
        Assert.Equal(new[] { 2 }, Rows.Where((Expression<Func<Row, bool>>)Expand(expression)).Select(row => row.Id));
    }

    private sealed class MarkerFieldHolder(Func<Row, bool> callback)
    {
        public int Reads;
        public MarkerFieldProvider Provider { get { Reads++; return new MarkerFieldProvider(callback); } }
    }

    private sealed class MarkerFieldProvider(Func<Row, bool> callback)
    {
        public Func<Row, bool> Callback = callback;
    }

    [Fact]
    public void NonRaffinertDelegateMemberIsNotEagerlyReadByExpansion()
    {
        var holder = new OrdinaryDelegateHolder();
        Expression<Func<int, Func<int, int>>> expression = value => holder.Callback;
        Assert.Same(expression, Expand(expression));
        Assert.Equal(0, holder.Reads);
    }

    [Fact]
    public void ThrowingNonRaffinertDelegateMemberIsNotEagerlyReadByExpansion()
    {
        var holder = new OrdinaryDelegateHolder { Fail = true };
        Expression<Func<int, Func<int, int>>> expression = value => holder.Callback;
        Assert.Same(expression, Expand(expression));
        Assert.Equal(0, holder.Reads);
    }

    private sealed class OrdinaryDelegateHolder
    {
        public int Reads;
        public bool Fail;
        public Func<int, int> Callback
        {
            get
            {
                Reads++;
                if (Fail) throw new ApplicationException("synthetic delegate getter failure");
                return static value => value + 1;
            }
        }
    }

    [Fact]
    public void OrdinaryMethodGroupReceiverIsNotEagerlyReadByExpansion()
    {
        var holder = new MethodGroupHolder();
        Expression<Func<Row, bool>> expression = row => row.Children.Any(holder.Target.Invoke);
        Assert.Same(expression, Expand(expression));
        Assert.Equal(0, holder.Reads);
    }

    private sealed class MethodGroupHolder
    {
        public int Reads;
        public Unrelated Target { get { Reads++; return new Unrelated(); } }
    }

    [Fact]
    public void OpaqueDelegatePropertyStillExpandsInCallbackPositions()
    {
        var positive = Condition<Row>.Create(row => row.Value > 10);
        var holder = new MarkerDelegateHolder(positive.Invoke);
        var query = Rows.Where(row => holder.Callback(row))
            .Select(row => row.Children.Any(holder.Callback));
        Assert.Equal(new[] { false }, query.Provider.CreateQuery<bool>(Expand(query.Expression)));
        Assert.Equal(2, holder.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkerMethodGroupReadsWrapperTargetOnce(bool interfaceTyped)
    {
        var holder = new Holder();
        var query = interfaceTyped
            ? Rows.Select(row => row.Children.Any(((IComposableExpression<Row, bool>)holder.Condition).Invoke))
            : Rows.Select(row => row.Children.Any(holder.Condition.Invoke));
        Assert.Equal(new[] { false, false }, query.Provider.CreateQuery<bool>(Expand(query.Expression)));
        Assert.Equal(1, holder.Reads);
    }

    private sealed class MarkerDelegateHolder(Func<Row, bool> callback)
    {
        public int Reads;
        public Func<Row, bool> Callback { get { Reads++; return callback; } }
    }

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
    public void HiddenInvokeOnANativeWrapperIsNotAMarker()
    {
        var condition = new HiddenInvoke();
        var query = Rows.Where(x => condition.Invoke(x));
        Assert.Same(query.Expression, Expand(query.Expression));
        Assert.Equal(new[] { 1 }, query.Select(x => x.Id));
    }

    [Fact]
    public void ExternalInterfaceImplementationHasADescriptiveRestriction()
    {
        IComposableExpression<Row, bool> condition = new ExternalCondition();
        var query = Rows.Where(x => condition.Invoke(x));
        var error = Assert.Throws<NotSupportedException>(() => Expand(query.Expression));
        Assert.Contains("derived from ComposableExpression", error.Message);
    }

    [Fact]
    public void InterfaceTypedCycleStillFailsDescriptively()
    {
        var condition = new LinkedCondition();
        IComposableExpression<Row, bool> reference = condition;
        condition.Next = Condition<Row>.Create(x => reference.Invoke(x));
        var query = Rows.Where(x => reference.Invoke(x));
        Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() => Expand(query.Expression)).Message);
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

    private sealed class HiddenInvoke : Condition<Row>
    {
        public override Expression<Func<Row, bool>> GetExpression() => x => x.Value > 10;
        public new bool Invoke(Row row) => row.Value < 10;
    }

    private sealed class ExternalCondition : IComposableExpression<Row, bool>
    {
        public bool Invoke(Row value) => value.Value > 10;
        public LambdaExpression GetExpandedLambdaExpression() => (Expression<Func<Row, bool>>)(x => x.Value > 10);
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
