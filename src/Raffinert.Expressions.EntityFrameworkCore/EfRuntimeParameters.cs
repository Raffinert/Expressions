using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

// Direct public EF Core 10 parameter contracts.
internal static class EfRuntimeParameters
{
    internal const string Prefix = RaffinertParameterNameGenerator.Prefix;
    public static IReadOnlyDictionary<string, object?> Values(QueryContext context) => context.Parameters;
    public static string? Name(Expression node) => node switch
    {
        QueryParameterExpression parameter => parameter.Name,
        ParameterExpression parameter => parameter.Name,
        _ => null
    };
    public static Expression Create(Type type, string name) => new QueryParameterExpression(name, type);
    public static void Add(QueryContext context, string name, object? value) => context.Parameters.Add(name, value);
}
