using System.Linq.Expressions;

namespace Raffinert.Expressions.EntityFrameworkCore.IntegrationTests;

public class ComputedDirectiveOperandEvaluatorTests
{
    [Fact]
    public void ValidationNeverReadsApprovedOrRejectedGetters()
    {
        var holder = new Holder();
        Expression<Func<int>> approved = () => holder.Value + 100;
        Expression<Func<int>> rejected = () => holder.Value + Unsupported();
        Assert.True(ComputedDirectiveOperandEvaluator.Validate(approved.Body));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(rejected.Body));
        Assert.Equal(0, holder.Reads);
        Assert.Equal(1100, ComputedDirectiveOperandEvaluator.EvaluateApproved(approved.Body));
        Assert.Equal(1, holder.Reads);
    }

    [Fact]
    public void EntireGrammarIsCheckedWithoutExecutingAnything()
    {
        var holder = new Holder();
        var selectGetter = true;
        var array = new[] { 1 };
        Func<int> invoke = Unsupported;
        Expression<Func<int>>[] rejected =
        [
            () => holder.Value + Unsupported(),
            () => selectGetter ? holder.Value : Unsupported(),
            () => holder.Value + new Holder().Value,
            () => holder.Value + array[0],
            () => holder.Value + invoke(),
            () => holder.Value + DateTime.Now.Day,
            () => holder.Value / 1,
            () => holder.Value % 1,
            () => -holder.Value,
            () => (int)((decimal)holder.Value + 1m)
        ];
        foreach (var expression in rejected)
            Assert.False(ComputedDirectiveOperandEvaluator.Validate(expression.Body));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(Expression.Parameter(typeof(int))));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(Expression.NewArrayInit(typeof(int), Expression.Constant(1))));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(Expression.TypeAs(Expression.Constant(holder), typeof(object))));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(Expression.Assign(Expression.Parameter(typeof(int)), Expression.Constant(1))));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(Expression.Default(typeof(object))));
        Assert.Equal(0, holder.Reads);
    }

    [Fact]
    public void BoundsRejectOversizedTreesBeforeEvaluation()
    {
        Expression expression = Expression.Constant(1);
        for (var i = 0; i < 65; i++) expression = Expression.Add(expression, Expression.Constant(1));
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(expression));
        expression = Expression.Constant(1);
        for (var i = 0; i < 9; i++) expression = Expression.Add(expression, expression);
        Assert.False(ComputedDirectiveOperandEvaluator.Validate(expression));
    }

    [Fact]
    public void MissingPreparationContextRejectsApprovedOperandBeforeGetter()
    {
        var holder = new Holder();
        var condition = Condition<OrderRow>.Create(x => x.TotalCents > Microsoft.EntityFrameworkCore.EF.Parameter(holder.Value + 100));
        Expression<Func<OrderRow, bool>> expression = x => condition.Invoke(x);
        Assert.Throws<NotSupportedException>(() => EfQueryExpansion.Expand(expression));
        Assert.Equal(0, holder.Reads);
    }

    private static int Unsupported() => throw new InvalidOperationException("Must never execute.");
    private sealed class Holder
    {
        public int Reads { get; private set; }
        public int Value { get { Reads++; return 1000; } }
    }
}
