using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

// Only public EF contracts are reflected; the assembly is compiled against EF 7.
internal static class EfRuntimeParameters
{
    internal const string Prefix = "__raffinert_runtime_";
    private static readonly int Major = typeof(QueryContext).Assembly.GetName().Version!.Major;
    private static readonly PropertyInfo ValuesProperty = typeof(QueryContext).GetProperty(Major == 10 ? "Parameters" : "ParameterValues")
        ?? throw new NotSupportedException("This EF Core version does not expose public query parameter storage.");
    private static readonly Type? ParameterType = typeof(QueryContext).Assembly.GetType("Microsoft.EntityFrameworkCore.Query.QueryParameterExpression");
    private static readonly ConstructorInfo? ParameterConstructor = ParameterType?.GetConstructor(new[] { typeof(string), typeof(Type) });
    private static readonly MethodInfo? AddParameterMethod = typeof(QueryContext).GetMethod("AddParameter", new[] { typeof(string), typeof(object) });
    private static readonly PropertyInfo? NameProperty = ParameterType?.GetProperty("Name");

    public static IReadOnlyDictionary<string, object?> Values(QueryContext context) =>
        (IReadOnlyDictionary<string, object?>)ValuesProperty.GetValue(context)!;
    public static string? Name(Expression node) => node is ParameterExpression parameter ? parameter.Name
        : ParameterType?.IsInstanceOfType(node) == true ? (string?)NameProperty!.GetValue(node) : null;

    public static Expression Create(Type type, string name)
    {
        if (Major is >= 7 and <= 9 && ParameterType == null) return Expression.Parameter(type, name);
        if (Major == 10 && ParameterConstructor != null)
            return (Expression)ParameterConstructor.Invoke(new object[] { name, type });
        throw new NotSupportedException("Runtime parameter lifting requires a verified EF Core 7, 8, 9 or 10 parameter contract.");
    }
    public static void Add(QueryContext context, string name, object? value)
    {
        if (Major == 10) ((IDictionary<string, object?>)ValuesProperty.GetValue(context)!).Add(name, value);
        else if (Major is >= 7 and <= 9 && AddParameterMethod != null)
            AddParameterMethod.Invoke(context, new[] { name, value });
        else throw new NotSupportedException("Runtime parameter lifting requires a verified public EF parameter binding API.");
    }
}
