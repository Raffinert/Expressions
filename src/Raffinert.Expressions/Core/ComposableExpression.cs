using System.Diagnostics;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable expression that can be composed with other expressions and evaluated.</summary>
/// <typeparam name="TSource">The type of value supplied to the expression.</typeparam>
/// <typeparam name="TResult">The type of value produced by the expression.</typeparam>
/// <remarks>
/// After expansion or evaluation is first requested, implementations must continue to return an expression
/// with equivalent behavior for the lifetime of the instance.
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[DebuggerTypeProxy(typeof(ExpressionDebugView))]
public abstract class ComposableExpression<TSource, TResult> : IExpressionExpansionSource
{
    private readonly object _cacheLock = new();
    private Expression<Func<TSource, TResult>>? _expandedExpression;
    private Func<TSource, TResult>? _compiledExpression;

    /// <summary>Returns the expression represented by this instance.</summary>
    /// <returns>The expression from a source value to a result value.</returns>
    public abstract Expression<Func<TSource, TResult>> GetExpression();

    /// <summary>Returns the expression with all nested composition calls inlined.</summary>
    /// <returns>An expanded expression containing no calls to this library's composition methods.</returns>
    /// <remarks>The expanded expression is created once and cached for the lifetime of this instance.</remarks>
    public Expression<Func<TSource, TResult>> GetExpandedExpression()
    {
        if (_expandedExpression != null) return _expandedExpression;

        lock (_cacheLock)
        {
            return _expandedExpression ??= ExpressionExpander.Expand(this, GetExpression());
        }
    }

    /// <summary>Evaluates the expression for the specified value.</summary>
    /// <param name="value">The source value to evaluate.</param>
    /// <returns>The result of evaluating the expression.</returns>
    /// <remarks>
    /// The expanded expression is compiled on first use and then cached. When this call appears in another
    /// composable expression tree, <see cref="GetExpandedExpression"/> replaces it with the referenced expression body.
    /// </remarks>
    public TResult Invoke(TSource value)
    {
        if (_compiledExpression != null) return _compiledExpression(value);

        lock (_cacheLock)
        {
            _compiledExpression ??= GetExpandedExpression().Compile();
        }

        return _compiledExpression(value);
    }

    /// <summary>Returns the default result for a null input; otherwise evaluates the expression.</summary>
    /// <param name="value">The source value to evaluate, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="default"/>(<typeparamref name="TResult"/>) when <paramref name="value"/> is null;
    /// otherwise, the result of evaluating the expression.
    /// </returns>
    /// <remarks>
    /// When this call appears in another composable expression tree, expansion replaces it with a conditional expression.
    /// The default result is not necessarily null; for example, it is zero for numeric value types and false for Boolean values.
    /// </remarks>
    public TResult? InvokeOrDefault(TSource? value) => value is null ? default : Invoke(value);

    LambdaExpression IExpressionExpansionSource.GetExpression() => GetExpression();

    LambdaExpression? IExpressionExpansionSource.GetCachedExpandedExpression() => _expandedExpression;

    LambdaExpression IExpressionExpansionSource.CacheExpandedExpression(LambdaExpression expression)
    {
        var typed = (Expression<Func<TSource, TResult>>)expression;
        Interlocked.CompareExchange(ref _expandedExpression, typed, null);
        return _expandedExpression!;
    }

    private string DebuggerDisplay => GetExpandedExpression().ToString();
}
