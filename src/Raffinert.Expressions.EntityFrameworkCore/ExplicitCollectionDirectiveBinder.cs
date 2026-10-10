using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

// Binds one already-materialized capture. EF owns enumeration, SQL expansion and mode.
internal static class ExplicitCollectionDirectiveBinder
{
    public static bool IsSupportedType(Type type) => type == typeof(int[]) || type == typeof(List<int>) ||
        type == typeof(int?[]) || type == typeof(string[]);

    public static bool ValidateCapture(Expression operand)
    {
        if (!IsSupportedType(operand.Type) || operand is not MemberExpression) return false;
        Expression? current = operand;
        var depth = 0;
        while (current is MemberExpression member)
        {
            if (++depth > 64) return false;
            if (member.Member is not FieldInfo && member.Member is not PropertyInfo { GetMethod: not null }) return false;
            if (member.Member is PropertyInfo property && property.GetIndexParameters().Length != 0) return false;
            if (member.Expression == null) return member.Member is FieldInfo;
            current = member.Expression;
        }
        return current is ConstantExpression;
    }

    public static Expression Bind(Expression operand, QueryContext? context, RaffinertParameterNameGenerator names)
    {
        if (!IsSupportedType(operand.Type)) throw Unsupported();
        if (operand is QueryParameterExpression) return operand;
        if (!ValidateCapture(operand)) throw Unsupported();
        if (context == null)
            throw new NotSupportedException("Explicit collection directives require ordinary query preparation.");

        object? value;
        bool evaluated;
        try { evaluated = SafeValueEvaluator.TryEvaluate(operand, out value); }
        catch (Exception)
        {
            throw new InvalidOperationException("Unable to read an explicit collection directive capture. Getter failure details are not included in diagnostics.");
        }
        if (!evaluated || (value != null && value.GetType() != operand.Type)) throw Unsupported();
        var name = names.Next((MemberExpression)operand);
        var parameter = EfRuntimeParameters.Create(operand.Type, name);
        EfRuntimeParameters.Add(context, name, value);
        return parameter;
    }

    public static NotSupportedException Unsupported() => new(
        "This explicit collection directive operand is unsupported inside a Raffinert wrapper. Use a supported closed array or List<int> capture; computed, row-dependent and lazy collections are not evaluated.");
}
