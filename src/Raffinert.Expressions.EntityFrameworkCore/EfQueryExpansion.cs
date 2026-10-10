using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace Raffinert.Expressions;

internal static class EfQueryExpansion
{
    public static Expression Expand(Expression expression, QueryContext? context = null)
    {
        try
        {
            var expanded = ExpressionExpander.Expand(expression);
            return ReferenceEquals(expression, expanded) ? expression : new RuntimeCaptureVisitor(context).Visit(expanded)!;
        }
        catch (InvalidOperationException error) when (error.InnerException != null)
        {
            throw new InvalidOperationException("Unable to read closure member while preparing a Raffinert query. Getter and constructor failures are not included in diagnostics.");
        }
    }

    private sealed class RuntimeCaptureVisitor(QueryContext? context) : ExpressionVisitor
    {
        private readonly HashSet<string> _names = context == null ? [] : new(EfRuntimeParameters.Values(context).Keys, StringComparer.Ordinal);
        private int _next;
        protected override Expression VisitDefault(DefaultExpression node)
        {
            // EF normally folds these before compilation; late expansion introduces new defaults.
            if (!node.Type.IsValueType || Nullable.GetUnderlyingType(node.Type) != null)
                return Expression.Constant(null, node.Type);
            return IsScalar(node.Type)
                ? Expression.Constant(Activator.CreateInstance(node.Type), node.Type)
                : base.VisitDefault(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (!IsCapture(node)) return base.VisitMember(node);
            var type = Nullable.GetUnderlyingType(node.Type) ?? node.Type;
            if (!IsScalar(type))
                throw new NotSupportedException(node.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(node.Type)
                    ? "Captured collections inside invocation markers are unsupported. Pass the wrapper directly to an operator so EF can extract the collection."
                    : "This captured type is unsupported inside invocation markers. Pass the wrapper directly to an operator.");
            if (context == null)
                throw new NotSupportedException("Runtime captures inside standalone interception or explicitly compiled wrappers are unsupported. Use UseRaffinertExpressions with ordinary LINQ, or direct operators/scalar compiled-query parameters.");
            if (!SafeValueEvaluator.TryEvaluate(node, out var value)) return base.VisitMember(node);
            string name;
            do name = EfRuntimeParameters.Prefix + _next++;
            while (!_names.Add(name));
            var parameter = EfRuntimeParameters.Create(node.Type, name);
            EfRuntimeParameters.Add(context, name, value);
            return parameter;
        }

        private static bool IsCapture(Expression? node)
        {
            while (node is MemberExpression member)
            {
                if (member.Expression == null) return member.Member is FieldInfo;
                node = member.Expression;
                while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
                    node = conversion.Operand;
            }
            return node is ConstantExpression or NewExpression;
        }

        private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum ||
            type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
            type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
}
