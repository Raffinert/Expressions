using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ExplicitCollectionDirectiveBinderTests
{
    [Fact]
    public void ValidationReadsNoGettersAndDoesNotEnumerate()
    {
        var holder = new Holder();
        Expression<Func<int[]>> approved = () => holder.Ids;
        Expression<Func<int[]>> rejected = () => holder.Ids.Append(4).ToArray();
        Assert.True(ExplicitCollectionDirectiveBinder.ValidateCapture(approved.Body));
        Assert.False(ExplicitCollectionDirectiveBinder.ValidateCapture(rejected.Body));
        Assert.False(ExplicitCollectionDirectiveBinder.ValidateCapture(Expression.NewArrayInit(typeof(int), Expression.Constant(1))));
        Assert.False(ExplicitCollectionDirectiveBinder.ValidateCapture(Expression.Default(typeof(int[]))));
        Assert.False(ExplicitCollectionDirectiveBinder.ValidateCapture(Expression.Constant(null, typeof(int[]))));
        Assert.False(ExplicitCollectionDirectiveBinder.ValidateCapture(Expression.Parameter(typeof(int[]))));
        Assert.Equal(0, holder.Reads);
        Assert.Throws<NotSupportedException>(() => ExplicitCollectionDirectiveBinder.Bind(approved.Body, null, new([])));
        Assert.Equal(0, holder.Reads);
    }

    [Fact]
    public void ExistingNativeParameterIsRetainedWithoutBinding()
    {
        var parameter = new QueryParameterExpression("native", typeof(int[]));
        Assert.Same(parameter, ExplicitCollectionDirectiveBinder.Bind(parameter, null, new([])));
    }

    [Theory]
    [InlineData(typeof(int[]), true)]
    [InlineData(typeof(List<int>), true)]
    [InlineData(typeof(int?[]), true)]
    [InlineData(typeof(string[]), true)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(IEnumerable<int>), false)]
    [InlineData(typeof(IQueryable<int>), false)]
    [InlineData(typeof(List<string>), false)]
    [InlineData(typeof(object[]), false)]
    public void CollectionTypesAreExplicit(Type type, bool expected) =>
        Assert.Equal(expected, ExplicitCollectionDirectiveBinder.IsSupportedType(type));

    private sealed class Holder
    {
        public int Reads { get; private set; }
        public int[] Ids { get { Reads++; throw new InvalidOperationException("Must not run."); } }
    }
}
