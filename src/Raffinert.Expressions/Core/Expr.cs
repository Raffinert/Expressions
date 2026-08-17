using System.Diagnostics;
using System.Linq.Expressions;

namespace Raffinert.Expressions;

/// <summary>Represents a reusable, executable expression from <typeparamref name="TIn"/> to <typeparamref name="TOut"/>.</summary>
/// <remarks>
/// Implementations must return an expression that remains stable for the lifetime of the instance once expansion or compilation is requested.
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[DebuggerTypeProxy(typeof(ExpressionDebugView))]
public abstract class Expr<TIn, TOut> : IExpandableExpression
{
    private readonly object _cacheLock = new();
    private Expression<Func<TIn, TOut>>? _expandedExpression;
    private Func<TIn, TOut>? _compiledExpression;

    /// <summary>Gets the expression represented by this instance.</summary>
    public abstract Expression<Func<TIn, TOut>> GetExpression();

    /// <summary>Gets a cached expression in which all Raffinert invocation markers have been inlined.</summary>
    public Expression<Func<TIn, TOut>> GetExpandedExpression()
    {
        if (_expandedExpression != null) return _expandedExpression;

        lock (_cacheLock)
        {
            return _expandedExpression ??= ExpressionExpander.Expand(this, GetExpression());
        }
    }

    /// <summary>Executes the expression for one value.</summary>
    /// <remarks>Inside another Raffinert expression, this method is a composition marker and is inlined during expansion.</remarks>
    public TOut Invoke(TIn value)
    {
        if (_compiledExpression != null) return _compiledExpression(value);

        lock (_cacheLock)
        {
            _compiledExpression ??= GetExpandedExpression().Compile();
        }

        return _compiledExpression(value);
    }

    /// <summary>Returns the default output for a null input; otherwise executes the expression.</summary>
    /// <remarks>
    /// Inside another Raffinert expression, this method is expanded to a provider-friendly conditional expression.
    /// The fallback is <c>default(TOut)</c>, which is not necessarily null for value-type outputs.
    /// </remarks>
    public TOut? InvokeOrDefault(TIn? value) => value is null ? default : Invoke(value);

    LambdaExpression IExpandableExpression.GetExpressionUntyped() => GetExpression();

    LambdaExpression? IExpandableExpression.GetCachedExpandedExpressionUntyped() => _expandedExpression;

    LambdaExpression IExpandableExpression.CacheExpandedExpressionUntyped(LambdaExpression expression)
    {
        var typed = (Expression<Func<TIn, TOut>>)expression;
        Interlocked.CompareExchange(ref _expandedExpression, typed, null);
        return _expandedExpression!;
    }

    private string DebuggerDisplay => GetExpandedExpression().ToString();
}
