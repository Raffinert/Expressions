using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
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
        private static readonly MethodInfo ConstantDirective = ((MethodCallExpression)((Expression<Func<int, int>>)(value => EF.Constant(value))).Body).Method.GetGenericMethodDefinition();
        private static readonly MethodInfo ParameterDirective = ((MethodCallExpression)((Expression<Func<int, int>>)(value => EF.Parameter(value))).Body).Method.GetGenericMethodDefinition();
        private readonly RaffinertParameterNameGenerator _names = new(context == null ? [] : EfRuntimeParameters.Values(context).Keys);

        private static bool IsDirective(MethodInfo method) => method.IsGenericMethod &&
            (method.GetGenericMethodDefinition() == ConstantDirective || method.GetGenericMethodDefinition() == ParameterDirective);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!IsDirective(node.Method))
                return base.VisitMethodCall(node);

            // Validate computed operands before visiting or evaluating their children.
            var original = node.Arguments[0];
            if (original is not (ConstantExpression or DefaultExpression or QueryParameterExpression) &&
                !(original is MemberExpression capture && IsCapture(capture)))
            {
                if (!ComputedDirectiveOperandEvaluator.Validate(original)) throw UnsupportedDirectiveOperand();
                if (context == null)
                    throw new NotSupportedException("Computed EF directive operands require ordinary query preparation.");
                var computed = ComputedDirectiveOperandEvaluator.EvaluateApproved(original);
                var computedName = _names.NextPath("computed");
                var computedParameter = EfRuntimeParameters.Create(original.Type, computedName);
                EfRuntimeParameters.Add(context, computedName, computed);
                return node.Update(node.Object, [computedParameter]);
            }

            // Leave mode selection to EF's native normalizer. It requires a native
            // parameter operand even for a literal introduced by late expansion.
            var operand = Visit(node.Arguments[0]);
            if (operand is QueryParameterExpression)
                return node.Update(node.Object, [operand]);
            if (operand is not ConstantExpression literal || !IsScalar(Nullable.GetUnderlyingType(literal.Type) ?? literal.Type))
                throw UnsupportedDirectiveOperand();
            if (context == null)
                throw new NotSupportedException("EF directive literals inside standalone interception or explicitly compiled wrappers require ordinary query preparation.");
            var name = _names.NextPath("p");
            var parameter = EfRuntimeParameters.Create(literal.Type, name);
            EfRuntimeParameters.Add(context, name, literal.Value);
            return node.Update(node.Object, [parameter]);
        }

        private static NotSupportedException UnsupportedDirectiveOperand() => new(
            "This EF directive operand is unsupported inside a Raffinert wrapper. Use a supported scalar capture, literal or closed numeric computation; unsupported operands are not evaluated.");
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
            var name = _names.Next(node);
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
