using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Raffinert.Expressions;

/// <summary>Member-initializer transformations and update operations for reusable expressions.</summary>
/// <remarks>Member-binding operations validate the expression shape at runtime; arbitrary lambdas cannot be mapped into an existing object.</remarks>
public static class MemberInitializationExtensions
{
    /// <summary>Merges member-initializer bindings, with later bindings taking precedence.</summary>
    public static Expr<TSource, TResult> MergeBindings<TSource, TResult>(
        this IExpr<TSource, TResult> expression, IExpr<TSource, TResult> other) =>
        expression.MergeBindings(other, BindingConflictBehavior.UseLast);

    /// <summary>Merges member-initializer bindings using the specified duplicate-member policy.</summary>
    public static Expr<TSource, TResult> MergeBindings<TSource, TResult>(
        this IExpr<TSource, TResult> expression, IExpr<TSource, TResult> other,
        BindingConflictBehavior conflictBehavior)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (other == null) throw new ArgumentNullException(nameof(other));
        return ProjectionBindingMerger.Merge(expression, other, conflictBehavior);
    }

    /// <summary>Merges member-initializer bindings from a lambda, with later bindings taking precedence.</summary>
    public static Expr<TSource, TResult> MergeBindings<TSource, TResult>(
        this IExpr<TSource, TResult> expression, Expression<Func<TSource, TResult>> other) =>
        expression.MergeBindings(other, BindingConflictBehavior.UseLast);

    /// <summary>Merges member-initializer bindings from a lambda using the specified duplicate-member policy.</summary>
    public static Expr<TSource, TResult> MergeBindings<TSource, TResult>(
        this IExpr<TSource, TResult> expression, Expression<Func<TSource, TResult>> other,
        BindingConflictBehavior conflictBehavior)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (other == null) throw new ArgumentNullException(nameof(other));
        return expression.MergeBindings(Expr<TSource, TResult>.Create(other), conflictBehavior);
    }

    /// <summary>Builds a cached update expression for an existing reference-type result.</summary>
    public static Expression<Action<TSource, TResult>> GetMapToExistingExpression<TSource, TResult>(
        this IExpr<TSource, TResult> expression) where TResult : class
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        return UpdateCache<TSource, TResult>.Get(expression).Expression.Value;
    }

    /// <summary>Compiles and caches the action updating an existing reference-type result.</summary>
    public static Action<TSource, TResult> GetMapToExistingAction<TSource, TResult>(
        this IExpr<TSource, TResult> expression) where TResult : class
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        return UpdateCache<TSource, TResult>.Get(expression).Action.Value;
    }

    /// <summary>Creates the result if null, otherwise updates its member bindings in place.</summary>
    public static void MapToExisting<TSource, TResult>(
        this IExpr<TSource, TResult> expression, TSource source, ref TResult? destination)
        where TResult : class
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (destination is null)
        {
            destination = expression.Invoke(source);
            return;
        }

        expression.GetMapToExistingAction()(source, destination);
    }

    private static class UpdateCache<TSource, TResult> where TResult : class
    {
        // No mapping-specific state or lock is allocated for ordinary expressions.
        private static readonly ConditionalWeakTable<IExpr<TSource, TResult>, UpdateState<TSource, TResult>> Table = new();

        internal static UpdateState<TSource, TResult> Get(IExpr<TSource, TResult> expression) =>
            Table.GetValue(expression, key => new UpdateState<TSource, TResult>(key));
    }

    private sealed class UpdateState<TSource, TResult> where TResult : class
    {
        internal readonly Lazy<Expression<Action<TSource, TResult>>> Expression;
        internal readonly Lazy<Action<TSource, TResult>> Action;

        internal UpdateState(IExpr<TSource, TResult> expression)
        {
            Expression = new Lazy<Expression<Action<TSource, TResult>>>(
                () => MapToExistingBuilder.Build(ExprAdapter.GetExpandedExpression(expression)));
            Action = new Lazy<Action<TSource, TResult>>(() => Expression.Value.Compile());
        }
    }
}
