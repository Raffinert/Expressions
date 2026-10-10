using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

internal sealed class QueryExecutionState
{
    // The context scope is never shared across DbContexts, and this does not prolong
    // the lifetime of an execution's parameters or any user-captured DbContext.
    private WeakReference<QueryContext>? _context;
    private ConditionalWeakTable<QueryContext, Preparation> _preparations = new();

    public void SetContext(QueryContext context)
    {
        _context = new(context);
        _preparations = new(); // Invalidates hit, failure/cancellation and pooled-lease state.
    }

    public Expression Prepare(Expression query)
    {
        if (_context == null || !_context.TryGetTarget(out var context)) return EfQueryExpansion.Expand(query);
        var resolved = new WrapperParameterVisitor(EfRuntimeParameters.Values(context)).Visit(query)!;
        var prepared = EfQueryExpansion.Expand(resolved, context);
        if (!_context.TryGetTarget(out var active) || !ReferenceEquals(context, active))
            throw new InvalidOperationException("Reentrant Raffinert query preparation is unsupported.");
        if (!ReferenceEquals(query, prepared)) _preparations.Add(context, new(new(query), prepared));
        return prepared;
    }

    public Expression ForCompilation(Expression query, DbContext? owner)
    {
        if (_context != null && _context.TryGetTarget(out var context) && _preparations.TryGetValue(context, out var preparation))
        {
            if (!ReferenceEquals(context.Context, owner) || !preparation.Original.TryGetTarget(out var original))
                throw new InvalidOperationException("Raffinert query preparation does not match this execution.");
            var replacement = new PreparedExpressionVisitor(original, preparation.Expression);
            var result = replacement.Visit(query)!;
            _preparations.Remove(context);
            if (!replacement.Found)
                throw new InvalidOperationException("Another query interceptor replaced the prepared Raffinert tree. Register Raffinert before interceptors that rewrite existing nodes.");
            return result;
        }
        // Explicit compiled queries bypass ordinary cache-key preparation.
        return EfQueryExpansion.Expand(query);
    }

    private sealed record Preparation(WeakReference<Expression> Original, Expression Expression);
    private sealed class PreparedExpressionVisitor(Expression original, Expression prepared) : ExpressionVisitor
    {
        public bool Found { get; private set; }
        public override Expression? Visit(Expression? node)
        {
            if (!ReferenceEquals(node, original)) return base.Visit(node);
            Found = true;
            return prepared;
        }
    }

    private sealed class WrapperParameterVisitor(IReadOnlyDictionary<string, object?> values) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node != null && (typeof(IExpressionExpansionSource).IsAssignableFrom(node.Type) ||
                                 (node.Type.IsGenericType && node.Type.GetGenericTypeDefinition() == typeof(IComposableExpression<,>)) ||
                                 typeof(Delegate).IsAssignableFrom(node.Type)))
            {
                var name = EfRuntimeParameters.Name(node);
                if (name != null && values.TryGetValue(name, out var value) &&
                    node.Type.IsInstanceOfType(value) &&
                    (value is IExpressionExpansionSource || value is Delegate { Target: IExpressionExpansionSource }))
                    return Expression.Constant(value, node.Type);
            }
            return base.Visit(node);
        }
    }
}
