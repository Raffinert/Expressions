using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

internal sealed class QueryExecutionState
{
    // These PUBLIC properties were renamed in EF 10. Resolve them at runtime so the
    // EF 7-compiled adapter remains usable with the tested EF 8/9/10 assemblies.
    private static readonly PropertyInfo ParametersProperty =
        typeof(QueryContext).GetProperty("Parameters") ?? typeof(QueryContext).GetProperty("ParameterValues")
        ?? throw new NotSupportedException("This EF Core version does not expose query parameter values.");
    private static readonly Type? QueryParameterType = typeof(QueryContext).Assembly.GetType("Microsoft.EntityFrameworkCore.Query.QueryParameterExpression");
    private static readonly PropertyInfo? ParameterNameProperty = QueryParameterType?.GetProperty("Name");

    // The context scope is never shared across DbContexts, and this does not prolong
    // the lifetime of an execution's parameters or any user-captured DbContext.
    private WeakReference<QueryContext>? _context;

    public void SetContext(QueryContext context) => _context = new(context);

    public Expression ResolveWrappers(Expression query)
    {
        if (_context == null || !_context.TryGetTarget(out var context)) return query;
        var values = (IReadOnlyDictionary<string, object?>)ParametersProperty.GetValue(context)!;
        return new WrapperParameterVisitor(values).Visit(query)!;
    }

    private sealed class WrapperParameterVisitor(IReadOnlyDictionary<string, object?> values) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node != null && typeof(IExpressionExpansionSource).IsAssignableFrom(node.Type))
            {
                var name = node is ParameterExpression parameter ? parameter.Name
                    : QueryParameterType?.IsInstanceOfType(node) == true ? (string?)ParameterNameProperty!.GetValue(node)
                    : null;
                if (name != null && values.TryGetValue(name, out var value))
                    return Expression.Constant(value, node.Type);
            }
            return base.Visit(node);
        }
    }
}
