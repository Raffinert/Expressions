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
            if (node.Object == null || !IsInvocationMarker(node.Method, node.Object.Type))
            {
                // Resolve delegate-typed arguments to preserve callback expansion.
                if (node.Arguments.Any(argument => typeof(Delegate).IsAssignableFrom(argument.Type)))
                    return node.Update(Visit(node.Object), node.Arguments.Select(VisitDelegateOperand));
                return base.VisitMethodCall(node);
            }

            if (!SafeValueEvaluator.TryEvaluate(node.Object, out var value) || value == null)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve expression instance for invocation marker '{node.Method.DeclaringType?.FullName}.{node.Method.Name}'. " +
                    "Only constant, closure-rooted member, static member, and direct constructor targets can be expanded.");
            }

            if (value is not IExpr expansionSource)
            {
                throw new NotSupportedException("Invocation marker target must implement IExpr.");
            }

            if (node.Arguments.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Invocation marker '{node.Method.Name}' must have exactly one argument.");
            }

            var argument = Visit(node.Arguments[0])!;
            var inner = ExpandNested(expansionSource);
            if (inner.Parameters.Count != 1 ||
                !inner.Parameters[0].Type.IsAssignableFrom(argument.Type) ||
                !node.Type.IsAssignableFrom(inner.ReturnType))
            {
                throw new InvalidOperationException(
                    $"Expression instance '{value.GetType().FullName}' returned an incompatible lambda for " +
                    $"the '{node.Method.Name}' invocation marker.");
            }

            var boundArgument = inner.Parameters[0].Type == argument.Type
                ? argument
                : Expression.Convert(argument, inner.Parameters[0].Type);
            var body = new ReplaceExpressionVisitor(inner.Parameters[0], boundArgument).Visit(inner.Body)!;

            if (node.Method.Name == nameof(Expr<,>.InvokeOrDefault) && CanBeNull(argument.Type))
            {
                body = Expression.Condition(
                    Expression.Equal(argument, Expression.Default(argument.Type)),
                    Expression.Default(body.Type),
                    body);
            }

            if (body.Type != node.Type)
                body = Expression.Convert(body, node.Type);

            return Visit(body)!;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Convert &&
                node.Operand is MethodCallExpression call &&
                call.Method.Name == nameof(Delegate.CreateDelegate) &&
                call.Arguments.Count >= 2 &&
                call.Object is ConstantExpression { Value: MethodInfo method } &&
                TryResolveMethodGroup(call.Arguments[1], method, node.Type, out var lambda))
            {
                return lambda;
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            return TryExpandDelegate(node.Value, node.Type, out var expression) ? expression : base.VisitConstant(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            // A final field can still have a getter/constructor in its receiver chain.
            if (IsFieldOnlyAccess(node) && typeof(Delegate).IsAssignableFrom(node.Type) &&
                SafeValueEvaluator.TryEvaluate(node, out var value) && TryExpandDelegate(value, node.Type, out var expression))
            {
                return expression;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitInvocation(InvocationExpression node) =>
            node.Update(VisitDelegateOperand(node.Expression), Visit(node.Arguments));

        private Expression VisitDelegateOperand(Expression node)
        {
            // Opaque receiver chains must be read to identify a marker only when used as callbacks.
            // Merely carrying one leaves its evaluation to the eventual execution provider.
            if (node is MemberExpression && !IsFieldOnlyAccess(node) && typeof(Delegate).IsAssignableFrom(node.Type) &&
                SafeValueEvaluator.TryEvaluate(node, out var value) && TryExpandDelegate(value, node.Type, out var expression))
                return expression;
            return Visit(node)!;
        }

        private static bool IsFieldOnlyAccess(Expression? node) => node switch
        {
            null or ConstantExpression => true,
            MemberExpression { Member: FieldInfo } member => IsFieldOnlyAccess(member.Expression),
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion =>
                IsFieldOnlyAccess(conversion.Operand),
            _ => false
        };

        private bool TryResolveMethodGroup(
            Expression targetExpression, MethodInfo method, Type delegateType, out LambdaExpression expression)
        {
            // Identify the marker from static types BEFORE evaluating the receiver.
            // Ordinary method-group receivers may contain getters with side effects.
            if (!IsInvocationMarker(method, targetExpression.Type))
            {
                expression = null!;
                return false;
            }

            if (SafeValueEvaluator.TryEvaluate(targetExpression, out var target) && target is IExpr expansionSource)
            {
                expression = ExprAdapter.AdaptToDelegate(ExpandNested(expansionSource), delegateType);
                return true;
            }

            expression = null!;
            return false;
        }

        private bool TryExpandDelegate(object? value, Type delegateType, out LambdaExpression expression)
        {
            if (value is Delegate @delegate &&
                @delegate.Target is IExpr expansionSource &&
                (IsInvocationMarker(@delegate.Method) ||
                 ImplementsInvocationMarker(@delegate.Method, expansionSource.GetType())))
            {
                expression = ExprAdapter.AdaptToDelegate(ExpandNested(expansionSource), delegateType);
                return true;
            }

            expression = null!;
            return false;
        }

        private LambdaExpression ExpandNested(IExpr expression)
        {
            // Native wrappers own their expanded-tree cache. External implementations expose
            // an expanded lambda via IExpr but still need visiting and cycle detection.
            var native = expression as IExpressionExpansionSource;
            var cached = native?.GetCachedExpandedExpression();
            if (cached != null) return cached;

            if (!expansionStack.Add(expression))
            {
                throw new InvalidOperationException(
                    $"Expression composition cycle detected while expanding '{expression.GetType().FullName}'.");
            }

            try
            {
                var source = native is not null
                    ? native.GetExpression()
                    : expression.GetExpandedLambdaExpression();
                if (source is null)
                {
                    throw new InvalidOperationException(
                        $"Expression instance '{expression.GetType().FullName}' returned a null lambda.");
                }

                var expanded = (LambdaExpression)Visit(source)!;
                return native?.CacheExpandedExpression(expanded) ?? expanded;
            }
            finally
            {
                expansionStack.Remove(expression);
            }
        }

        // Captured method groups can point to the concrete external implementation
        // rather than IExpr<TSource, TResult>.Invoke. Only expand methods actually
        // mapped to that interface; unrelated delegates on the same object stay intact.
        private static bool ImplementsInvocationMarker(MethodInfo method, Type implementationType)
        {
            foreach (var contract in implementationType.GetInterfaces())
            {
                if (!contract.IsGenericType ||
                    contract.GetGenericTypeDefinition() != typeof(IExpr<,>))
                    continue;

                var mapping = implementationType.GetInterfaceMap(contract);
                for (var i = 0; i < mapping.InterfaceMethods.Length; i++)
                {
                    if (IsInvocationMarker(mapping.InterfaceMethods[i]) &&
                        mapping.TargetMethods[i].GetBaseDefinition() == method.GetBaseDefinition())
                        return true;
                }
            }

            return false;
        }

        // Recognize concrete implementations without evaluating the receiver:
        // non-marker methods (including ordinary Invoke methods) must retain their
        // usual side effects and evaluation timing.
        private static bool IsInvocationMarker(MethodInfo method, Type receiverType) =>
            IsInvocationMarker(method) ||
            (!receiverType.IsInterface &&
             typeof(IExpr).IsAssignableFrom(receiverType) &&
             ImplementsInvocationMarker(method, receiverType));

        private static bool IsInvocationMarker(MethodInfo method)
        {
            var declaringType = method.DeclaringType;
            if (declaringType == null || !declaringType.IsGenericType) return false;
            var contract = declaringType.GetGenericTypeDefinition();
            return (contract == typeof(Expr<,>) || contract == typeof(IExpr<,>)) &&
                (method.Name == nameof(Expr<,>.Invoke) ||
                 method.Name == nameof(Expr<,>.InvokeOrDefault)) &&
                method == declaringType.GetMethod(method.Name);
        }

        private static bool CanBeNull(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }
    }
}
