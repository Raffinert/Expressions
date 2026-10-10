using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class ExpressionExpander
{
    public static Expression<TDelegate> Expand<TDelegate>(Expression<TDelegate> expression)
        where TDelegate : Delegate
        => (Expression<TDelegate>)Expand((Expression)expression);

    internal static Expression Expand(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var stack = new HashSet<object>(ReferenceIdentityComparer.Instance);
        return new Visitor(stack).Visit(expression)!;
    }

    public static Expression<TDelegate> Expand<TDelegate>(
        IExpressionExpansionSource owner,
        Expression<TDelegate> expression)
        where TDelegate : Delegate
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var stack = new HashSet<object>(ReferenceIdentityComparer.Instance) { owner };
        return (Expression<TDelegate>)new Visitor(stack).Visit(expression)!;
    }

    private sealed class Visitor(HashSet<object> expansionStack) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!IsInvocationMarker(node.Method) || node.Object == null)
            {
                return base.VisitMethodCall(node);
            }

            if (!SafeValueEvaluator.TryEvaluate(node.Object, out var value) || value == null)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve expression instance for invocation marker '{node.Method.DeclaringType?.FullName}.{node.Method.Name}'. " +
                    "Only constant, closure-rooted member, static member, and direct constructor targets can be expanded.");
            }

            if (value is not IExpressionExpansionSource expansionSource)
            {
                throw new NotSupportedException(
                    "Invocation marker expansion requires a wrapper derived from ComposableExpression<TSource, TResult>. " +
                    "For external IComposableExpression implementations, pass the wrapper directly to an operator.");
            }

            if (node.Arguments.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Invocation marker '{node.Method.Name}' must have exactly one argument.");
            }

            var argument = Visit(node.Arguments[0])!;
            var inner = ExpandNested(expansionSource);
            if (inner.Parameters.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expression instance '{value.GetType().FullName}' must expose a one-parameter lambda.");
            }

            var body = new ReplaceExpressionVisitor(inner.Parameters[0], argument).Visit(inner.Body)!;

            if (node.Method.Name == nameof(ComposableExpression<,>.InvokeOrDefault) && CanBeNull(argument.Type))
            {
                body = Expression.Condition(
                    Expression.Equal(argument, Expression.Default(argument.Type)),
                    Expression.Default(body.Type),
                    body);
            }

            return Visit(body)!;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Convert &&
                node.Operand is MethodCallExpression call &&
                call.Method.Name == nameof(Delegate.CreateDelegate) &&
                call.Arguments.Count >= 2 &&
                TryResolveMethodGroup(call.Arguments[1], out var lambda))
            {
                return lambda;
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            return TryExpandDelegate(node.Value, out var expression) ? expression : base.VisitConstant(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (SafeValueEvaluator.TryEvaluate(node, out var value) && TryExpandDelegate(value, out var expression))
            {
                return expression;
            }

            return base.VisitMember(node);
        }

        private bool TryResolveMethodGroup(Expression targetExpression, out LambdaExpression expression)
        {
            if (SafeValueEvaluator.TryEvaluate(targetExpression, out var target) && target is IExpressionExpansionSource expansionSource)
            {
                expression = ExpandNested(expansionSource);
                return true;
            }

            expression = null!;
            return false;
        }

        private bool TryExpandDelegate(object? value, out LambdaExpression expression)
        {
            if (value is Delegate @delegate &&
                @delegate.Target is IExpressionExpansionSource expansionSource &&
                IsInvocationMarker(@delegate.Method))
            {
                expression = ExpandNested(expansionSource);
                return true;
            }

            expression = null!;
            return false;
        }

        private LambdaExpression ExpandNested(IExpressionExpansionSource expansionSource)
        {
            var cached = expansionSource.GetCachedExpandedExpression();
            if (cached != null) return cached;

            if (!expansionStack.Add(expansionSource))
            {
                throw new InvalidOperationException(
                    $"Expression composition cycle detected while expanding '{expansionSource.GetType().FullName}'.");
            }

            try
            {
                var expanded = (LambdaExpression)Visit(expansionSource.GetExpression())!;
                return expansionSource.CacheExpandedExpression(expanded);
            }
            finally
            {
                expansionStack.Remove(expansionSource);
            }
        }

        private static bool IsInvocationMarker(MethodInfo method)
        {
            var declaringType = method.DeclaringType;
            if (declaringType == null || !declaringType.IsGenericType) return false;
            var contract = declaringType.GetGenericTypeDefinition();
            return (contract == typeof(ComposableExpression<,>) || contract == typeof(IComposableExpression<,>)) &&
                (method.Name == nameof(ComposableExpression<,>.Invoke) ||
                 method.Name == nameof(ComposableExpression<,>.InvokeOrDefault)) &&
                method == declaringType.GetMethod(method.Name);
        }

        private static bool CanBeNull(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }
    }
}
