using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class MapToExistingBuilder
{
    private static readonly ConstructorInfo InvalidOperationConstructor =
        typeof(InvalidOperationException).GetConstructor([typeof(string)])!;

    public static Expression<Action<TIn, TOut>> Build<TIn, TOut>(
        Expression<Func<TIn, TOut>> projection)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (typeof(TOut).IsValueType)
        {
            throw new NotSupportedException(
                $"MapToExisting does not support value-type destination '{typeof(TOut).FullName}'.");
        }

        var existing = Expression.Parameter(typeof(TOut),
            projection.Parameters[0].Name == "existing" ? "existing1" : "existing");
        var body = BuildRoot(projection.Body, existing);
        return Expression.Lambda<Action<TIn, TOut>>(
            body,
            projection.Parameters[0],
            existing);
    }

    private static Expression BuildRoot(Expression source, Expression destination)
    {
        return source switch
        {
            MemberInitExpression memberInit => BuildMemberInit(memberInit, destination),
            ConditionalExpression conditional => Expression.IfThenElse(conditional.Test,
                BuildRootBranch(conditional.IfTrue, destination), BuildRootBranch(conditional.IfFalse, destination)),
            _ => throw new NotSupportedException(
                $"MapToExisting does not support projection body node '{source.NodeType}'. A member initializer is required.")
        };
    }

    private static Expression BuildRootBranch(Expression source, Expression destination)
    {
        if (source is MemberInitExpression memberInit)
        {
            return BuildMemberInit(memberInit, destination);
        }

        if (IsNullOrDefault(source))
        {
            return Throw(
                "MapToExisting cannot update an existing root destination when the selected projection branch returns null.");
        }

        throw new NotSupportedException(
            $"MapToExisting conditional branches must be member initializers or null/default; found '{source.NodeType}'.");
    }

    private static Expression BuildMemberInit(MemberInitExpression source, Expression destination)
    {
        var updates = new List<Expression>();
        foreach (var binding in source.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw new NotSupportedException(
                    $"MapToExisting supports MemberAssignment bindings only; member '{binding.Member.Name}' uses '{binding.BindingType}'.");
            }

            var target = Expression.MakeMemberAccess(destination, assignment.Member);
            updates.Add(BuildMemberUpdate(target, assignment.Expression, assignment.Member));
        }

        return updates.Count == 0 ? Expression.Empty() : Expression.Block(updates);
    }

    private static Expression BuildMemberUpdate(
        Expression target,
        Expression source,
        MemberInfo destinationMember)
    {
        if (source is MemberInitExpression memberInit)
        {
            if (target.Type.IsValueType)
            {
                throw new NotSupportedException(
                    $"MapToExisting does not recursively update value-type member '{destinationMember.Name}'.");
            }

            var update = BuildMemberInit(memberInit, target);
            return Expression.IfThenElse(
                Expression.Equal(target, Expression.Default(target.Type)),
                Throw(
                    $"Cannot update nested destination member '{destinationMember.DeclaringType?.FullName}.{destinationMember.Name}' because its current value is null."),
                update);
        }

        if (source is ConditionalExpression conditional)
        {
            return Expression.IfThenElse(
                conditional.Test,
                BuildMemberUpdate(target, conditional.IfTrue, destinationMember),
                BuildMemberUpdate(target, conditional.IfFalse, destinationMember));
        }

        EnsureWritable(destinationMember);
        return Expression.Assign(target, source);
    }

    private static void EnsureWritable(MemberInfo member)
    {
        if (member is PropertyInfo { SetMethod: null })
        {
            throw new NotSupportedException(
                $"MapToExisting cannot assign read-only property '{member.DeclaringType?.FullName}.{member.Name}'.");
        }

        if (member is FieldInfo { IsInitOnly: true })
        {
            throw new NotSupportedException(
                $"MapToExisting cannot assign readonly field '{member.DeclaringType?.FullName}.{member.Name}'.");
        }
    }

    private static UnaryExpression Throw(string message) =>
        Expression.Throw(Expression.New(InvalidOperationConstructor, Expression.Constant(message)));

    private static bool IsNullOrDefault(Expression expression) =>
        expression is DefaultExpression || expression is ConstantExpression { Value: null };
}
