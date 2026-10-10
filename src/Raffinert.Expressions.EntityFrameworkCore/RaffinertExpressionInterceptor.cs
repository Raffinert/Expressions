using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffinert.Expressions;

/// <summary>Expands Raffinert invocation markers before EF Core translates a query.</summary>
/// <remarks>Use UseRaffinertExpressions to also preserve captured wrappers and expanded query cache keys.</remarks>
public sealed class RaffinertExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <summary>Gets a stateless interceptor that can be shared across contexts.</summary>
    public static RaffinertExpressionInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        var services = (eventData.Context as IInfrastructure<IServiceProvider>)?.Instance;
        var state = services?.GetService(typeof(QueryExecutionState)) as QueryExecutionState;
        return EfQueryExpansion.Expand(state?.ResolveWrappers(queryExpression) ?? queryExpression);
    }
}
