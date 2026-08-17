using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

/// <summary>Creates structural expression templates from a sample type.</summary>
public static class ExpressionTemplate<TSample>
{
    /// <summary>Creates a Boolean structural template.</summary>
    public static ExpressionTemplate<TSample, TTemplate> Create<TTemplate>(
        Expression<Func<TSample, TTemplate>> template,
        Expression<Func<TTemplate, bool>> expression)
    {
        return new ExpressionTemplate<TSample, TTemplate>(template, expression);
    }
}

/// <summary>Adapts a Boolean expression over a selected structural shape to compatible target types.</summary>
/// <param name="template">The sample shape selector.</param>
/// <param name="expression">The Boolean expression written over the structural shape.</param>
public class ExpressionTemplate<TSample, TTemplate>(
    Expression<Func<TSample, TTemplate>> template,
    Expression<Func<TTemplate, bool>> expression)
{
    private readonly Dictionary<MemberInfo, TemplateMember> _members =
        TemplateShapeReader.Read(template ?? throw new ArgumentNullException(nameof(template)));

    /// <summary>Gets the sample shape selector.</summary>
    public Expression<Func<TSample, TTemplate>> Template { get; } = template;

    /// <summary>Gets the expression written over the structural shape.</summary>
    public Expression<Func<TTemplate, bool>> Expression { get; } =
        expression ?? throw new ArgumentNullException(nameof(expression));

    /// <summary>Adapts this template to a compatible target type.</summary>
    public Spec<TTarget> AdaptSpec<TTarget>(string? newParameterName = null)
    {
        var oldParameter = Expression.Parameters[0];
        var newParameter = System.Linq.Expressions.Expression.Parameter(
            typeof(TTarget),
            newParameterName ?? oldParameter.Name);
        foreach (var requirement in _members.Values)
        {
            TemplateAdaptationVisitor.ResolveTargetMember(typeof(TTarget), requirement);
        }

        var visitor = new TemplateAdaptationVisitor(oldParameter, newParameter, _members);
        var body = visitor.Visit(Expression.Body)!;
        return Spec<TTarget>.Create(
            System.Linq.Expressions.Expression.Lambda<Func<TTarget, bool>>(body, newParameter));
    }

    /// <summary>Compatibility alias for <see cref="AdaptSpec{TTarget}(string)"/>.</summary>
    public Spec<TTarget> Adapt<TTarget>(string? newParameterName = null) =>
        AdaptSpec<TTarget>(newParameterName);

    private sealed class TemplateAdaptationVisitor(
        ParameterExpression oldParameter,
        ParameterExpression newParameter,
        Dictionary<MemberInfo, TemplateMember> members)
        : ExpressionVisitor
    {
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            if (node.Parameters.Contains(oldParameter)) return node;
            return base.VisitLambda(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != oldParameter)
            {
                return base.VisitMember(node);
            }

            if (!members.TryGetValue(node.Member, out var requirement))
            {
                throw new InvalidOperationException(
                    $"Expression template uses member '{node.Member.Name}' that is not supplied by its template selector.");
            }

            var targetMember = ResolveTargetMember(newParameter.Type, requirement);
            var access = System.Linq.Expressions.Expression.MakeMemberAccess(newParameter, targetMember);
            if (access.Type == node.Type) return access;

            if (node.Type.IsAssignableFrom(access.Type))
            {
                return System.Linq.Expressions.Expression.Convert(access, node.Type);
            }

            throw new InvalidOperationException(
                $"Target member '{newParameter.Type.FullName}.{requirement.Name}' has incompatible type '{access.Type.FullName}'; expected '{node.Type.FullName}'.");
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == oldParameter)
            {
                throw new NotSupportedException(
                    "Expression templates may use the template parameter only through members selected by the template shape.");
            }

            return base.VisitParameter(node);
        }

        internal static MemberInfo ResolveTargetMember(Type targetType, TemplateMember requirement)
        {
            var candidates = targetType
                .GetMember(requirement.Name, BindingFlags.Instance | BindingFlags.Public)
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Target type '{targetType.FullName}' is missing required member '{requirement.Name}'.");
            }

            if (candidates.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Target type '{targetType.FullName}' exposes multiple public instance members named '{requirement.Name}'.");
            }

            var member = candidates[0];
            Type memberType = member switch
            {
                PropertyInfo property when property.GetMethod != null && property.GetMethod.IsPublic => property
                    .PropertyType,
                PropertyInfo => throw new InvalidOperationException(
                    $"Target property '{targetType.FullName}.{requirement.Name}' is not publicly readable."),
                FieldInfo field => field.FieldType,
                _ => throw new InvalidOperationException(
                    $"Target member '{targetType.FullName}.{requirement.Name}' is not a readable property or field.")
            };

            if (memberType != requirement.Type && !requirement.Type.IsAssignableFrom(memberType))
            {
                throw new InvalidOperationException(
                    $"Target member '{targetType.FullName}.{requirement.Name}' has incompatible type '{memberType.FullName}'; expected '{requirement.Type.FullName}'.");
            }

            return member;
        }
    }

    private static class TemplateShapeReader
    {
        public static Dictionary<MemberInfo, TemplateMember> Read(
            Expression<Func<TSample, TTemplate>> template)
        {
            var result = new Dictionary<MemberInfo, TemplateMember>();

            switch (template.Body)
            {
                case NewExpression created when created.Members != null:
                    for (var index = 0; index < created.Arguments.Count; index++)
                    {
                        Add(result, created.Members[index], created.Arguments[index], template.Parameters[0]);
                    }

                    break;

                case MemberInitExpression initialized:
                    foreach (var binding in initialized.Bindings)
                    {
                        if (binding is not MemberAssignment assignment)
                        {
                            throw new ArgumentException(
                                $"Expression-template shape member '{binding.Member.Name}' must use a simple assignment.",
                                nameof(template));
                        }

                        Add(result, assignment.Member, assignment.Expression, template.Parameters[0]);
                    }

                    break;

                default:
                    throw new ArgumentException(
                        "Expression-template shape must be an anonymous/object constructor with named members or a member initializer.",
                        nameof(template));
            }

            if (result.Count == 0)
            {
                throw new ArgumentException("Expression-template shape must select at least one member.", nameof(template));
            }

            return result;
        }

        private static void Add(
            Dictionary<MemberInfo, TemplateMember> result,
            MemberInfo shapeMember,
            Expression source,
            ParameterExpression sampleParameter)
        {
            source = StripConvert(source);
            if (source is not MemberExpression sourceMember || sourceMember.Expression != sampleParameter)
            {
                throw new ArgumentException(
                    $"Expression-template shape member '{shapeMember.Name}' must be assigned from a directly readable sample member.",
                    nameof(source));
            }

            if (shapeMember.Name != sourceMember.Member.Name)
            {
                throw new ArgumentException(
                    $"Expression-template shape member '{shapeMember.Name}' must retain source member name '{sourceMember.Member.Name}'.",
                    nameof(source));
            }

            if (result.ContainsKey(shapeMember))
            {
                throw new ArgumentException(
                    $"Expression-template shape contains duplicate member '{shapeMember.Name}'.",
                    nameof(source));
            }

            result.Add(shapeMember, new TemplateMember(shapeMember.Name, sourceMember.Type));
        }

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
            {
                expression = unary.Operand;
            }

            return expression;
        }
    }

    internal sealed class TemplateMember(string name, Type type)
    {
        public string Name { get; } = name;

        public Type Type { get; } = type;
    }
}
