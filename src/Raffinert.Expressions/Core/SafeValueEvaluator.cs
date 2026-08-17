using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class SafeValueEvaluator
{
    public static bool TryEvaluate(Expression? expression, out object? value)
    {
        expression = StripConversions(expression);

        switch (expression)
        {
            case null:
                value = null;
                return false;

            case ConstantExpression constant:
                value = constant.Value;
                return true;

            case DefaultExpression @default:
                value = @default.Type.IsValueType ? Activator.CreateInstance(@default.Type) : null;
                return true;

            case MemberExpression member:
                return TryEvaluateMember(member, out value);

            case NewExpression created:
                return TryEvaluateNew(created, out value);

            default:
                value = null;
                return false;
        }
    }

    private static bool TryEvaluateMember(MemberExpression expression, out object? value)
    {
        object? instance = null;
        if (expression.Expression != null && !TryEvaluate(expression.Expression, out instance))
        {
            value = null;
            return false;
        }

        try
        {
            switch (expression.Member)
            {
                case FieldInfo field:
                    value = field.GetValue(instance);
                    return true;
                case PropertyInfo property when property.GetIndexParameters().Length == 0 && property.GetMethod != null:
                    value = property.GetValue(instance, null);
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"Unable to read closure member '{expression.Member.DeclaringType?.FullName}.{expression.Member.Name}' while expanding an expression.",
                exception.InnerException ?? exception);
        }
    }

    private static bool TryEvaluateNew(NewExpression expression, out object? value)
    {
        var arguments = new object?[expression.Arguments.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryEvaluate(expression.Arguments[index], out arguments[index]))
            {
                value = null;
                return false;
            }
        }

        try
        {
            value = expression.Constructor != null
                ? expression.Constructor.Invoke(arguments)
                : Activator.CreateInstance(expression.Type);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"Unable to construct expression instance of type '{expression.Type.FullName}' while expanding an invocation marker.",
                exception.InnerException ?? exception);
        }
    }

    private static Expression? StripConversions(Expression? expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.TypeAs))
        {
            expression = unary.Operand;
        }

        return expression;
    }
}
