using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

// Original, deliberately narrow grammar. Interpretation is not a sandbox:
// approved captured property getters retain the existing user-code contract.
internal static class ComputedDirectiveOperandEvaluator
{
    public static bool Validate(Expression operand) => new Validator().Numeric(operand, 0);

    // Caller must validate the original tree and require an execution context first.
    public static object? EvaluateApproved(Expression operand)
    {
        var evaluate = Expression.Lambda<Func<object?>>(Expression.Convert(operand, typeof(object)))
            .Compile(preferInterpretation: true);
        try { return evaluate(); }
        catch (Exception)
        {
            throw new InvalidOperationException("Unable to evaluate a computed EF directive operand. Captured getter and arithmetic failure details are not included in diagnostics.");
        }
    }

    private static bool IsNumeric(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(short) || type == typeof(int) || type == typeof(long);
    }

    private sealed class Validator
    {
        private int _nodes;
        private bool WithinBounds(int depth) => depth <= 64 && ++_nodes <= 256;

        public bool Numeric(Expression expression, int depth)
        {
            if (!WithinBounds(depth) || !IsNumeric(expression.Type)) return false;
            return expression switch
            {
                ConstantExpression or DefaultExpression => true,
                MemberExpression member => Capture(member, depth + 1),
                BinaryExpression binary when binary.Method == null && binary.Conversion == null &&
                    binary.NodeType is ExpressionType.Add or ExpressionType.AddChecked or
                        ExpressionType.Subtract or ExpressionType.SubtractChecked or
                        ExpressionType.Multiply or ExpressionType.MultiplyChecked =>
                    Numeric(binary.Left, depth + 1) && Numeric(binary.Right, depth + 1),
                UnaryExpression unary when unary.Method == null &&
                    unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked =>
                    Numeric(unary.Operand, depth + 1),
                _ => false
            };
        }

        private bool Capture(Expression expression, int depth)
        {
            if (!WithinBounds(depth)) return false;
            if (expression is ConstantExpression) return true;
            if (expression is not MemberExpression member) return false;
            if (member.Member is not FieldInfo &&
                member.Member is not PropertyInfo { GetMethod: not null }) return false;
            if (member.Member is PropertyInfo indexed && indexed.GetIndexParameters().Length != 0) return false;
            // Static properties, constructors, conversions and method receivers are excluded.
            return member.Expression == null ? member.Member is FieldInfo : Capture(member.Expression, depth + 1);
        }
    }
}
