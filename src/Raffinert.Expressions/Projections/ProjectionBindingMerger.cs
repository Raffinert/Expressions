using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class ProjectionBindingMerger
{
    public static Proj<TIn, TOut> Merge<TIn, TOut>(
        Proj<TIn, TOut> first,
        Proj<TIn, TOut> second,
        BindingConflictBehavior conflictBehavior)
    {
        if (conflictBehavior != BindingConflictBehavior.UseLast &&
            conflictBehavior != BindingConflictBehavior.UseFirst &&
            conflictBehavior != BindingConflictBehavior.Throw)
        {
            throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
        }

        var firstExpression = first.GetExpandedExpression();
        var secondExpression = second.GetExpandedExpression();
        var secondBody = new ReplaceExpressionVisitor(
                secondExpression.Parameters[0],
                firstExpression.Parameters[0])
            .Visit(secondExpression.Body)!;

        var firstShape = ExtractShape(firstExpression.Body, "first");
        var secondShape = ExtractShape(secondBody, "second");
        EnsureCompatibleConstruction(firstShape.Creation, secondShape.Creation);

        var ordered = new List<MemberInfo>(firstShape.Order);
        var expressions = new Dictionary<MemberInfo, Expression>();
        foreach (var member in firstShape.Order)
        {
            expressions.Add(member, firstShape.Assignments[member]);
        }

        foreach (var member in secondShape.Order)
        {
            if (!expressions.ContainsKey(member))
            {
                ordered.Add(member);
                expressions.Add(member, secondShape.Assignments[member]);
                continue;
            }

            switch (conflictBehavior)
            {
                case BindingConflictBehavior.UseFirst:
                    break;
                case BindingConflictBehavior.UseLast:
                    expressions[member] = secondShape.Assignments[member];
                    break;
                case BindingConflictBehavior.Throw:
                    throw new InvalidOperationException(
                        $"Both projections bind destination member '{member.DeclaringType?.FullName}.{member.Name}'.");
            }
        }

        var bindings = ordered.ConvertAll(member =>
            (MemberBinding)Expression.Bind(member, expressions[member]));
        var body = Expression.MemberInit(firstShape.Creation, bindings);
        return Proj<TIn, TOut>.Create(
            Expression.Lambda<Func<TIn, TOut>>(body, firstExpression.Parameters));
    }

    private static ProjectionShape ExtractShape(Expression body, string side)
    {
        if (body is MemberInitExpression memberInit)
        {
            var assignments = new Dictionary<MemberInfo, Expression>();
            var order = new List<MemberInfo>();
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is not MemberAssignment assignment)
                {
                    throw new NotSupportedException(
                        $"MergeBindings supports MemberAssignment bindings only; member '{binding.Member.Name}' uses '{binding.BindingType}'.");
                }

                if (assignments.ContainsKey(binding.Member))
                {
                    throw new InvalidOperationException(
                        $"The {side} projection binds member '{binding.Member.Name}' more than once.");
                }

                order.Add(binding.Member);
                assignments.Add(binding.Member, assignment.Expression);
            }

            return new ProjectionShape(memberInit.NewExpression, order, assignments);
        }

        if (body is ConditionalExpression conditional)
        {
            return ExtractConditionalShape(conditional, side);
        }

        throw new NotSupportedException(
            $"MergeBindings requires a MemberInitExpression or a compatible ConditionalExpression; the {side} projection uses '{body.NodeType}'.");
    }

    private static ProjectionShape ExtractConditionalShape(ConditionalExpression conditional, string side)
    {
        var trueIsDefault = IsNullOrDefault(conditional.IfTrue);
        var falseIsDefault = IsNullOrDefault(conditional.IfFalse);

        if (trueIsDefault && falseIsDefault)
        {
            throw new InvalidOperationException(
                $"The {side} projection conditional has no member-initializer branch.");
        }

        var trueShape = trueIsDefault ? null : ExtractShape(conditional.IfTrue, side + " true branch");
        var falseShape = falseIsDefault ? null : ExtractShape(conditional.IfFalse, side + " false branch");
        var creation = trueShape?.Creation ?? falseShape!.Creation;

        if (trueShape != null && falseShape != null)
        {
            EnsureCompatibleConstruction(trueShape.Creation, falseShape.Creation);
            EnsureSameMembers(trueShape, falseShape, side);
        }

        var reference = trueShape ?? falseShape!;
        var assignments = new Dictionary<MemberInfo, Expression>();
        foreach (var member in reference.Order)
        {
            var memberType = GetMemberType(member);
            var ifTrue = trueShape != null
                ? trueShape.Assignments[member]
                : Expression.Default(memberType);
            var ifFalse = falseShape != null
                ? falseShape.Assignments[member]
                : Expression.Default(memberType);
            assignments.Add(member, Expression.Condition(conditional.Test, ifTrue, ifFalse));
        }

        return new ProjectionShape(creation, reference.Order, assignments);
    }

    private static void EnsureSameMembers(ProjectionShape first, ProjectionShape second, string side)
    {
        foreach (var member in first.Order)
        {
            if (!second.Assignments.ContainsKey(member))
            {
                throw new InvalidOperationException(
                    $"Projection branches bind incompatible members: the {side} false branch is missing '{member.Name}'.");
            }
        }

        foreach (var member in second.Order)
        {
            if (!first.Assignments.ContainsKey(member))
            {
                throw new InvalidOperationException(
                    $"Projection branches bind incompatible members: the {side} true branch is missing '{member.Name}'.");
            }
        }
    }

    private static void EnsureCompatibleConstruction(NewExpression first, NewExpression second)
    {
        if (first.Type != second.Type || first.Constructor != second.Constructor || first.Arguments.Count != second.Arguments.Count)
        {
            throw new InvalidOperationException(
                "MergeBindings projections use incompatible destination constructors.");
        }

        if (first.Arguments.Count != 0)
        {
            throw new NotSupportedException(
                "MergeBindings currently requires member initializers with a parameterless destination constructor.");
        }
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new NotSupportedException($"Member '{member.Name}' is not a property or field.")
        };
    }

    private static bool IsNullOrDefault(Expression expression) =>
        expression is DefaultExpression || expression is ConstantExpression { Value: null };

    private sealed class ProjectionShape(
        NewExpression creation,
        List<MemberInfo> order,
        Dictionary<MemberInfo, Expression> assignments)
    {
        public NewExpression Creation { get; } = creation;

        public List<MemberInfo> Order { get; } = order;

        public Dictionary<MemberInfo, Expression> Assignments { get; } = assignments;
    }
}
