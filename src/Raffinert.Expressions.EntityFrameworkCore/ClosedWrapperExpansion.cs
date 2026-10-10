using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Raffinert.Expressions;

// Compatibility path for standalone interception and EF compiled-query creation.
// Ordinary queries have already expanded before native extraction and leave this unchanged.
internal static class ClosedWrapperExpansion
{
    public static Expression Expand(Expression query)
    {
        try
        {
            var expanded = ExpressionExpander.Expand(query);
            return ReferenceEquals(query, expanded) ? query : new ClosedOperandVisitor().Visit(expanded)!;
        }
        catch (InvalidOperationException error) when (error.InnerException != null)
        {
            throw new InvalidOperationException("Unable to expand a Raffinert query. Getter and constructor failures are not included in diagnostics.");
        }
    }

    private sealed class ClosedOperandVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name is nameof(EF.Constant) or nameof(EF.Parameter))
                throw new NotSupportedException("EF directives inside standalone interception or explicitly compiled wrappers require ordinary query execution.");
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            Expression? root = node;
            while (root is MemberExpression member)
            {
                if (member.Expression == null)
                {
                    if (member.Member is System.Reflection.FieldInfo) throw UnsupportedCapture();
                    return base.VisitMember(node);
                }
                root = member.Expression;
                while (root is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
                    root = conversion.Operand;
            }
            if (root is ConstantExpression or NewExpression) throw UnsupportedCapture();
            return base.VisitMember(node);
        }

        private static NotSupportedException UnsupportedCapture() => new(
            "Runtime captures inside standalone interception or explicitly compiled wrappers are unsupported. Use ordinary LINQ or scalar compiled-query parameters.");

        protected override Expression VisitDefault(DefaultExpression node)
        {
            if (!node.Type.IsValueType || Nullable.GetUnderlyingType(node.Type) != null)
                return Expression.Constant(null, node.Type);
            var type = node.Type;
            return type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid) ||
                type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
                type == typeof(DateOnly) || type == typeof(TimeOnly)
                ? Expression.Constant(Activator.CreateInstance(type), type)
                : base.VisitDefault(node);
        }
    }
}
