using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffinert.Expressions;

/// <summary>Expands Raffinert invocation markers before EF Core translates a query.</summary>
/// <remarks>Use UseRaffinertExpressions for ordinary queries with runtime captures: it expands before EF's native extraction. Standalone interception and compiled wrappers require closed operands.</remarks>
public sealed class RaffinertExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <summary>Gets a stateless interceptor that can be shared across contexts.</summary>
    public static RaffinertExpressionInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        return ClosedWrapperExpansion.Expand(queryExpression);
    }
}
