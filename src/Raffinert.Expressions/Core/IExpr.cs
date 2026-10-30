using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Provides a variant view of a reusable composable expression.</summary>
/// <typeparam name="TSource">The type of value supplied to the expression.</typeparam>
/// <typeparam name="TResult">The type of value produced by the expression.</typeparam>
/// <summary>Provides a non-generic expansion contract for composable expressions.</summary>
/// <remarks>
/// This contract also allows custom <see cref="IExpr{TSource, TResult}"/> implementations
/// to be expanded when their Invoke methods appear inside another expression tree.
/// </remarks>
public interface IExpr
{
    /// <summary>Returns a one-parameter, fully expanded lambda representing this expression.</summary>
    LambdaExpression GetExpandedLambdaExpression();
}

/// <summary>A typed reusable expression that can be evaluated or expanded.</summary>
public interface IExpr<TSource, out TResult> : IExpr
{
    /// <summary>Evaluates the expression for the specified value.</summary>
    /// <param name="value">The source value to evaluate.</param>
    /// <returns>The result of evaluating the expression.</returns>
    TResult Invoke(TSource value);

}
