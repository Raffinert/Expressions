using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class EfQueryExpansion
{
    public static Expression Expand(Expression expression)
    {
        var expanded = ExpressionExpander.Expand(expression);
        // EF already extracted parameters. Only newly inlined, closure-rooted scalar members
        // need snapshotting; normal queries retain their original EF parameterization.
        return ReferenceEquals(expression, expanded) ? expression : new ScalarSnapshotVisitor().Visit(expanded)!;
    }

    private sealed class ScalarSnapshotVisitor : ExpressionVisitor
    {
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
            if (node.Type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(node.Type) &&
                SafeValueEvaluator.TryEvaluate(node, out _))
            {
                throw new NotSupportedException(
                    "Captured collections inside invocation markers are unsupported in constant-snapshot mode. " +
                    "Pass the wrapper directly to Where(condition) or an async condition operator so EF can extract the collection.");
            }
            var type = Nullable.GetUnderlyingType(node.Type) ?? node.Type;
            if (IsScalar(type) &&
                // Preserve provider translations such as DateTime.Now / DateTime.UtcNow.
                (node.Expression != null || node.Member is FieldInfo) &&
                SafeValueEvaluator.TryEvaluate(node, out var value))
            {
                return value == null
                    ? Expression.Constant(null, node.Type)
                    : Expression.Convert(Expression.Constant(value, type), node.Type);
            }
            return base.VisitMember(node);
        }

        private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum ||
            type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
            type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
}
