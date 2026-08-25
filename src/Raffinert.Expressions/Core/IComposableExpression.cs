using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Provides a variant view of a reusable composable expression.</summary>
/// <typeparam name="TSource">The type of value supplied to the expression.</typeparam>
/// <typeparam name="TResult">The type of value produced by the expression.</typeparam>
public interface IComposableExpression<TSource, out TResult>
{
    /// <summary>Evaluates the expression for the specified value.</summary>
    /// <param name="value">The source value to evaluate.</param>
    /// <returns>The result of evaluating the expression.</returns>
    TResult Invoke(TSource value);

    /// <summary>Returns the expanded expression through a variant-safe, untyped view.</summary>
    /// <returns>An expanded lambda expression containing no composition calls.</returns>
    LambdaExpression GetExpandedLambdaExpression();
}
