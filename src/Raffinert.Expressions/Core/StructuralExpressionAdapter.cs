using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class StructuralExpressionAdapter
{
    public static Expression<Func<TNewSource, TResult>> AdaptSource<TSource, TResult, TNewSource>(
        Expression<Func<TSource, TResult>> expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var oldParameter = expression.Parameters[0];
        var newParameter = Expression.Parameter(typeof(TNewSource), oldParameter.Name);
        var body = new SourceVisitor(oldParameter, newParameter).Visit(expression.Body)!;
        return Expression.Lambda<Func<TNewSource, TResult>>(body, newParameter);
    }

    public static Expression<Func<TSource, TNewResult>> AdaptResult<TSource, TResult, TNewResult>(
        Expression<Func<TSource, TResult>> expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var body = ResultAdapter.Adapt(expression.Body, typeof(TNewResult), "projection result");
        return Expression.Lambda<Func<TSource, TNewResult>>(body, expression.Parameters);
    }

    public static Expression<Func<TNewSource, TNewResult>> Adapt<TSource, TResult, TNewSource, TNewResult>(
        Expression<Func<TSource, TResult>> expression)
    {
        var sourceAdapted = AdaptSource<TSource, TResult, TNewSource>(expression);
        return AdaptResult<TNewSource, TResult, TNewResult>(sourceAdapted);
    }

    private sealed class SourceVisitor(
        ParameterExpression oldParameter,
        ParameterExpression newParameter) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (!TryGetSourcePath(node, out var path))
            {
                return base.VisitMember(node);
            }

            var current = RewritePath(path);

            if (current.Type == node.Type) return current;
            if (node.Type.IsAssignableFrom(current.Type))
            {
                return Expression.Convert(current, node.Type);
            }

            throw new InvalidOperationException(
                $"Adapted source member path '{FormatPath(path)}' has incompatible type '{current.Type.FullName}' on " +
                $"'{newParameter.Type.FullName}'; expected '{node.Type.FullName}'.");
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.Method == null &&
                (node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual))
            {
                if (TryAdaptNullComparison(node.Left, node.Right, node.NodeType, out var adapted) ||
                    TryAdaptNullComparison(node.Right, node.Left, node.NodeType, out adapted))
                {
                    return adapted;
                }
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == oldParameter)
            {
                throw new NotSupportedException(
                    $"Cannot structurally adapt expression parameter '{oldParameter.Name}' from " +
                    $"'{oldParameter.Type.FullName}' to '{newParameter.Type.FullName}' because it is used directly. " +
                    "Source adaptation supports parameter-rooted public property and field access only.");
            }

            return base.VisitParameter(node);
        }

        private bool TryGetSourcePath(MemberExpression node, out IReadOnlyList<MemberInfo> path)
        {
            var members = new Stack<MemberInfo>();
            Expression? current = node;
            while (current is MemberExpression member)
            {
                members.Push(member.Member);
                current = member.Expression;
            }

            if (current == oldParameter)
            {
                path = members.ToArray();
                return true;
            }

            path = [];
            return false;
        }

        private Expression RewritePath(IEnumerable<MemberInfo> path)
        {
            Expression current = newParameter;
            foreach (var sourceMember in path)
            {
                var targetMember = ResolveMember(
                    current.Type,
                    sourceMember.Name,
                    requireReadable: true,
                    requireWritable: false,
                    $"source member corresponding to '{sourceMember.DeclaringType?.FullName}.{sourceMember.Name}'");
                current = Expression.MakeMemberAccess(current, targetMember);
            }

            return current;
        }

        private bool TryAdaptNullComparison(
            Expression possibleMember,
            Expression possibleNull,
            ExpressionType nodeType,
            out Expression adapted)
        {
            if (possibleMember is MemberExpression member &&
                IsNullOrDefault(possibleNull) &&
                TryGetSourcePath(member, out var path))
            {
                var reboundMember = RewritePath(path);
                if (!CanBeNull(reboundMember.Type))
                {
                    throw new InvalidOperationException(
                        $"Adapted source member path '{FormatPath(path)}' has non-nullable type " +
                        $"'{reboundMember.Type.FullName}' and cannot preserve a null comparison.");
                }

                adapted = Expression.MakeBinary(
                    nodeType,
                    reboundMember,
                    Expression.Default(reboundMember.Type));
                return true;
            }

            adapted = null!;
            return false;
        }

        private static string FormatPath(IEnumerable<MemberInfo> path) =>
            string.Join(".", path.Select(member => member.Name));
    }

    private static class ResultAdapter
    {
        public static Expression Adapt(Expression expression, Type targetType, string context)
        {
            if (expression.Type == targetType) return expression;

            if (expression is MemberInitExpression memberInit)
            {
                return AdaptMemberInit(memberInit, targetType, context);
            }

            if (expression is ConditionalExpression conditional)
            {
                return Expression.Condition(
                    conditional.Test,
                    AdaptBranch(conditional.IfTrue, targetType, context + " true branch"),
                    AdaptBranch(conditional.IfFalse, targetType, context + " false branch"));
            }

            if (IsNullOrDefault(expression) && CanBeNull(targetType))
            {
                return Expression.Default(targetType);
            }

            throw new NotSupportedException(
                $"Cannot adapt {context} from '{expression.Type.FullName}' to '{targetType.FullName}'. " +
                $"Result adaptation requires a parameterless member initializer; found '{expression.NodeType}'.");
        }

        private static Expression AdaptBranch(Expression expression, Type targetType, string context)
        {
            return IsNullOrDefault(expression)
                ? Expression.Default(targetType)
                : Adapt(expression, targetType, context);
        }

        private static MemberInitExpression AdaptMemberInit(
            MemberInitExpression source,
            Type targetType,
            string context)
        {
            if (source.NewExpression.Arguments.Count != 0)
            {
                throw new NotSupportedException(
                    $"Cannot adapt {context} because its constructor has arguments. " +
                    "Result adaptation supports parameterless member initializers only.");
            }

            var creation = CreateNewExpression(targetType, context);
            var bindings = new List<MemberBinding>(source.Bindings.Count);
            var boundMembers = new HashSet<MemberInfo>();

            foreach (var binding in source.Bindings)
            {
                if (binding is not MemberAssignment assignment)
                {
                    throw new NotSupportedException(
                        $"Cannot adapt {context} member '{binding.Member.Name}' because binding type " +
                        $"'{binding.BindingType}' is unsupported. Result adaptation supports simple assignments only.");
                }

                var targetMember = ResolveMember(
                    targetType,
                    assignment.Member.Name,
                    requireReadable: false,
                    requireWritable: true,
                    $"result member corresponding to '{assignment.Member.DeclaringType?.FullName}.{assignment.Member.Name}'");
                if (!boundMembers.Add(targetMember))
                {
                    throw new InvalidOperationException(
                        $"Multiple source bindings resolve to result member '{targetType.FullName}.{targetMember.Name}'.");
                }

                var targetMemberType = GetMemberType(targetMember);
                var value = AdaptAssignedValue(
                    assignment.Expression,
                    targetMemberType,
                    $"result member '{targetType.FullName}.{targetMember.Name}'");
                bindings.Add(Expression.Bind(targetMember, value));
            }

            return Expression.MemberInit(creation, bindings);
        }

        private static Expression AdaptAssignedValue(Expression value, Type targetType, string context)
        {
            if (value.Type == targetType) return value;

            if (value is MemberInitExpression || value is ConditionalExpression)
            {
                return Adapt(value, targetType, context);
            }

            if (IsNullOrDefault(value) && CanBeNull(targetType))
            {
                return Expression.Default(targetType);
            }

            if (targetType.IsAssignableFrom(value.Type))
            {
                return Expression.Convert(value, targetType);
            }

            throw new InvalidOperationException(
                $"Cannot assign expression of type '{value.Type.FullName}' to adapted {context} of type " +
                $"'{targetType.FullName}'.");
        }

        private static NewExpression CreateNewExpression(Type targetType, string context)
        {
            if (targetType.IsValueType) return Expression.New(targetType);

            if (targetType.IsAbstract || targetType.IsInterface)
            {
                throw new NotSupportedException(
                    $"Cannot adapt {context} to non-constructible type '{targetType.FullName}'.");
            }

            var constructor = targetType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
            {
                throw new NotSupportedException(
                    $"Cannot adapt {context} to '{targetType.FullName}' because it has no public parameterless constructor.");
            }

            return Expression.New(constructor);
        }
    }

    private static MemberInfo ResolveMember(
        Type targetType,
        string name,
        bool requireReadable,
        bool requireWritable,
        string description)
    {
        var candidates = targetType
            .GetMember(name, BindingFlags.Instance | BindingFlags.Public)
            .Where(member => member is FieldInfo || member is PropertyInfo property && property.GetIndexParameters().Length == 0)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Type '{targetType.FullName}' is missing public {description}.");
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                $"Type '{targetType.FullName}' exposes multiple public members named '{name}' while resolving {description}.");
        }

        var member = candidates[0];
        if (requireReadable && member is PropertyInfo propertyInfo &&
            (propertyInfo.GetMethod == null || !propertyInfo.GetMethod.IsPublic))
        {
            throw new InvalidOperationException(
                $"Property '{targetType.FullName}.{name}' is not readable while resolving {description}.");
        }

        if (requireWritable)
        {
            if (member is PropertyInfo property && (property.SetMethod == null || !property.SetMethod.IsPublic))
            {
                throw new InvalidOperationException(
                    $"Property '{targetType.FullName}.{name}' is not publicly writable while resolving {description}.");
            }

            if (member is FieldInfo { IsInitOnly: true } || member is FieldInfo { IsLiteral: true })
            {
                throw new InvalidOperationException(
                    $"Field '{targetType.FullName}.{name}' is not writable while resolving {description}.");
            }
        }

        return member;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new NotSupportedException(
                $"Member '{member.DeclaringType?.FullName}.{member.Name}' is not a property or field.")
        };
    }

    private static bool IsNullOrDefault(Expression expression) =>
        expression is DefaultExpression || expression is ConstantExpression { Value: null };

    private static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
}
